# Trigger Diagnostics

Tools for finding out how Syteline fires IDO events for each kind of save: a form, the REST API, and an extension class calling `UpdateCollection`. They are for a test system. Nothing here replicates data.

The results decide how replication is redesigned to stay reliable under bulk saves. The predictions below come from reading Infor's runtime (`IDOCore.dll` in `Lib/`). None of them has been checked in Syteline yet.

## What's included

| Piece | What it does |
| --- | --- |
| `LogOnly` target type | A rule that sends nothing. Each record's outcome and payload go to the [replication log](Replication-Log-and-Retries) as usual. Options: **DelayMilliseconds** (wait this long per send, to stand in for a slow target, at most 60,000), **RecordIDFieldName** (the output field shown as the log's Record ID) and **SimulatedResult** (`Succeeded`, the default, or `Failed`). It needs no credential. |
| Diagnostic triggers | `ue_UpdateAESTriggers` with the action `SetupDiagnosticTriggers` creates event handlers (description `ue_AIR_DiagnosticTrigger`) for all eight update events on one IDO: `IdoOnUpdateCollection`, `IdoPostUpdateCollection`, and `IdoOn`/`IdoPost` for each of `ItemInsert`, `ItemUpdate` and `ItemDelete`. All are synchronous, plus one asynchronous `IdoPostUpdateCollection`. They are **active as soon as they're created**. `RemoveDiagnosticTriggers` deletes them. They don't affect the replication triggers or the Configs form's status. |
| `ue_LogTriggerEvent` | What each diagnostic trigger calls. It writes one log row per event (see [Reading the results](#reading-the-results)). It never fails the save. |
| `ue_DiagUpdateCollection` | Saves test records through `this.Context.Commands.UpdateCollection`, the way an extension class does, in one request. |
| `tools/Test-AIRTriggers.ps1` | A PowerShell script that drives the REST and extension-class tests. Run `Get-Help .\Test-AIRTriggers.ps1 -Full` for its parameters. |

## Before you start

1. Install this version (see [Upgrading](Installation#upgrading)), including **Resync Parameters** on the Rules IDO's two new methods.
2. Pick a test IDO. The defaults use `SLEndtypes` (End User Types), whose key `EndUserType` holds 6 characters. The test records use keys that start with `ZQ`, which no real record should use. Check that first.
3. Point that IDO's replication at a `LogOnly` rule, so no test reaches a real target. Create a `LogOnly` rule for the IDO, map at least the key field, set **RecordIDFieldName** to it, and make the rule active. Make any other rule for the IDO inactive while you test. A Salesforce rule with Delete Behavior `Delete` would otherwise delete the test records in Salesforce.
4. Decide how the script signs in. Directly: pass the server (`https://<server>`), the configuration name and `-Credential`, and the script gets a token from `ido/token/{config}`. Through the ION API: pass the gateway URL up to `/ido`, `-Authorization 'Bearer <token>'` and the configuration name.
5. Set up the diagnostic triggers:

```powershell
$conn = @{ BaseUrl = 'https://<server>'; Credential = (Get-Credential); MongooseConfig = '<config>' }
.\Test-AIRTriggers.ps1 @conn -Action SetupDiagnostics
```

The token, load and update calls follow Infor's documentation and the [syteline-api-examples](https://github.com/functionaldevices-cis/syteline-api-examples) repository. `ido/invoke`, which the setup, the removal and `-Via ExtensionClass` use, isn't confirmed by either. If it fails, compare it with your instance's Swagger document (`ido/dynamic/api-docs-collection`) and adjust `Invoke-AIRMethod` in the script.

## The tests

Run them in order. After each one, read the `Diag:` rows it wrote (next section), and write down what you saw against the prediction.

| # | Test | How | Answers | Prediction |
| --- | --- | --- | --- | --- |
| 1 | Form, insert 1 record | On the IDO's form, add one `ZQ` record and save | A3 | One `IdoOnUpdateCollection`, `IdoOnItemInsert`, `IdoPostItemInsert`, `IdoPostUpdateCollection`, all with the same transaction ID. RowPointer sent on the post-insert event |
| 2 | Form, update 3 records in one save | Change 3 `ZQ` rows in the grid, save once | A2, A3 | One pair of collection events around three pairs of item events, one transaction |
| 3 | Form, delete 3 records in one save | Delete 3 `ZQ` rows, save once | A2, A3 | As test 2. RowPointer sent (a single delete is known to work) |
| 4 | REST insert | `-Action Insert -Count 3` | A1, A3 | As test 2. RowPointer **blank** on `IdoPostItemInsert` unless the runtime refreshes it |
| 5 | REST update by ItemId | `-Action Update -Identify ItemId` | A3 | RowPointer and keys **blank** |
| 6 | REST update by key | `-Action Update -Identify Keys` | A3, A4 | RowPointer blank, keys sent |
| 7 | REST update with RowPointer | `-Action Update -Identify KeysAndRowPointer` | A3 | Both sent |
| 8 | REST delete by ItemId | `-Action Insert -Count 3`, then `-Action Delete -Identify ItemId` | A3 | RowPointer and keys blank on `IdoOnItemDelete` |
| 9 | REST delete by key | Insert again, then `-Action Delete -Identify Keys` | A3, A4 | Keys sent, RowPointer blank |
| 10 | REST insert that fails | `-Action Insert -Count 3 -FailLast` | A5, A7 | The save fails on the duplicate key, and **no** `Diag:` rows from it remain, because they were written in the save's transaction. Note whether the asynchronous `IdoPostUpdateCollection` row appears |
| 11 | REST update that fails | Insert 3, then `-Action Update -FailLast` | A5 | As test 10 |
| 12 | Extension class | `-Via ExtensionClass` with tests 4 to 9's actions and `-Identify` values | A3 | Same events as REST. What P() sees depends only on what the method sent |
| 13 | Extension class that fails | `-Via ExtensionClass -Action Insert -Count 3 -FailLast` | A5 | As test 10 |
| 14 | Cost per record | Make the replication triggers active too, with the `LogOnly` rule. `-Action Insert -Count 200`, then `-Action Cleanup`. Repeat with the rule's DelayMilliseconds set to 200 | A8 | `msSincePreviousEvent` between item events shows the cost of one record. With the delay, the save takes about 200 ms longer per record |
| 15 | Bulk | Only if test 14 looks reasonable: `-Count 2000` | A1, A8 | One transaction for the whole save. The time grows with the count |

Clean up with `-Action Cleanup` (every `ZQ` record, by key) and remove the triggers with `-Action RemoveDiagnostics`. Put the IDO's real rules back the way they were.

## Reading the results

Each diagnostic row in `ue_AIR_IDOReplicationLogs` has:

| Column | Holds |
| --- | --- |
| Source | `Diag: ` and the event, for example `Diag: IdoPostItemUpdate` |
| Operation | `Insert`, `Update` or `Delete` for an item event, `Upsert` for a collection event (it means nothing there) |
| Status | Always `Skipped`, so the retry task and the Resend form ignore the row. The retry task's purge removes it like any other `Skipped` row |
| Record ID | The key values the handler passed, as `Name=Value` |
| Record Row Pointer | The RowPointer the handler passed, when it was a GUID |
| Error Message | A summary: whether RowPointer, the keys and the ItemId were sent |
| Payload | JSON with the details below |

The payload holds:

| Field | Meaning |
| --- | --- |
| `event`, `mode` | The event, and `Sync` or `Async` |
| `rowPointer`, `keyValues`, `itemId` | The raw values of `P("RowPointer")`, the key properties and `P("_ItemId")` |
| `utc`, `sequence`, `msSincePreviousEvent` | The time, a counter that keeps counting as long as the server process runs, and the milliseconds since the previous diagnostic event in that process |
| `machine`, `processId`, `processStartedUtc`, `appDomain`, `threadId` | Where it ran. Two machine names mean more than one application server |
| `sessionId`, `initiatorType`, `initiator` | The Mongoose session and what started the request |
| `transactionId`, `transactionIsolation` | The database transaction the handler ran in, and its isolation level. Events with the same ID were in one transaction. `(none)` means no transaction |

To list a test's rows, load the log with `IDOName = '<IDO>' AND Source LIKE 'Diag:%'`, newest first by Create Date.

## Assumptions these tests answer

| # | Assumption |
| --- | --- |
| A1 | A REST update of N records is one transaction, with the item events in sequence and the collection events once per request |
| A2 | A form's multi-row save is one `UpdateCollection` |
| A3 | Whether `P("RowPointer")` has a value, for each caller and action |
| A4 | `P()` returns the key properties whenever the caller sends them |
| A5 | A synchronous, non-transactional handler's writes roll back with a failed save |
| A6 | `P()` returns nothing in the collection events (checked by every test: the collection rows' values should be blank) |
| A7 | When an asynchronous handler runs, and whether it is dropped when the save rolls back |
| A8 | The cost per record of a trigger call |
| A10 | Static values in an extension class last between calls (`sequence` keeps counting), and how many servers there are |
