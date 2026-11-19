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
3. Optional: create a user-defined type named `IDOReplicationTargetType` with the values `AzureEventHub` and `SalesforceRESTAPI`. The Target Type drop-down on the Rules and Credentials forms takes its list from it, and the package doesn't create it. Without it the list is empty and you type the value, spelled exactly.

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

1. Add any new columns first. Compare `SQL Columns.csv` with your tables, append the rows you don't have on the SQL Columns form, and refresh or recreate the view of each table you changed. An IDO property bound to a column that doesn't exist makes every save to that IDO fail. For the replication log that means no log rows are written, and so nothing is retried. A column of yours that is no longer in the file was renamed or dropped. Leave it in place until the new IDO export is imported and no IDO property is bound to it, then delete it. The log table's `source_row_pointer` is one: it is now `record_row_pointer`.
2. Import the new IDO export, then run **Refresh Form Metadata**.
3. Upload the new extension class assemblies if you build them yourself. Use **Resync Parameters** on any IDO method whose parameters changed.
4. Import the new forms and strings, then unload global form objects.
5. Create any new Background Task Definitions listed in [step 4](#4-background-tasks).
6. Open the IDO Replication Configs form. If an IDO's status changed to **Broken**, the trigger format changed. Click **Fix Triggers**. It keeps each trigger active or inactive as it was, so an IDO that was replicating before the upgrade should show **Active**. If it shows **Inactive** or **Partly Active** and it should replicate, click **Activate All**. See [Setting Up Triggers](Setting-Up-Triggers).
