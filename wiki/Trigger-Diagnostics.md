# Trigger Diagnostics

Tools for finding out how Syteline fires IDO events for each kind of save: a form, the REST API, and an extension class calling `UpdateCollection`. They are for a test system. Nothing here replicates data.

The results decide how replication is redesigned to stay reliable under bulk saves. The predictions below come from reading Infor's runtime (`IDOCore.dll` in `Lib/`). The [results](#results) of the first run, in a cloud sandbox, follow the tests.

## What's included

| Piece | What it does |
| --- | --- |
| `LogOnly` target type | A rule that sends nothing. Each record's outcome and payload go to the [replication log](Replication-Log-and-Retries) as usual. Options: **DelayMilliseconds** (wait this long per send, to stand in for a slow target, at most 60,000), **RecordIDFieldName** (the output field shown as the log's Record ID) and **SimulatedResult** (`Succeeded`, the default, or `Failed`). It needs no credential. |
| Diagnostic triggers | `ue_UpdateAESTriggers` with the action `SetupDiagnosticTriggers` creates event handlers (description `ue_AIR_DiagnosticTrigger`) for all eight update events on one IDO: `IdoOnUpdateCollection`, `IdoPostUpdateCollection`, and `IdoOn`/`IdoPost` for each of `ItemInsert`, `ItemUpdate` and `ItemDelete`. All are synchronous, plus one asynchronous `IdoPostUpdateCollection`. They are **active as soon as they're created**, and set to ignore a failure of their own, so they can't fail a save. `RemoveDiagnosticTriggers` deletes them. Only a super user can set them up, and not on this project's own IDOs or on `EventHandlers` and `EventActions`. They don't change the replication triggers or the Configs form's trigger status, though they do appear in its list of the IDO's event handlers. |
| `ue_LogTriggerEvent` | What each diagnostic trigger calls. It writes one log row per event (see [Reading the results](#reading-the-results)). It never fails the save. |
| `ue_DiagUpdateCollection` | Saves test records through `this.Context.Commands.UpdateCollection`, the way an extension class does, in one request. It can delete records, so only a super user can run it, the key prefix must be at least 2 characters with no SQL wildcard, and it refuses this project's own IDOs. |
| `tools/Test-AIRTriggers.ps1` | A PowerShell script that drives the REST and extension-class tests. Run `Get-Help .\Test-AIRTriggers.ps1 -Full` for its parameters. |

## Before you start

1. Install this version (see [Upgrading](Installation#upgrading)). The IDO export carries the new assemblies and both new methods.
2. Pick a test IDO. The defaults use `SLEndtypes` (End User Types), whose key `EndUserType` holds 6 characters. The test records use keys that start with `ZQ`, which no real record should use. Check that first.
3. Point that IDO's replication at a `LogOnly` rule, so no test reaches a real target. Create a `LogOnly` rule for the IDO, map at least the key field, set **RecordIDFieldName** to it, and make the rule active. Make any other rule for the IDO inactive while you test. A Salesforce rule with Delete Behavior `Delete` would otherwise delete the test records in Salesforce.
4. Decide how the script signs in. In Infor's cloud, use the ION API: pass `-IonApiFile` with a `.ionapi` file (the credentials ION API Gateway gives a backend service account) and the configuration name. The script gets a token from the file and takes the address from it. Keep the file outside the repository. On a server you reach directly, pass the server (`https://<server>`), the configuration name and `-Credential` instead, and the script gets a token from `ido/token/{config}`.
5. Set up the diagnostic triggers. Run the script in a normal PowerShell window; it needs no admin rights on your computer. If Windows blocks the downloaded script, run `Unblock-File .\Test-AIRTriggers.ps1` once, or `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`, which lasts only until that window closes. Neither needs admin rights.

```powershell
$conn = @{ IonApiFile = 'C:\path\to\credentials.ionapi'; MongooseConfig = '<config>' }
.\Test-AIRTriggers.ps1 @conn -Action SetupDiagnostics
```

Setting up and removing the diagnostic triggers, and `-Via ExtensionClass`, need a Syteline super user (the **Super User** flag on the Users form). The check is on the Syteline user the calls run as. Through the ION API that is the user behind the `.ionapi` file's service account, not the person running the script, so that user needs the flag. With `-Credential` it is your own user. The REST tests (insert, update, delete, cleanup) don't need a super user. Every record and log row the tests write shows the same user as its creator.

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
| 12 | Extension class | `-Via ExtensionClass` with tests 4 to 9's actions and `-Identify` values (`Cleanup` always goes through REST) | A3 | Same events as REST. What P() sees depends only on what the method sent |
| 13 | Extension class that fails | `-Via ExtensionClass -Action Insert -Count 3 -FailLast` | A5 | As test 10 |
| 14 | Cost per record | Make the replication triggers active too, with the `LogOnly` rule. `-Action Insert -Count 200`, then `-Action Cleanup`. Repeat with the rule's DelayMilliseconds set to 200 | A8 | `msSincePreviousEvent` between item events shows the cost of one record. With the delay, the save takes about 200 ms longer per record |
| 15 | Bulk | Only if test 14 looks reasonable: `-Count 2000` | A1, A8 | One transaction for the whole save. The time grows with the count |

Clean up with `-Action Cleanup` (every `ZQ` record, by key) and remove the triggers with `-Action RemoveDiagnostics`. Put the IDO's real rules back the way they were.

## Results

First run: November 23, 2026, in an Infor CloudSuite Industrial sandbox (multi-tenant cloud), on `SLEndtypes`, with a `LogOnly` rule and the replication triggers active. Tests 1 to 10 were run, in a different order, plus the two variants below. Test 11 (a failing REST update), the extension-class tests (12, 13) and the cost and bulk tests (14, 15) haven't been run yet.

| # | Test | What happened |
| --- | --- | --- |
| 1 | Form, insert 1 | As predicted for RowPointer: blank on `IdoOnItemInsert`, present on `IdoPostItemInsert`, so the replication trigger replicated it. This was the first save after the assemblies were uploaded, and it took about 33 seconds, 28 of them between `IdoOnUpdateCollection` and `IdoOnItemInsert`. Later saves took 1 to 2 seconds. |
| 1b | Form, insert 2 in one save | One `UpdateCollection` for the save, each record finished (events, save, replication) before the next began. Replicated. |
| 2 | Form, update 3 in one save | RowPointer and key present before and after the save. Replicated. |
| 3 | Form, delete in one save | Run with 2 records, not 3. RowPointer and key present before and after the delete. Replicated. |
| 4 | REST insert of 3 (`refresh=true`) | Same order as a form save: the collection event, each record's events in turn, the collection event. Keys present, RowPointer blank before **and after** the save. Not replicated: the replication trigger logged `Skipped` ("the trigger wasn't given the saved record's RowPointer"). |
| 5 | REST update by ItemId | Nothing present: RowPointer, key and `_ItemId` all blank. Not replicated. |
| 6 | REST update by key | Keys present, RowPointer blank. Not replicated. |
| 7 | REST update with key and RowPointer | Both present. Replicated. |
| 8 | REST delete by ItemId | Nothing present. Not replicated: with a real target, the delete would not have been sent. |
| 9 | REST delete by key | Keys present, RowPointer blank. Not replicated. |
| 10 | REST insert that fails on its last record | The whole save rolled back. The replication trigger's log rows rolled back with it. The diagnostic rows didn't. |
| 10b | Test 10 again, with **Ignore Failure** cleared on the diagnostic `IdoPostItemInsert` handler | That handler's rows rolled back too, while the other diagnostic handlers' rows stayed. |

What the results establish:

- **In an item event, `P()` sees only the properties the caller put in that record.** A form includes `RowPointer` on updates and deletes, and on an insert the runtime fills it in by `IdoPostItemInsert`. A REST caller that names records by key sends only the keys, and one that names them by ItemId sends nothing the event can read. `P("_ItemId")` was blank in every test, even when the caller named the record by ItemId. A REST insert didn't get its RowPointer back on `IdoPostItemInsert` even with `refresh=true`, likely because the runtime refreshes only the properties the request included (not checked). So a REST insert isn't replicated, whatever the caller does. Callers other than the test script's REST calls (ION, extension classes) haven't been tested.
- **The collection events never saw a record**: RowPointer and keys were blank in every `IdoOnUpdateCollection` and `IdoPostUpdateCollection`.
- **A handler with Ignore Failure on wrote outside the save's transaction**: its rows stayed when the save failed. **With Ignore Failure off, it wrote inside the save's transaction**, and its rows rolled back with the save. Seen on synchronous `IdoPostItemInsert` handlers in a REST insert; other events and Transactional handlers weren't tested. The replication triggers have Ignore Failure off, so their log rows roll back with a failed save. Anything they had already sent to a target would stay sent (not observed: the test's rule was `LogOnly`, and a REST insert isn't sent anyway).
- **The asynchronous `IdoPostUpdateCollection` handler never wrote a row**, and the event queue (`EventQueues`) had no entry for it. The cause wasn't found. Until it is, an asynchronous handler can't be counted on to run in this tenant.
- **The replication trigger cost about 0.2 to 0.4 seconds per record** in the form saves, with a `LogOnly` rule, so with no network call at all: loading the rules, loading the record, reading the site's time and writing the log row. If that stays linear, a save of 5,000 records would spend roughly 15 to 35 minutes in replication before any target is reached. Test 15 will check.
- **Saves ran on at least five application servers.** Static values in the extension class lasted as long as the server's app domain (one kept counting across saves 8 minutes apart), but a save can land on any server, so an in-memory cache can't be relied on between saves.
- **`System.Transactions.Transaction.Current` was empty inside every handler**, so the payload's `transactionId` can't show which database transaction an event ran in. Tests 10 and 10b answer that question instead.
- **`processId` is blocked** in the cloud (`SecurityException`). `processStartedUtc` (when the assembly's static values were set up in that app domain) and `appDomain` identify the app domain instead.

| # | Assumption | Result |
| --- | --- | --- |
| A1 | A REST save of several records is one transaction, item events in sequence, collection events once | Confirmed for a REST insert of 3 (tests 4, 10). A failing REST update (test 11) wasn't run |
| A2 | A form's multi-row save is one `UpdateCollection` | Confirmed (tests 1b, 2, 3). A failing form save wasn't tested, so its transaction wasn't shown |
| A3 | `P("RowPointer")` in item events | Only when the caller sends it. Forms do (inserts after the save). REST callers by key or ItemId don't, and a REST insert never gets it |
| A4 | `P()` returns keys the caller sends | Confirmed |
| A5 | A handler's writes roll back with a failed save | Only with Ignore Failure off. With it on, they stay |
| A6 | `P()` is empty in the collection events | Confirmed |
| A7 | An asynchronous handler runs after the save | Not seen: it never ran, and nothing was found in the queue. Cause unknown |
| A8 | Cost per record | About 0.2 to 0.4 seconds per record for the replication trigger alone, in form saves |
| A10 | Static values last between calls | Per app domain only, and saves are spread across servers |

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
| `utc`, `sequence`, `msSincePreviousEvent` | The time, a counter that keeps counting as long as the server's app domain lasts, and the milliseconds since the previous diagnostic event in that app domain |
| `machine`, `processId`, `processStartedUtc`, `appDomain`, `threadId` | Where it ran. Two machine names mean more than one application server. `processStartedUtc` is when the assembly's static values were set up in that app domain. `processId` is blocked in the cloud |
| `sessionId`, `initiatorType`, `initiator` | The Mongoose session and what started the request |
| `transactionId`, `transactionIsolation` | The ambient `System.Transactions` transaction and its isolation level. In the cloud run it was always `(none)`, so it says nothing about the database transaction; a failing save (tests 10 and 10b) shows that instead |

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
