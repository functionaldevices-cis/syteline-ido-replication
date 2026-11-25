# Installation

The install package is in the repository's `dist/` folder. Its folders are numbered in install order.

## Before you start

You need:
- An Infor CloudSuite Industrial (Syteline) environment where you can add SQL tables, import IDOs, import forms, and define background tasks.
- Outbound HTTPS from the Syteline application server to each target (your Event Hubs namespace, or Salesforce).

> [!NOTE]
> The two extension class assemblies are embedded in the IDO export. They match the source only as of the last time someone built them and re-exported the IDOs. If you build from a newer commit, upload your own DLLs after the import (see [Development](Development#deploying-a-change)).

## 1. SQL tables

Folder: `dist/1 - SQL (Paste Rows Append)/`

1. Open Syteline's SQL Tables form and paste the rows of `SQL Tables.csv` as appended rows.
2. Open the SQL Columns form and paste the rows of `SQL Columns.csv` as appended rows.
3. Create a view for each table. The view has the table's name without `_mst`. The IDOs bind to the views, not the tables.

| Table | View | Holds |
| --- | --- | --- |
| `ue_AIR_idorepl_rule_mst` | `ue_AIR_idorepl_rule` | Replication rules |
| `ue_AIR_idorepl_cred_mst` | `ue_AIR_idorepl_cred` | Credentials for external targets |
| `ue_AIR_idorepl_mapfield_mst` | `ue_AIR_idorepl_mapfield` | Field maps |
| `ue_AIR_idorepl_mapfieldsource_mst` | `ue_AIR_idorepl_mapfieldsource` | Map field sources (not used by replication yet) |
| `ue_AIR_idorepl_log_mst` | `ue_AIR_idorepl_log` | The replication log |

## 2. IDOs and extension classes

Folder: `dist/2 - IDOs and Extension Classes (IDO Import Wizard)/`

1. Import `IDOs and Extension Classes.xml` with the IDO Import Wizard. It contains the seven IDOs and both extension class assemblies.
2. Run **Refresh Form Metadata** to clear the IDO cache.

## 3. Forms and strings

Folder: `dist/3 - Mongoose Objects (FormSync)/`

1. Import the forms and `Strings.XML` with FormSync.
2. Unload global form objects (Form > Definition > Unload All Global Form Objects).
3. Optional: create a user-defined type named `IDOReplicationTargetType` with the values `AzureEventHub`, `SalesforceRESTAPI` and `LogOnly`. The Target Type drop-down on the Rules and Credentials forms takes its list from it, and the package doesn't create it. Without it the list is empty and you type the value, spelled exactly.

| Form | Use |
| --- | --- |
| `ue_AIR_IDOReplicationConfigs` | Set up, repair and remove triggers per IDO |
| `ue_AIR_IDOReplicationRules` | Rules, field maps and target options |
| `ue_AIR_IDOReplicationCredentials` | Credentials for external targets |
| `ue_AIR_IDOReplicationLogs` | The replication log |
| `ue_AIR_IDOReplicationResend` | Resend a rule's records by hand |
| `ue_AIR_ImportJob`, `ue_AIR_JobFileInputWindow` | Import Map window opened from the Rules form (not working yet) |

## 4. Background tasks

Create three Background Task Definitions.

**Replication** (for [bulk loads](Bulk-Loads))
- Task name: `ue_AIR_ReplicateRecordToExternalSystems`
- Calls the `ue_AIR_IDOReplicationRules` IDO method `ue_ReplicateRecordToExternalSystems`
- Parameters: `BG~TASKID~, <filter>, <IDO name>, <record cap>`

**Retries** (see [Replication Log and Retries](Replication-Log-and-Retries))
- Task name: `ue_AIR_RetryFailedReplications`
- Calls the `ue_AIR_IDOReplicationRules` IDO method `ue_RetryFailedReplications`
- Parameters: `BG~TASKID~, 90`. The second parameter is how many days to keep successful log rows. `0` keeps them forever.
- Schedule it to run every minute. Each failed record gets 3 retries, one per run.
- Make sure two runs can't overlap, or a record could be resent twice. Set the task definition to allow one run at a time.

**Resend** (see [Resending Records](Resending-Records))
- Task name: `ue_AIR_ResendReplications`
- Calls the `ue_AIR_IDOReplicationRules` IDO method `ue_ResendReplications`
- Parameters: `BG~TASKID~, <rule number>, <from>, <to>, <Failures or Everything>, <filter>, <limit>`. The Resend form fills these in.
- Not scheduled. The form submits it.

## 5. Configure

1. Add [credentials](Credentials).
2. Add [rules and field maps](Replication-Rules-and-Field-Maps).
3. [Set up triggers](Setting-Up-Triggers) and activate them.

## Upgrading

Do steps 2 to 4 together, at a quiet time. Between them the IDOs, the assemblies and the forms are out of step with each other. A save to an IDO with active replication triggers can fail for a few minutes after the assemblies are uploaded (step 3), and a replication that runs in that window can fail to write its log row, and a failure that isn't logged is never retried.

1. Add any new columns first. Compare `SQL Columns.csv` with your tables, append the rows you don't have on the SQL Columns form, and refresh or recreate the view of each table you changed. An IDO property bound to a column that doesn't exist makes every save to that IDO fail. For the replication log that means no log rows are written, and so nothing is retried.
2. Import the new IDO export, then run **Refresh Form Metadata**. The export carries the extension class assemblies too, and an import puts its own in place: seen once in testing, an import replaced a newer uploaded assembly with the older one in the export. If you build the assemblies yourself, upload them after the import (step 3), never before.
3. Upload the new extension class assemblies if you build them yourself. Use **Resync Parameters** on any IDO method whose parameters changed. For a few minutes after an upload, a save to an IDO with active replication triggers can fail with `Extension class for ue_AIR_IDOReplicationRules IDO not found`. If it does, run **Refresh Form Metadata**, wait a few minutes and save again. This was seen once in testing, and its cause wasn't confirmed.
4. Import the new forms and strings, then unload global form objects.
5. Create any new Background Task Definitions listed in [step 4](#4-background-tasks).
6. Carry over and remove columns that were renamed or dropped. A column of yours that is no longer in `SQL Columns.csv` isn't used any more. Do these in order, once the new assemblies are in place (step 3 above) and before anyone uses the Resend form:
   1. **Copy a renamed column's values into its new column.** The table below lists the renames and the statement for each. Run it against the application database, at a quiet time. It is safe to run more than once. Without the copy, every row written before the upgrade loses that value.
   2. **Check that no IDO property is still bound to the old column.** Open the IDO that used it on the IDOs form. If a property is still bound to it, delete that property first: a property bound to a missing column makes every save to that IDO fail.
   3. **Delete the old column**, then refresh or recreate the table's view again.

   | Table | Old column | New column | Copy statement |
   | --- | --- | --- | --- |
   | `ue_AIR_idorepl_log_mst` | `source_row_pointer` | `record_row_pointer` | `UPDATE ue_AIR_idorepl_log_mst SET record_row_pointer = source_row_pointer WHERE record_row_pointer IS NULL AND source_row_pointer IS NOT NULL` |

   If you can't run SQL against the database, the log rows written before the upgrade have no record row pointer. [Resend Errors](Resending-Records) then can't reload their records. A failed row with a stored payload is left to the retry task, which sends that stored payload. A failed row with no stored payload can't be resent, isn't listed on the Resend form, and stays `Failed`. To send those records' current data, use **Bulk Resync** after the upgrade.
7. Notes for specific versions:
   - **The version that makes `RecordRowPointer` writable:** the replication log's `RecordRowPointer` property was read-only through its property class, and Syteline dropped the value without an error, so every log row written before this version has no record row pointer. [Resend Errors](Resending-Records) can't reload those rows' records: it leaves rows with a stored payload to the retry task, and the rest need **Bulk Resync**. No new columns. After importing, check that the property is writable: the next replication should log a row with its Record Row Pointer filled in.
   - **The version that adds `LogOnly` and the trigger diagnostics:** the Rules IDO has two new methods, `ue_LogTriggerEvent` and `ue_DiagUpdateCollection`. Until the new assemblies are uploaded, a `LogOnly` rule fails with `Unknown target type`, and the new methods and diagnostic actions don't exist. If you build the assemblies yourself, use **Resync Parameters** on both new methods afterward. No new columns. See [Trigger Diagnostics](Trigger-Diagnostics).
   - **The version that adds the doorbell test:** the Rules IDO has two new test-only methods, `ue_DiagRingDoorbell` and `ue_DiagDoorbellTask`, and the diagnostic setup creates a doorbell handler. Upload both new assemblies before you set up the diagnostic triggers again: the doorbell handler has Ignore Failure off, so while the Rules assembly on the server lacks `ue_DiagRingDoorbell`, or right after an upload when Syteline can't load it yet (see step 3), every save to the tested IDO fails. Run `-Action RemoveDiagnostics` before any later assembly upload, and set the diagnostics up again afterward. If you build the assemblies yourself, use **Resync Parameters** on both new methods afterward. The test also needs a Background Task Definition, `ue_AIR_DiagDoorbellTask` (see [Trigger Diagnostics](Trigger-Diagnostics#the-doorbell-test)). No new columns. Replication doesn't use any of it.
   - **The version that writes a blank record row pointer as blank:** in the versions above, a log row with no record (a trigger that wasn't given the record, or a rule that failed before it got to its records) got a random record row pointer instead of a blank one. [Resend Errors](Resending-Records) would treat such a `Failed` row's record as deleted and mark the row `Skipped`. Upload the new Rules assembly. Afterward, check a row with no record, such as a `Skipped` row whose message says the trigger wasn't given the saved record's RowPointer: its Record Row Pointer should be blank. To clear the random values on `Failed` rows that have no record, run this against the application database (it is safe to run more than once): `UPDATE ue_AIR_idorepl_log_mst SET record_row_pointer = NULL WHERE status = 'Failed' AND payload IS NULL AND record_id IS NULL AND (error_message IS NULL OR error_message NOT LIKE '%payload is over 4,000 characters%')`. The last condition keeps the real record row pointer on a row whose payload was too long to store, which Resend Errors can still reload. `Skipped` rows from those versions keep their random values; nothing acts on `Skipped` rows. No new columns.
   - **The version that adds the strict diagnostics and `ue_DiagTimeLoad`:** test-only. The Config assembly has a new trigger action, `SetupStrictDiagnosticTriggers` (`-Strict` in the test script), and the Rules IDO a new method, `ue_DiagTimeLoad` (`-ReadMode` in the test script). The assemblies embedded in the IDO export don't have the new code yet: import the IDOs first, then upload both newly built assemblies (step 3), because an import after the upload would put the older embedded assemblies back. Until the Config assembly is uploaded, the strict setup is refused as an unknown action, and the script now stops when that happens. No new columns. Replication doesn't use any of it.
8. Open the IDO Replication Configs form. If an IDO's status changed to **Broken**, the trigger format changed. Click **Fix Triggers**. It keeps each trigger active or inactive as it was, so an IDO that was replicating before the upgrade should show **Active**. If it shows **Inactive** or **Partly Active** and it should replicate, click **Activate All**. See [Setting Up Triggers](Setting-Up-Triggers).
