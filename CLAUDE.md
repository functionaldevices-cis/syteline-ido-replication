# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

AES-driven, IDO-layer data replication for Infor CloudSuite Industrial (Syteline), built on the Mongoose framework. Administrators define replication rules (which IDO and which events), credentials for external targets, and field-level maps and transformations. When a record changes, the rule's IDO extension class loads the record, remaps it, and pushes it to the target. Working targets are Azure Event Hubs (including Fabric Eventstreams through a custom endpoint) and the Salesforce REST API. The project is pre-beta.

Nothing here runs locally and there is no test suite. Changes are deployed into Syteline through Mongoose tooling.

This is an MIT-licensed, open-source project meant for any company's Syteline instance. Don't hard-code company-specific values (URLs, names, sites). Make them settings on a credential or rule instead.

## Naming

Everything uses the `AIR` prefix: tables `ue_AIR_idorepl_*_mst`, IDOs `ue_AIR_*` on IDO server `ue_AIR`, forms `ue_AIR_*`, assemblies `ue_AIR_*_ECA`. The README still lists the older `ue_FDI_` names. Treat `ue_AIR_` as current.

## Layout

- `dist/` holds the install package. The numbered folders are the install order:
  1. `1 - SQL (Paste Rows Append)/`: `SQL Tables.csv` and `SQL Columns.csv`, pasted as appended rows into Syteline's SQL Tables/Columns forms to create the five `_mst` tables. Each table also gets a view of the same name without `_mst`, and that view is what the IDO binds as its table.
  2. `2 - IDOs and Extension Classes (IDO Import Wizard)/IDOs and Extension Classes.xml`: an `<ObjectStudioExport>` with the IDOs. The `<CustomAssemblies>` section at the end embeds both compiled extension class DLLs as base64 (`<AssemblyImage>`), so this file changes whenever an assembly is re-uploaded. Never edit those images by hand.
     - `ue_AIR_IDOReplicationRules` binds `ue_AIR_IDOReplicationRules_ECA` and exposes `ue_ReplicateRecordToExternalSystems` and the custom-load `ue_LoadOutputPreview`.
     - `ue_AIR_IDOReplicationConfigs` extends the standard `IdoCollections` IDO (no table of its own, one row per IDO collection). It binds `ue_AIR_IDOReplicationConfig_ECA` and exposes `ue_UpdateAESTriggers`.
     - `ue_AIR_IDOReplicationCredentials`, `ue_AIR_IDOReplicationMapFields`, `ue_AIR_IDOReplicationMapFieldSources`, `ue_AIR_IDOReplicationLogs` and `ue_AIR_EventHandlers` have no extension class.
     - `ue_AIR_IDOReplicationLogs` (table `ue_AIR_idorepl_log_mst`) is the replication log: one row per record per rule, with the operation, the payload as sent (JSON, up to 4,000 characters), the status (`Succeeded`, `Failed`, `Skipped`), the error, and `retries_remaining` (default 3) for the retry task. Nothing writes to it yet.
  3. `3 - Mongoose Objects (FormSync)/*.XML`: FormSync exports of the forms (`ue_AIR_IDOReplicationRules`, `ue_AIR_IDOReplicationCredentials`, `ue_AIR_IDOReplicationConfigs`, `ue_AIR_ImportJob`, `ue_AIR_JobFileInputWindow`) plus `Strings.XML`. Form scripts are embedded as `<ScriptLine>` elements.

  These files come from Mongoose export tools. Edit them minimally and keep the exporter's formatting: 3-space indentation, element order, and any UTF-8 BOM.

  When you hand-edit an IDO in the IDO export, also bump that IDO's `<RevisionNo>` by 1 and set its `<RevisionDate>` to the current local (Eastern) time in the same format, for example `2026-11-13T13:28:05`. Otherwise the IDO Import Wizard may not treat it as changed. After importing, use **Refresh Form Metadata** to clear the IDO cache. After changing an extension class method's signature, use **Resync Parameters** on the IDO method, which reads the parameters from the uploaded DLL, instead of editing them by hand.
- `src/IDO Extension Class Custom Assemblies/` holds two .NET Framework 4.8 C# class library solutions. They reference Infor DLLs from the sibling `Lib/` folder (`..\..\Lib\*.dll` HintPaths) and use NuGet `Newtonsoft.Json` and `System.ValueTuple` via `packages.config`. The language version is the .NET Framework default (C# 7.3), so avoid newer syntax such as collection expressions (`[]`), target-typed `new()`, `??=`, nullable annotations and file-scoped namespaces.
  - `ue_AIR_IDOReplicationRules_ECA` replicates data. Its IDO methods are `ue_ReplicateRecordToExternalSystems` (the entry point) and `ue_LoadOutputPreview`. `ue_ParseSavedMap` exists in the class but is not exposed by the IDO yet.
  - `ue_AIR_IDOReplicationConfig_ECA` manages triggers. `ue_UpdateAESTriggers(idoName, action, ref results)` takes `SetupTriggers`, `RemoveTriggers`, `FixTriggers`, `ActivateAllTriggers` or `DeactivateAllTriggers`, called from the Configs form. Setup creates `EventHandlers` (description `ue_AIR_ReplicationTrigger`) for insert, update and delete on the target IDO, with `EventActions` that call the Rules IDO's replication method with `PARMS(-1, <RowPointer filter>, <IDO name>, 1, "", <operation>)`, where the operation is `Insert`, `Update` or `Delete` for that event. It inserts and then deletes a temporary `CustomAIR` handler to work around a sequencing bug when inserting several handlers at once. Two derived properties compare each action's `Parameters` string exactly, so any change to that string in `SetupTriggers` (or to `OperationForEvent`) must also be made in both expressions, and existing triggers need **Fix Triggers**:
    - `ue_AIR_IDOReplicationConfigs.ue_EventHandlersAreSetup`, one comparison per event. It drives the Setup/Broken status on the Configs form.
    - `ue_AIR_EventHandlers.ue_IsSetup`, one comparison that picks the operation from the handler's `EventName`.
  - **`Helpers/` and `Models/` are duplicated in both projects and are identical except for the namespace.** When you change a helper or model in one project, apply the same change to the other. Keep the two assemblies separate. The plan is to move the helpers into a separate shared extension class assembly later, not to merge these two.

## How replication works (Rules ECA)

1. A trigger created by the Config assembly calls `ue_ReplicateRecordToExternalSystems` with the IDO name, a query filter, a record cap, an optional rule number (blank means every active rule) and an optional operation (`Insert`, `Update`, `Delete`, or blank when unknown, as from background tasks). It returns early when there are no active rules.
2. `LoadReplicationRulesAndMaps` loads the active rules for that IDO along with their map fields. Each map field has a single source: `Name`, `SourceType` (`IDOProperty` or `Literal`), `SourceValue` and `SourceTransformation` on `ue_AIR_IDOReplicationMapFields`. Replication does not use the `Sources` subcollection or the derived `JSON` property.
3. The union of each rule's source properties is loaded once through `SytelineInternalAPI.LoadRecords`, which wraps `this.Context.Commands`. Data access goes through IDOs, not direct SQL.
4. For each rule, `RemapRecords` builds the output records from the field map. Transform types are defined in `Models/ReplicationFieldTransform*`. The result is sent based on `TargetType`:
   - `AzureEventHub`: `AzureEventHubPusher.Push`, using `CredentialValue01` as the connection string, `CredentialValue02` as the event source and `CredentialValue03` as an optional `MaxBatchKBOverride`. `Option01` is an optional Fabric Event Schema URI, `Option02` is the field used as the record ID in failure messages, and `Option03` (`OperationFieldName`) is an optional body field that receives the operation. When the operation is known, every event also carries it as the `operation` user property, which Fabric keeps in event metadata only when the Event Hub source uses **Extended features** (read it with `GETMETADATAPROPERTYVALUE` in a SQL operator). The body field is off by default because Event Schema Eventstreams reject fields their schema doesn't define. When `Option01` is set, each event carries CloudEvents user properties (`type` = schema name, `source`, `dataschema` = the URI), which Fabric Event Schema Eventstreams require. Without it, plain events are sent. The batch size limit depends on the namespace tier (256 KB Basic, 1 MB Standard and up). Batches default to about 220 KB, which is safe for every tier, and `MaxBatchKBOverride` can raise that to at most 850. Only `201 Created` counts as accepted.
   - `SalesforceRESTAPI`: `SalesforceRestAPI.UpsertRecords`, using `CredentialValue01`–`05` (client ID and secret, username, password, security token), `Option01` as the sObject, and `Option02` as the external ID field. `Option03` (`DeleteBehavior`) decides what a `Delete` operation does: anything but `Delete` (the default, blank, means `Skip`) skips it, and `Delete` calls `SalesforceRestAPI.DeleteRecords`, which deletes by external ID through the Composite API in batches of 25. A record that's already gone (`NOT_FOUND`, `ENTITY_IS_DELETED`) counts as deleted.
5. Logging goes through `Utilities.WriteLogMessage`, tied to `BGTaskNum` when the method runs as a background task.

A rule's `CredentialValue01`–`10` come from its linked credential record. What each credential value and option means for a target type is defined in two form scripts, which also label and show or hide the form fields:
- the Credentials form's `UpdateValueLabels` (credential keys)
- the Rules form's `UpdateVariableUI` `config` dictionary (`CredentialKeys` and `OptionKeys`)

Keep both in sync, and make the extension class read the same positions.

To add a new target type:
- add a `case` in that `switch`
- add a helper, plus models under the target's own folder, in both projects
- add entries to both form-script lists

## Building

You need Windows with Visual Studio/MSBuild and the .NET Framework 4.8 targeting pack, and you have to restore NuGet packages first. The .NET SDK and MSBuild aren't available in this Linux container, so you can't compile or validate changes here. Build each solution on its own, for example:

```
nuget restore "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln"
msbuild "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln" /p:Configuration=Release
```

Upload the built DLL to Syteline (IDO Extension Class Assemblies). Then re-export the IDOs to refresh `dist/`, which picks up the new assembly image and any method signature changes. `bin/` and `obj/` are gitignored, so don't commit build output.

An IDO method's name in the IDO must exactly match the C# method name, `ue_` prefix included. That applies to strings that reference a method, like the `METHOD("...")` in trigger `EventActions`.
