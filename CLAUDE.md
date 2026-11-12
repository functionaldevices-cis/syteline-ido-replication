# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

AES-driven, IDO-layer data replication for Infor CloudSuite Industrial (Syteline), built on the Mongoose framework. Administrators define replication rules (which IDO and which events), credentials for external targets, and field-level maps and transformations. When a record changes, the rule's IDO extension class loads the record, remaps it, and pushes it to the target. Working targets are Azure Event Hubs (including Fabric Eventstreams through a custom endpoint) and the Salesforce REST API. The project is pre-beta.

Nothing here runs locally and there is no test suite. Changes are deployed into Syteline through Mongoose tooling.

## Naming

Everything uses the `AIR` prefix: tables `ue_AIR_idorepl_*_mst`, IDOs `ue_AIR_IDOReplication*` on IDO server `ue_AIR`, forms `ue_AIR_IDOReplication*`, assemblies `ue_AIR_*_ECA`. The README still lists the older `ue_FDI_` names. Treat `ue_AIR_` as current.

## Layout

- `dist/` holds the install package. The numbered folders are the install order:
  1. `1 - SQL (Paste Rows Append)/`: `SQL Tables.csv` and `SQL Columns.csv`, pasted as appended rows into Syteline's SQL Tables/Columns forms to create the four `_mst` tables.
  2. `2 - IDOs and Extension Classes (IDO Import Wizard)/IDOs and Extension Classes.xml`: an `<ObjectStudioExport>` with the four IDOs. Only `ue_AIR_IDOReplicationRules` binds an extension class (`ExtClassNamespace`/`ExtClassName` = `ue_AIR_IDOReplicationRules_ECA`) and exposes its methods.
  3. `3 - Forms (FormSync)/*.XML`: FormSync exports of the Rules and Credentials forms. Form scripts are embedded as `<ScriptLine>` elements.

  These files come from Mongoose export tools. Edit them minimally and keep the exporter's formatting: 3-space indentation, element order, and any UTF-8 BOM.
- `src/IDO Extension Class Custom Assemblies/` holds two .NET Framework 4.8 C# class library solutions. They reference Infor DLLs from the sibling `Lib/` folder (`..\..\Lib\*.dll` HintPaths) and use NuGet `Newtonsoft.Json` and `System.ValueTuple` via `packages.config`.
  - `ue_AIR_IDOReplicationRules_ECA`: the assembly the IDO export uses. IDO methods: `ue_ReplicateRecordToExternalSystems` (the replication entry point), `ue_UpdateAESTriggers` (creates `EventHandlers` records for `IdoPostItemInsert/Update/Delete` on a target IDO; the `Remove*` branches are still stubs), `ue_ParseSavedMap`, and the custom-load `ue_LoadOutputPreview`.
  - `ue_AIR_IDOReplicationConfig_ECA`: a parallel assembly with a different `ue_UpdateAESTriggers` signature (`ref string results`). It is not referenced by the current IDO export.
  - **`Helpers/` and `Models/` are duplicated in both projects and are identical except for the namespace.** When you change a helper or model in one project, apply the same change to the other.

## How replication works (Rules ECA)

1. An AES event handler on the source IDO calls `ue_ReplicateRecordToExternalSystems` with the IDO name, a query filter, a record cap, and optionally a rule number. It returns early when there are no active rules.
2. `LoadReplicationRulesAndMaps` loads the active rules for that IDO along with their map fields and sources.
3. The union of each rule's source properties is loaded once through `SytelineInternalAPI.LoadRecords`, which wraps `this.Context.Commands`. Data access goes through IDOs, not direct SQL.
4. For each rule, `RemapRecords` applies the field map and transforms (`Models/ReplicationFieldTransform*`). The result is sent based on `TargetType`:
   - `AzureEventHub`: `AzureEventHubPusher`, using `CredentialValue01` as the connection string.
   - `SalesforceRESTAPI`: `SalesforceRestAPI.UpsertRecords`, using `CredentialValue01`–`05` (client ID and secret, username, password, security token), `Option01` as the sObject, and `Option02` as the external ID field.
5. Logging goes through `Utilities.WriteLogMessage`, tied to `BGTaskNum` when the method runs as a background task.

To add a new target type, add a `case` in that `switch`, a helper and models under the target's own folder, and matching credential/option semantics in the Credentials and Rules forms. Do this in both projects for any shared helpers or models.

## Building

You need Windows with Visual Studio/MSBuild and the .NET Framework 4.8 targeting pack, and you have to restore NuGet packages first. The .NET SDK and MSBuild aren't available in this Linux container, so you can't compile or validate changes here. Build each solution on its own, for example:

```
nuget restore "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln"
msbuild "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln" /p:Configuration=Release
```

Upload the built DLL to Syteline (IDO Extension Class Assemblies). If you change method signatures, update the IDO export in `dist/` to match. `bin/` and `obj/` are gitignored, so don't commit build output.
