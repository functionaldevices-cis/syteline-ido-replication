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
| Operation | `Insert`, `Update` or `Delete` for a trigger, `Test` for a run from the Rules form's Test tab, or `Bulk` for a bulk load (a background task run that passes no operation) |
| RecordID | The value of the rule's ID field (Option 2), up to 255 characters |
| Payload | The output record exactly as sent, as JSON, up to 4,000 characters |
| Status | `Succeeded`, `Failed` or `Skipped` |
| ErrorMessage | The error, and any transformation warnings |
| RetriesRemaining | How many more times the retry task will try. Starts at 3 for a failed row with a stored payload. |
| AttemptCount, LastAttemptDate | How many times the record was sent, and when it was last tried |

## What gets logged

| Run | Logged |
| --- | --- |
| Trigger (a user's save) | Every record |
| Background task that loaded 100 records or fewer | Every record |
| Background task that loaded more than 100 records | Failed records, and records with a transformation warning |

Special cases:
- If a rule fails before its records are built, it gets one `Failed` row with no payload.
- If the source records can't be loaded (for example, a field map names a property the IDO doesn't have), every rule for the IDO gets one such row, each with its own rule number, target type and credential name.
- A payload over 4,000 characters isn't stored. The row says so, and it can't be retried.

## Statuses

| Status | Meaning |
| --- | --- |
| Succeeded | The target accepted the record |
| Failed | The target rejected the record, or it couldn't be sent |
| Skipped | Nothing was sent on purpose: a Salesforce delete with Delete Behavior `Skip`, or a failed row that a newer replication replaced |

## The retry task

Background task: `ue_AIR_RetryFailedReplications`, which calls `ue_RetryFailedReplications(BGTaskNum, purgeAfterDays)`. See [Installation](Installation) for the task definition. Schedule it, for example hourly.

Each run:

1. **Skips rows that are out of date.** If the same rule replicated the same record again after a row failed, the older row is marked `Skipped`. This keeps old data from overwriting newer data, and keeps a record that was deleted since from being re-created.
2. **Resends failed rows.** It takes up to 500 `Failed` rows that have retries left and a stored payload, oldest first, and sends them from their stored payloads with the rule's current settings. Because it sends the stored payload, a record that Syteline has since deleted can still be retried.
3. **Updates each row.** A row that goes through becomes `Succeeded`. A row that fails again loses one retry.
4. **Purges old rows.** It deletes `Succeeded` and `Skipped` rows older than `purgeAfterDays` (default 90). `0` turns the purge off. `Failed` rows are never deleted.

It only retries rows for active rules. Rows for an inactive or deleted rule are left as they are.

## When retries run out

A failed row gets 3 retries. With an hourly schedule, a target that is down for more than about 3 hours leaves rows with no retries left. They stay in the log as `Failed`.

To send them again, either:
- set RetriesRemaining back above 0 on those rows, and the next run picks them up, or
- run a [bulk load](Bulk-Loads) for the affected records, which sends their current data.

## Limits to know about

- The supersede check needs a record ID. Set Option 2 (the ID field) on every rule, or old failed rows may be resent after newer data.
- A background run over 100 records doesn't log its successes, so a failed row from before that run isn't recognized as out of date.
- The retry task sends the payload that was stored when the record first failed, not the record's current values.
