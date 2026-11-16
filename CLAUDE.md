# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

AES-driven, IDO-layer data replication for Infor CloudSuite Industrial (Syteline), built on the Mongoose framework. Administrators define replication rules (which IDO and which events), credentials for external targets, and field-level maps and transformations. When a record changes, the rule's IDO extension class loads the record, remaps it, and pushes it to the target. Working targets are Azure Event Hubs (including Fabric Eventstreams through a custom endpoint) and the Salesforce REST API. The project is pre-beta.

Nothing here runs locally and there is no test suite. Changes are deployed into Syteline through Mongoose tooling.

This is an MIT-licensed, open-source project meant for any company's Syteline instance. Don't hard-code company-specific values (URLs, names, sites). Make them settings on a credential or rule instead.

## Naming

Everything uses the `AIR` prefix: tables `ue_AIR_idorepl_*_mst`, IDOs `ue_AIR_*` on IDO server `ue_AIR`, forms `ue_AIR_*`, assemblies `ue_AIR_*_ECA`.

## Layout

- `dist/` holds the install package. The numbered folders are the install order:
  1. `1 - SQL (Paste Rows Append)/`: `SQL Tables.csv` and `SQL Columns.csv`, pasted as appended rows into Syteline's SQL Tables/Columns forms to create the five `_mst` tables. Each table also gets a view of the same name without `_mst`, and that view is what the IDO binds as its table.
  2. `2 - IDOs and Extension Classes (IDO Import Wizard)/IDOs and Extension Classes.xml`: an `<ObjectStudioExport>` with the IDOs. The `<CustomAssemblies>` section at the end embeds both compiled extension class DLLs as base64 (`<AssemblyImage>`), so this file changes whenever an assembly is re-uploaded. Never edit those images by hand.
     - `ue_AIR_IDOReplicationRules` binds `ue_AIR_IDOReplicationRules_ECA` and exposes `ue_ReplicateRecordToExternalSystems`, the custom-load `ue_LoadOutputPreview` and `ue_RetryFailedReplications`.
     - `ue_AIR_IDOReplicationConfigs` extends the standard `IdoCollections` IDO (no table of its own, one row per IDO collection). It binds `ue_AIR_IDOReplicationConfig_ECA` and exposes `ue_UpdateAESTriggers`.
     - `ue_AIR_IDOReplicationCredentials`, `ue_AIR_IDOReplicationMapFields`, `ue_AIR_IDOReplicationMapFieldSources`, `ue_AIR_IDOReplicationLogs` and `ue_AIR_EventHandlers` have no extension class.
     - `ue_AIR_IDOReplicationLogs` (table `ue_AIR_idorepl_log_mst`) is the replication log: one row per record per rule, with the operation, the payload as sent (JSON, up to 4,000 characters), the status (`Succeeded`, `Failed`, `Skipped`), the error, and `retries_remaining` (default 3) for the retry task. `WriteReplicationLog` in the Rules ECA writes it at the end of every run: trigger runs (`BGTaskNum` -1) log every record, and background task runs log every record when they loaded 100 or fewer and only failed records above that. A rule that failed before its records were remapped gets one row without a payload. Only a failed row with a stored payload gets retries; a payload over 4,000 characters isn't stored. `ue_RetryFailedReplications(BGTaskNum, purgeAfterDays = 90)`, run as a scheduled background task, first marks `Skipped` any `Failed` row whose record the same rule replicated again later (so old data never overwrites newer data or re-creates a deleted record), then resends up to 500 `Failed` rows with retries left from their stored payloads (oldest first, grouped by IDO, rule and operation, through the same `SendRule`), updates each row, and then deletes `Succeeded` and `Skipped` rows older than `purgeAfterDays` (0 turns the purge off). Rows for an inactive or deleted rule are left alone. `Failed` rows are never purged.
  3. `3 - Mongoose Objects (FormSync)/*.XML`: FormSync exports of the forms (`ue_AIR_IDOReplicationRules`, `ue_AIR_IDOReplicationCredentials`, `ue_AIR_IDOReplicationConfigs`, `ue_AIR_ImportJob`, `ue_AIR_JobFileInputWindow`) plus `Strings.XML`. Form scripts are embedded as `<ScriptLine>` elements.

  These files come from Mongoose export tools. Edit them minimally and keep the exporter's formatting: 3-space indentation, element order, and any UTF-8 BOM.

  When you hand-edit an IDO in the IDO export, also bump that IDO's `<RevisionNo>` by 1 and set its `<RevisionDate>` to the current local (Eastern) time in the same format, for example `2026-11-13T13:28:05`. Otherwise the IDO Import Wizard may not treat it as changed. After importing, use **Refresh Form Metadata** to clear the IDO cache. After changing an extension class method's signature, use **Resync Parameters** on the IDO method, which reads the parameters from the uploaded DLL, instead of editing them by hand.
- `wiki/` holds the source for the GitHub wiki (administrator and developer documentation). `.github/workflows/publish-wiki.yml` mirrors it to the wiki when a change to `wiki/` reaches `main`, so edit the pages here, not in the wiki's web editor. A file's name is its page name, pages link to each other by name without `.md`, and `_Sidebar.md` is the navigation.
- `src/IDO Extension Class Custom Assemblies/` holds two .NET Framework 4.8 C# class library solutions. They reference Infor DLLs from the sibling `Lib/` folder (`..\..\Lib\*.dll` HintPaths) and use NuGet `Newtonsoft.Json` and `System.ValueTuple` via `packages.config`. The language version is the .NET Framework default (C# 7.3), so avoid newer syntax such as collection expressions (`[]`), target-typed `new()`, `??=`, nullable annotations and file-scoped namespaces.
  - `ue_AIR_IDOReplicationRules_ECA` replicates data. Its IDO methods are `ue_ReplicateRecordToExternalSystems` (the entry point), `ue_LoadOutputPreview` and `ue_RetryFailedReplications` (the retry background task). `ue_ParseSavedMap` exists in the class but is not exposed by the IDO yet.
  - `ue_AIR_IDOReplicationConfig_ECA` manages triggers. `ue_UpdateAESTriggers(idoName, action, ref results)` takes `SetupTriggers`, `RemoveTriggers`, `FixTriggers`, `ActivateAllTriggers` or `DeactivateAllTriggers`, called from the Configs form. Setup creates `EventHandlers` (description `ue_AIR_ReplicationTrigger`) for insert, update and delete on the target IDO, with `EventActions` that call the Rules IDO's replication method with `PARMS(-1, <RowPointer filter>, <IDO name>, 1, "", <operation>)`, where the operation is `Insert`, `Update` or `Delete` for that event. It inserts and then deletes a temporary `CustomAIR` handler to work around a sequencing bug when inserting several handlers at once. Two derived properties compare each action's `Parameters` string exactly, so any change to that string in `SetupTriggers` (or to `OperationForEvent`) must also be made in both expressions, and existing triggers need **Fix Triggers**:
    - `ue_AIR_IDOReplicationConfigs.ue_EventHandlersAreSetup`, one comparison per event. It drives the Setup/Broken status on the Configs form.
    - `ue_AIR_EventHandlers.ue_IsSetup`, one comparison that picks the operation from the handler's `EventName`.
  - **`Helpers/` and `Models/` are duplicated in both projects and are identical except for the namespace.** When you change a helper or model in one project, apply the same change to the other. Keep the two assemblies separate. The plan is to move the helpers into a separate shared extension class assembly later, not to merge these two.

## How replication works (Rules ECA)

Replication runs inside the user's save, so it must never throw out of the trigger and block that save. Every error is caught, turned into a `ReplicationRuleResult` or log row, and the transaction commits. Keep that true in new code paths. The delete trigger (`IdoOnItemDelete`) runs before the row is removed, so the record can still be loaded.

1. A trigger created by the Config assembly calls `ue_ReplicateRecordToExternalSystems` with the IDO name, a query filter, a record cap, an optional rule number (blank means every active rule) and an optional operation (`Insert`, `Update`, `Delete`, or blank when unknown, as from background tasks). It returns early when there are no active rules.
2. `LoadReplicationRulesAndMaps` loads the active rules for that IDO along with their map fields. Each map field has a single source: `Name`, `SourceType` (`IDOProperty` or `Literal`), `SourceValue` and `SourceTransformation` on `ue_AIR_IDOReplicationMapFields`. Replication does not use the `Sources` subcollection or the derived `JSON` property.
3. The union of each rule's source properties is loaded once through `SytelineInternalAPI.LoadRecords`, which wraps `this.Context.Commands`. Data access goes through IDOs, not direct SQL.
4. For each rule, `RemapRecords` builds the output records from the field map and applies each field's transformation (`ReplicationField.Transformation`). The transformations are defined in `Models/ReplicationFieldTransformTypes.cs`. Each takes the typed value the IDO returns (not a string), nulls stay null, and a value that can't be converted is sent unchanged with a warning (`ReplicationFieldTransform.Convert(value, out warning)`). `RemapRecords` collects the warnings per record, and they reach the record's log row (`ReplicationRecordOutcome.Warning`, appended to `ErrorMessage`). A record with a warning is logged even in runs that only log failures. Date strings are parsed as `DateTimeOffset`, keeping the date as written. Their `Value` strings must match the `SourceTransformation` inline list on `ue_AIR_IDOReplicationMapFields`. The result is sent based on `TargetType`:
   - `AzureEventHub`: `AzureEventHubPusher.Push`, using `CredentialValue01` as the connection string, `CredentialValue02` as the event source and `CredentialValue03` as an optional `MaxBatchKBOverride`. `Option01` is an optional Fabric Event Schema URI, `Option02` is the field used as the record ID in failure messages, and `Option03` (`OperationFieldName`) is an optional body field that receives the operation. When the operation is known, every event also carries it as the `operation` user property, which Fabric keeps in event metadata only when the Event Hub source uses **Extended features** (read it with `GETMETADATAPROPERTYVALUE` in a SQL operator). The body field is off by default because Event Schema Eventstreams reject fields their schema doesn't define. When `Option01` is set, each event carries CloudEvents user properties (`type` = schema name, `source`, `dataschema` = the URI), which Fabric Event Schema Eventstreams require. Without it, plain events are sent. The batch size limit depends on the namespace tier (256 KB Basic, 1 MB Standard and up). Batches default to about 220 KB, which is safe for every tier, and `MaxBatchKBOverride` can raise that to at most 850. Only `201 Created` counts as accepted.
   - `SalesforceRESTAPI`: `SalesforceRestAPI.UpsertRecords`, using `CredentialValue01` and `02` as the client ID and secret of a Salesforce external client app and `CredentialValue03` as the `Domain`, `Option01` as the sObject, and `Option02` as the external ID field. `Option03` (`DeleteBehavior`) decides what a `Delete` operation does: anything but `Delete` (the default, blank, means `Skip`) skips it, and `Delete` calls `SalesforceRestAPI.DeleteRecords`, which deletes by external ID through the Composite API in batches of 25. A record that's already gone (`NOT_FOUND`, `ENTITY_IS_DELETED`) counts as deleted. Sign-in uses the OAuth client credentials flow only. The username-password flow was removed on purpose, because external client apps don't offer it, so don't add it back. `Domain` works as in SyteBridge and the Bridjer Salesforce library: it is the My Domain name (`mycompany`, or `mycompany--sandboxname.sandbox` for a sandbox), sign-in goes to `https://<Domain>.my.salesforce.com`, and it is required. `NormalizeSalesforceDomain` in the Rules ECA cuts a pasted URL down to the name and rejects anything that isn't a My Domain name. `SalesforceAPICredential` started as a port of Bridjer's, but now carries only `ClientId`, `ClientSecret` and `Domain`.
5. Logging goes through `Utilities.WriteLogMessage`, tied to `BGTaskNum` when the method runs as a background task, and each record's outcome goes to the replication log (`ue_AIR_IDOReplicationLogs`).

A rule's `CredentialValue01`–`10` come from its linked credential record. What each credential value and option means for a target type is defined in two form scripts, which also label and show or hide the form fields:
- the Credentials form's `UpdateValueLabels` (credential keys, each with a flag that says whether the value is sensitive). A sensitive value's input is only shown on a new record. On a saved record the form shows the matching `ValueNNPreview` field instead, bound to a derived property on `ue_AIR_IDOReplicationCredentials` that returns the first and last 6 characters. When you add a credential value, set its flag.
- the Rules form's `UpdateVariableUI` `config` dictionary (`CredentialKeys` and `OptionKeys`)

Keep both in sync, and make the extension class read the same positions. `README.md` documents the same credential values, rule options, transformations and log behavior for administrators, so update its tables, and the matching pages in `wiki/`, when these change.

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

## Pull requests

After pushing changes to a branch, always open a pull request against `main` without asking first. If the branch already has an open pull request, the push updates it.
