# Replication Log and Retries

Every replication writes its outcome to the replication log. A scheduled background task resends failed records and cleans up old rows.

IDO: `ue_AIR_IDOReplicationLogs` (table `ue_AIR_idorepl_log_mst`)

Form: `ue_AIR_IDOReplicationLogs`

## What a log row holds

One row per record per rule.

| Property | Meaning |
| --- | --- |
| RuleNum, IDOName, TargetType | The rule that sent the record |
| CredentialName | The name of the credential the rule used, copied into the row when it is written. It is a snapshot: it stays as it was even if the rule or the credential is later renamed, changed or deleted. A retry updates it to the credential that attempt used. `(none)` means the rule had no credential, or the failure happened before the rule was loaded. |
| Source | What started the run. See [Source and operation](#source-and-operation). A retry doesn't change it. |
| Operation | What the run did with the record at the target: `Insert`, `Update`, `Upsert` or `Delete` |
| RecordID | The value of the rule's ID field (Option 2), up to 255 characters. Blank when the rule has no ID field. |
| Payload | The output record exactly as sent, as JSON, up to 4,000 characters |
| Status | `Succeeded`, `Failed` or `Skipped` |
| RecordRowPointer | The `RowPointer` of the record in Syteline. A [manual resend](Resending-Records) uses it to reload the record. Blank on rows logged before it was stored. |
| ErrorMessage | The error, and any transformation warnings |
| RetriesRemaining | How many more times the retry task will try. Starts at 3 for a failed row with a stored payload. Otherwise 0. |
| AttemptCount, LastAttemptDate | How many times the record was sent, and when it was last tried. The time is the site's, like CreateDate, which is when the row was first written. Rows written before this was corrected show the last attempt in the server's time, which is UTC in a hosted environment. |

## Source and operation

| Run | Source | Operation |
| --- | --- | --- |
| The insert trigger | `Event: After-Insert` | `Insert`, or `Upsert` for a Salesforce rule |
| The update trigger | `Event: After-Update` | `Update`, or `Upsert` for a Salesforce rule |
| The delete trigger, which runs before the row is removed | `Event: Before-Delete` | `Delete` |
| A [manual resend](Resending-Records) | `BG Task: Resend` | `Upsert` |
| A [bulk load](Bulk-Loads) | `BG Task: Bulk` | `Upsert` |
| A run from the Rules form's Test tab | `BG Task: Test` | `Upsert` |

`Upsert` means insert or update, whichever applies. A bulk load or a test doesn't know if the target already has the record.

**A Salesforce rule logs `Upsert` for an insert and for an update.** Salesforce matches on the external ID and creates or updates the record either way, so `Upsert` is what happens there. The Source still says which event it was: `Event: After-Insert` or `Event: After-Update`. A delete is logged as `Delete`. An Azure Event Hubs rule logs `Insert` and `Update` as they are, because the event it sends carries that operation. Salesforce rows logged before this was introduced keep the `Insert` or `Update` they had, so a Salesforce rule's log can show both. To find a Salesforce rule's inserts or updates, filter on Source.

Two less common cases:
- A bulk load that passes `Insert`, `Update` or `Delete` as its operation keeps the Source `BG Task: Bulk` and logs the operation it passed. A Salesforce rule still logs `Insert` and `Update` as `Upsert`, and with the Source `BG Task: Bulk` the row then doesn't say which of the two was passed.
- `Event: Unknown` with `Upsert` means a trigger called replication without one of the three operations. Open the IDO Replication Configs form and click **Fix Triggers** for that IDO.

## What gets logged

| Run | Logged |
| --- | --- |
| Trigger (a user's save) | Every record |
| Background task that loaded 100 records or fewer | Every record |
| Background task that loaded more than 100 records | Failed records, records with a transformation warning, and records that have an older `Failed` row |

Special cases:
- If a rule fails before its records are built, it gets one `Failed` row with no payload.
- If a rule's source records can't be loaded, most often because its field map names a property the IDO doesn't have, that rule gets one such row. The IDO's other rules still replicate. The record's change isn't replicated for the failed rule and can't be retried from the log, so after fixing the field map, run a [bulk load](Bulk-Loads) for the records that changed in the meantime.
- A payload over 4,000 characters isn't stored. The row says so, and it can't be retried.
- If a trigger can't find the record it was for, each rule gets one `Skipped` row with no record ID and no payload, whose message shows the filter the trigger was given.

## Statuses

| Status | Meaning |
| --- | --- |
| Succeeded | The target accepted the record |
| Failed | The target rejected the record, or it couldn't be sent |
| Skipped | Nothing was sent: a Salesforce delete with Delete Behavior `Skip`, a failed row that a newer replication replaced, or a trigger whose filter matched no record. The last kind has no record ID, and its message shows the filter the trigger passed. |

## The retry task

Background task: `ue_AIR_RetryFailedReplications`, which calls `ue_RetryFailedReplications(BGTaskNum, purgeAfterDays)`. See [Installation](Installation) for the task definition. Schedule it to run every minute, so a record that failed because of timing, such as a Salesforce child record sent before its parent, goes through a minute later.

Each run:

1. **Skips rows that are out of date.** If the same rule replicated the same record again after a row failed, the older row is marked `Skipped`. This keeps old data from overwriting newer data, and keeps a record that was deleted since from being re-created.
2. **Resends failed rows.** It takes up to 500 `Failed` rows that have retries left and a stored payload, oldest first, and sends them from their stored payloads with the rule's current settings. Because it sends the stored payload, a record that Syteline has since deleted can still be retried.
3. **Updates each row.** A row that goes through becomes `Succeeded`. A row that fails again loses one retry.
4. **Purges old rows.** It deletes `Succeeded` and `Skipped` rows older than `purgeAfterDays` (default 90). `0` turns the purge off. `Failed` rows are never deleted.

It only retries rows for active rules. Rows for an inactive or deleted rule are left as they are.

## When retries run out

A failed row gets 3 retries, one per run of the retry task. With the task running every minute, a target that is down for more than about 3 minutes leaves rows with no retries left. They stay in the log as `Failed`. The automatic retries are for timing problems, not for outages.

To send them again, use the [IDO Replication Resend](Resending-Records) form. It reloads the failed records and sends their current values.

You can also set RetriesRemaining back above 0 on a row, and the next run of the retry task resends its stored payload.

## Limits to know about

- The out-of-date check needs a record ID. A failed row with no record ID is still retried, up to 3 times, but the retry task can't tell whether its record was replicated again since, so older data can be resent after newer data. Set Option 2 (the ID field) on every rule to an output field that identifies the record.
- The record ID has to be unique per record. If two records share an ID field value, a newer replication of one makes a failed row for the other look out of date.
- The retry task sends the payload that was stored when the record first failed, not the record's current values.
