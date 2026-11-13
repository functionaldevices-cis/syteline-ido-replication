# AES-Driven IDO-Layer Data Replication

> [!CAUTION]
> **This project is in pre-beta testing and is shared for collaboration. Use at your own risk!**

An event-driven data replication system for Infor CloudSuite Industrial (Syteline), built on the Mongoose framework. Administrators set up replication rules in Syteline forms: which IDO to watch, which external system to send to, and how each field maps and transforms. When a record is inserted, updated or deleted, an Application Event System (AES) trigger replicates it. Failures are logged and retried automatically.

## Targets

| Target | Status | Details |
| --- | --- | --- |
| **Azure Event Hubs** | :white_check_mark: Working | Batched sends sized for every Event Hubs tier. Works with Microsoft Fabric Eventstreams through a custom endpoint, including standard, [inferred multi-schema](https://learn.microsoft.com/fabric/real-time-intelligence/event-streams/process-events-with-multiple-schemas) and [Event Schema](https://learn.microsoft.com/fabric/real-time-intelligence/schema-sets/use-event-schemas) Eventstreams. For Event Schema Eventstreams, set the rule's Event Schema URI and the credential's Event Source, and each event carries the CloudEvents properties Fabric needs. |
| **Salesforce REST API** | :white_check_mark: Working | Upserts by external ID. Deletes are optional, per rule. |
| Other Syteline instances (REST or SOAP) | :x: Not started | Conceptual |
| SQL databases (PostgreSQL, SQL Server, MySQL, ...) | :x: Not started | Conceptual |
| Other ERP systems with a REST API | :x: Not started | Conceptual |

## How it works

1. **Triggers.** From the IDO Replication Configs form, you set up triggers on any IDO. This creates AES event handlers for insert, update and delete, each calling the replication method with its operation (`Insert`, `Update` or `Delete`). The delete trigger runs before the row is removed, so the record can still be read.
2. **Rules.** The method loads the IDO's active replication rules, reads the record through the IDO layer, and builds one output record per rule from the rule's field map (IDO properties or literal values, each with an optional transformation).
3. **Sending.** Each rule sends to its target with its credential:
   - **Azure Event Hubs:** events carry the operation as an `operation` user property. Optionally, a body field holds it too.
   - **Salesforce:** records are upserted by external ID. A delete is skipped unless the rule's Delete Behavior is `Delete`.
4. **Logging and retries.** Every record's outcome goes to the replication log, with the payload exactly as sent. A scheduled background task resends failed records from their stored payloads, so deleted records can be retried too, and skips any that were replicated again since. It purges old successful rows.

Replication never blocks the user's save. Every error is caught and logged, and the triggering transaction always commits.

You can also run replication as a background task over many records, for example for an initial load.

## Components

All objects use the `AIR` prefix (`ue_AIR_...`) on IDO server `ue_AIR`.

**Tables** (each with a view of the same name without `_mst`, which the IDO uses)
- `ue_AIR_idorepl_rule_mst`: replication rules
- `ue_AIR_idorepl_cred_mst`: credentials for external targets
- `ue_AIR_idorepl_mapfield_mst`: field maps
- `ue_AIR_idorepl_mapfieldsource_mst`: map field sources
- `ue_AIR_idorepl_log_mst`: the replication log

**IDOs**
- `ue_AIR_IDOReplicationRules`: rules, with the replication, output preview and retry methods
- `ue_AIR_IDOReplicationCredentials`
- `ue_AIR_IDOReplicationMapFields` and `ue_AIR_IDOReplicationMapFieldSources`
- `ue_AIR_IDOReplicationLogs`: the replication log
- `ue_AIR_IDOReplicationConfigs`: extends `IdoCollections` to manage triggers per IDO
- `ue_AIR_EventHandlers`: extends `EventHandlers` to show trigger status

**Forms:** `ue_AIR_IDOReplicationRules`, `ue_AIR_IDOReplicationCredentials`, `ue_AIR_IDOReplicationConfigs`, `ue_AIR_ImportJob` and `ue_AIR_JobFileInputWindow`.

**Extension class assemblies** (.NET Framework 4.8)
- `ue_AIR_IDOReplicationRules_ECA`: replication, output preview, logging and retries
- `ue_AIR_IDOReplicationConfig_ECA`: trigger setup, removal and repair

Screenshots (from an earlier version):

![Rules form](https://github.com/user-attachments/assets/78fe11a2-e623-480b-bd1d-1b44760298ea)

![Credentials form](https://github.com/user-attachments/assets/a05d732e-aeb0-48ef-9042-c01a54e5947e)

## Installing

The install package is in `dist/`. Its folders are numbered in install order.

1. **SQL** (`dist/1 - SQL (Paste Rows Append)/`): paste the rows of `SQL Tables.csv` and `SQL Columns.csv` into Syteline's SQL Tables and SQL Columns forms as appended rows. Then create each table's view (the table name without `_mst`).
2. **IDOs and extension classes** (`dist/2 - IDOs and Extension Classes (IDO Import Wizard)/`): import the file with the IDO Import Wizard. It includes both extension class assemblies. Then run **Refresh Form Metadata** to clear the IDO cache.
3. **Forms and strings** (`dist/3 - Mongoose Objects (FormSync)/`): import the forms and `Strings.XML` with FormSync. Then unload global form objects (Form > Definition > Unload All Global Form Objects).
4. **Background tasks:** create Background Task Definitions for:
   - **replication**, called from the Rules form: `ue_AIR_ReplicateRecordToExternalSystems`, with parameters `BG~TASKID~, <filter>, <IDO name>, <record cap>`
   - **retries:** `ue_AIR_RetryFailedReplications`, calling the Rules IDO method `ue_RetryFailedReplications` with parameters `BG~TASKID~, 90`. The second parameter is how many days to keep successful log rows, and `0` keeps them forever. Schedule it, for example hourly.
5. **Configure:** add credentials and rules (below), then set up triggers on the IDO Replication Configs form.

## Configuring

### Credentials

| Target type | Value 1 | Value 2 | Value 3 | Values 4–5 |
| --- | --- | --- | --- | --- |
| `AzureEventHub` | Connection string (from a shared access policy on the event hub itself, including `EntityPath`) | Event Source: a URL identifying your organization, required for Event Schema Eventstreams | Max Batch KB Override (optional). Leave blank for the default of about 220 KB, safe for every tier; Standard tier and above can use up to 850. | |
| `SalesforceRESTAPI` | Client ID | Client secret | Username | Password, security token |

### Rules

| Target type | Option 1 | Option 2 | Option 3 |
| --- | --- | --- | --- |
| `AzureEventHub` | Event Schema URI (optional, for Fabric Event Schema Eventstreams) | ID field: the output field used to identify records in the log | Operation Field Name (optional): adds the operation to each event body under this name. Your event schema must then include it. |
| `SalesforceRESTAPI` | sObject name | External ID field | Delete Behavior: `Skip` (default) or `Delete` |

### Field maps

Each map field has an output name, a source type (`IDOProperty` or `Literal`), a source value (the property name or the literal), and an optional transformation:

| Transformation | Result |
| --- | --- |
| DateTime to Date | `yyyy-MM-dd` text |
| Decimal to Integer | A whole number, with halves rounded away from zero |
| Decimal to USD String | Text such as `$1,234.50` |
| Checkbox (Byte) to Boolean | `true` / `false` |
| Checkbox (Byte) to True/False String | `"True"` / `"False"` |
| Checkbox (Byte) to Yes/No String | `"Yes"` / `"No"` |

Empty values stay empty. A value a transformation can't convert is sent unchanged.

## Replication log and retries

`ue_AIR_IDOReplicationLogs` holds one row per record per rule. Each row stores:
- the operation and the record's ID
- the payload as sent (up to 4,000 characters)
- the status (`Succeeded`, `Failed` or `Skipped`) and the error
- the number of retries left (3 to start)

**What's logged:** trigger runs log every record. Background-task runs log every record when they load 100 or fewer, and only failures above that.

**What the retry task does:**
- It resends failed rows that still have retries left and a stored payload, using each rule's current settings.
- It marks a failed row `Skipped` when the same record was replicated again later, so older data never overwrites newer data.
- It ignores rows for inactive rules.
- It deletes `Succeeded` and `Skipped` rows older than the number of days you set. `Failed` rows are kept.

## Building

The extension class assemblies are in `src/IDO Extension Class Custom Assemblies/`, one Visual Studio solution each. They need Windows, Visual Studio or MSBuild, and the .NET Framework 4.8 targeting pack. They reference Infor's DLLs from the `Lib/` folder and NuGet's `Newtonsoft.Json`.

```
nuget restore "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln"
msbuild "src/IDO Extension Class Custom Assemblies/ue_AIR_IDOReplicationRules_ECA/ue_AIR_IDOReplicationRules_ECA.sln" /p:Configuration=Release
```

Upload the built DLL in Syteline's IDO Extension Class Assemblies form. If a method signature changed, use **Resync Parameters** on the IDO method.

## License

[MIT](LICENSE)
