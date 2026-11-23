# Trigger Diagnostics

Tools for finding out how Syteline fires IDO events for each kind of save: a form, the REST API, and an extension class calling `UpdateCollection`. They are for a test system. Nothing here replicates data.

The results decide how replication is redesigned to stay reliable under bulk saves. The predictions below come from reading Infor's runtime (`IDOCore.dll` in `Lib/`). The [results](#results) of the first run, in a cloud sandbox, follow the tests.

## What's included

| Piece | What it does |
| --- | --- |
| `LogOnly` target type | A rule that sends nothing. Each record's outcome and payload go to the [replication log](Replication-Log-and-Retries) as usual. Options: **DelayMilliseconds** (wait this long per send, to stand in for a slow target, at most 60,000), **RecordIDFieldName** (the output field shown as the log's Record ID) and **SimulatedResult** (`Succeeded`, the default, or `Failed`). It needs no credential. |
| Diagnostic triggers | `ue_UpdateAESTriggers` with the action `SetupDiagnosticTriggers` creates event handlers (description `ue_AIR_DiagnosticTrigger`) for all eight update events on one IDO: `IdoOnUpdateCollection`, `IdoPostUpdateCollection`, and `IdoOn`/`IdoPost` for each of `ItemInsert`, `ItemUpdate` and `ItemDelete`. All are synchronous, plus one asynchronous `IdoPostUpdateCollection`. They are **active as soon as they're created**, and set to ignore a failure of their own, so an error inside one doesn't fail the save (but see [Results](#results): one failed a save when its extension class couldn't be loaded). `RemoveDiagnosticTriggers` deletes them. Only a super user can set them up, and not on this project's own IDOs or on `EventHandlers` and `EventActions`. They don't change the replication triggers or the Configs form's trigger status, though they do appear in its list of the IDO's event handlers. The setup also creates the doorbell handler (next row). |
| Doorbell | One more synchronous `IdoOnUpdateCollection` handler, with **Ignore Failure off**, that calls `ue_DiagRingDoorbell` once per save. It writes a `Doorbell=<token>` row, submits the `ue_AIR_DiagDoorbellTask` background task through `BGTaskDefinitions.BGTaskSubmit2`, and writes a `DoorbellSubmit=<token>` row with what the submission returned. The task runs `ue_DiagDoorbellTask`, which sends nothing: it looks for the doorbell row at once and again for up to 60 seconds, then writes a `DoorbellTask=<token>` row. It waits like that only when `ActiveBGTasks` lists it as a running `ue_AIR_DiagDoorbellTask`, or for a super user; otherwise it looks once and says so, so a direct call can't hold a server thread. See [The doorbell test](#the-doorbell-test). Because Ignore Failure is off, every save to the IDO fails while the Rules assembly on the server doesn't have `ue_DiagRingDoorbell`, or can't be loaded right after an upload. Remove the diagnostics before uploading an assembly. The doorbell also adds two log rows, a background task and some time to every save, so tests 1 to 15 run again with it give different timings. |
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

## The doorbell test

Tests whether a save can start a background task that runs within seconds after the save commits, which a redesign of replication would rely on: a save rings a doorbell once, and a background task then finds and sends what changed. It answers:

- **D1.** Can an event handler submit a background task, and how long after the save does it start?
- **D2.** Can the task start before the save commits?
- **D3.** Is the submission part of the save's transaction? If it is, a save that fails submits nothing.

Before you start, create a Background Task Definition named `ue_AIR_DiagDoorbellTask` that calls the `ue_AIR_IDOReplicationRules` IDO method `ue_DiagDoorbellTask`. It takes no parameters of its own: the doorbell passes `BG~TASKID~`, the IDO name, the token and the time. Then run `-Action SetupDiagnostics` again, which removes the diagnostic triggers and creates them again with the doorbell.

| # | Test | How | What to look for |
| --- | --- | --- | --- |
| D1 | One form save | Insert one `ZQ` record in the IDO's form | A `Doorbell`, a `DoorbellSubmit` and a `DoorbellTask` row with the same token. The `DoorbellSubmit` row must show a task number and no error, or the other tests mean nothing. The task row's message says how long after the submission it started (by two servers' clocks, so it can be off, even negative) and whether its first look saw the doorbell row quickly. For the time from the save's end to the task's start, compare the Create Date of the save's `Diag: IdoPostUpdateCollection` row with the task's Start Date in Background Task History; both are in the site's time |
| D2 | A long save | `-Action Insert -Count 100` (about 45 seconds) | The doorbell rings at the start of the save. The task started before the commit if its Start Date is before the Create Date of the save's `Diag: IdoPostUpdateCollection` row, if its row says the doorbell row appeared only later, or if its first look took many seconds (it waited for the save's lock) |
| D3 | A save that fails | `-Action Insert -Count 3 -FailLast` | Counts only after D1 worked. If the submission is part of the save, the failed save leaves no doorbell rows and no task in Background Task History (Initiator `ue_AIR_DiagDoorbell`). A task that runs anyway means the submission isn't part of the save: its row will say the doorbell row never became visible |

Clean up the test records with `-Action Cleanup` between tests. If the submission raises a database error that ends the save's transaction, catching it doesn't save the transaction, and the save fails with the error; that is a result too. The tasks' history is under Background Task History, task name `ue_AIR_DiagDoorbellTask`.

### Doorbell results

Run on November 23, 2026, in the same cloud sandbox, on `SLEndtypes`, with the diagnostic and replication triggers active. Each save rang the doorbell once. Save and task times are site times, from the save's `Diag:` rows and Background Task History. The task's code start is by the task server's clock.

| Save | Records | Save ended | Task Manager started the task | The task's code began | Its first look |
| --- | --- | --- | --- | --- | --- |
| Form insert (D1) | 1 | 15:25:14.70 | 15:25:18.57 (3.9 s later) | 5.7 s after the save | Saw the doorbell row (46 ms) |
| Form insert (D1, again) | 1 | 15:26:52.63 | 15:26:55.23 (2.6 s) | 3.9 s | Saw it (103 ms) |
| REST delete by key (`Cleanup`) | 2 | 15:27:46.17 | 15:27:46.58 (0.4 s) | 0.9 s | Saw it (9 ms) |
| REST insert (D2) | 100 | 15:28:33.46, 42 s after the task was submitted | 15:28:34.63 (1.2 s) | 2.2 s | Saw it (43 ms) |
| REST delete by key (`Cleanup`) | 100 | 15:30:29.68, 45 s after the task was submitted | 15:30:32.97 (3.3 s) | under 4 s | Saw it |
| REST insert that fails (D3) | 3 | Rolled back | No task: none in Background Task History or `ActiveBGTasks` | | |

What the doorbell results establish:

- **A synchronous event handler can submit a background task** through `BGTaskDefinitions.BGTaskSubmit2`. The task got its own number in place of `BG~TASKID~`. It was listed in `ActiveBGTasks` while it ran. Its Initiator and requesting user (the user who saved) showed in Background Task History.
- **The submission is part of the save's transaction** when the handler has Ignore Failure off.
  - The task never started before the save committed, even when it was submitted 42 and 45 seconds before the save ended. Task Manager only saw it after the commit, and the task's first look found the doorbell row at once.
  - The save that failed (D3) submitted nothing. Its doorbell rows rolled back with it, while the `IdoOnUpdateCollection` diagnostic row, from a handler with Ignore Failure on, stayed. That is the same split test 10b showed on `IdoPostItemInsert`, now seen on `IdoOnUpdateCollection` too.
- **The task started 0.4 to 3.9 seconds after the commit**, and its code ran 0.9 to 5.7 seconds after, because a task server takes about 1 to 2 seconds to start a task.
- **The doorbell cost each save 0.12 to 0.66 seconds**, whatever the number of records: the method call, two log rows, and the submission (49 to 322 ms). The two form saves cost the most. Every save ran on a newly started app domain. A real doorbell would write one row, not two.

## Results

First run: November 23, 2026, in an Infor CloudSuite Industrial sandbox (multi-tenant cloud), on `SLEndtypes`, with a `LogOnly` rule and the replication triggers active. Every REST call went through the ION API. Tests 1 to 10 were run, in a different order, plus the two variants below. Tests 12 and 15 were run later the same day, after the fix that writes a blank Record Row Pointer as blank. Test 15 used 100 and 500 records instead of 2,000, with the diagnostic triggers still active. Its records were REST inserts and deletes by key, which the replication trigger doesn't load or send (it only writes a `Skipped` row), so its timings show how the time grows with the number of records, not what replicating a record costs. Test 11 (a failing REST update), test 13 (a failing extension-class save) and test 14's run with DelayMilliseconds haven't been run.

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
| 12 | Extension class (`ue_DiagUpdateCollection`), tests 4 to 9's actions | The same as REST in every case. Insert of 3: keys present, RowPointer blank before and after the save, although the method asked for the records back (`RefreshAfterUpdate`). Not replicated. Update and delete by ItemId: nothing present. Update and delete by key: keys only. Update with key and RowPointer: both present, and all 3 records replicated. Nothing else replicated. |
| 15a | REST insert of 100 | One `UpdateCollection`, 44.1 seconds of a 44.6-second call: about 0.37 seconds per record at first and 0.44 on average, including three records that paused for 1 to 2 seconds. Each item handler wrote one row per record, and the replication trigger one `Skipped` row per record. |
| 15b | REST delete of the 100 by key (`Cleanup`) | One `UpdateCollection`, 41.3 seconds: about 0.41 seconds per record. Not replicated (keys only). |
| 15c | REST insert of 500 | One `UpdateCollection`, 200.8 seconds of a 205.8-second call: about 0.40 seconds per record, 0.38 for the first 250 and 0.42 for the last 100. The call wasn't cut off. All 500 records were saved. Each item handler wrote one row per record, and the replication trigger one `Skipped` row per record. |
| 15d | REST delete of the 500 by key (`Cleanup`) | One `UpdateCollection`, 209.8 seconds of a 225.4-second call (the call also loaded the records and waited for the confirmation): about 0.42 seconds per record. |

What the results establish:

- **In an item event, `P()` sees only the properties the caller put in that record.** A form includes `RowPointer` on updates and deletes, and on an insert the runtime fills it in by `IdoPostItemInsert`. A REST caller that names records by key sends only the keys, and one that names them by ItemId sends nothing the event can read. `P("_ItemId")` was blank in every test, even when the caller named the record by ItemId. A REST insert didn't get its RowPointer back on `IdoPostItemInsert` even with `refresh=true`, likely because the runtime refreshes only the properties the request included (not checked). So a REST insert isn't replicated, whatever the caller does. An extension class saving through `this.Context.Commands.UpdateCollection` (test 12) behaved exactly like a REST caller, insert included, even with `RefreshAfterUpdate` set. ION hasn't been tested.
- **The collection events never saw a record**: RowPointer and keys were blank in every `IdoOnUpdateCollection` and `IdoPostUpdateCollection`.
- **A handler with Ignore Failure on wrote outside the save's transaction**: its rows stayed when the save failed. **With Ignore Failure off, it wrote inside the save's transaction**, and its rows rolled back with the save. Seen on synchronous `IdoPostItemInsert` handlers in a REST insert; other events and Transactional handlers weren't tested. The replication triggers have Ignore Failure off, so their log rows roll back with a failed save. Anything they had already sent to a target would stay sent (not observed: the test's rule was `LogOnly`, and a REST insert isn't sent anyway).
- **The asynchronous `IdoPostUpdateCollection` handler never wrote a row**, and the event queue (`EventQueues`) had no entry for it. The cause wasn't found. Until it is, an asynchronous handler can't be counted on to run in this tenant.
- **The replication trigger cost about 0.2 to 0.4 seconds per record** in the form saves, with a `LogOnly` rule, so with no network call at all: loading the rules, loading the record, reading the site's time and writing the log row. If that holds for every record, a form save of 5,000 records that replicates them would spend roughly 17 to 33 minutes in replication before any target is reached. That is an extrapolation. Test 14, which times the replication triggers alone, wasn't run.
- **The time per record barely grows with the number of records in one save.** In REST saves of 100 and 500 records (test 15), each was one `UpdateCollection`, and the time stayed about 0.4 seconds per record, rising by about a tenth over 500 records. Those records weren't replicated (the trigger only wrote a `Skipped` row), and the diagnostic handlers added their own log writes, so 0.4 seconds isn't the cost of replicating a record. Nothing backed up and nothing was dropped: the caller waited. REST calls of up to 3.8 minutes went through the ION API without being cut off. Whether a longer call is cut off, and what happens to the save if it is, wasn't tested. That these large saves were each one transaction was assumed from test 10, not shown.
- **Saves ran on at least five application servers.** Static values in the extension class lasted as long as the server's app domain (one kept counting across saves 8 minutes apart), but a save can land on any server, so an in-memory cache can't be relied on between saves.
- **`System.Transactions.Transaction.Current` was empty inside every handler**, so the payload's `transactionId` can't show which database transaction an event ran in. Tests 10 and 10b answer that question instead.
- **Right after an assembly upload, a handler with Ignore Failure on failed a save.** Right after the Rules assembly was uploaded again, a REST insert failed with `Extension class for ue_AIR_IDOReplicationRules IDO not found`, raised by the diagnostic `IdoOnUpdateCollection` handler, which has Ignore Failure on. The same insert worked when run again a few minutes later; **Refresh Form Metadata** may have been run in between. Seen once, and the cause is unknown: Ignore Failure may not cover an extension class that can't be loaded, or may not apply to `IdoOnUpdateCollection` at all (it was only shown working on `IdoPostItemInsert`, in test 10b). The replication triggers have Ignore Failure off, so a save to an IDO with active replication triggers would fail the same way in that window.
- **`processId` is blocked** in the cloud (`SecurityException`). `processStartedUtc` (when the assembly's static values were set up in that app domain) and `appDomain` identify the app domain instead.

| # | Assumption | Result |
| --- | --- | --- |
| A1 | A REST save of several records is one transaction, item events in sequence, collection events once | Confirmed for a REST insert of 3 (tests 4, 10). REST inserts and deletes of 100 and 500 records were each one `UpdateCollection` (test 15). A failing REST update (test 11) wasn't run |
| A2 | A form's multi-row save is one `UpdateCollection` | Confirmed (tests 1b, 2, 3). A failing form save wasn't tested, so its transaction wasn't shown |
| A3 | `P("RowPointer")` in item events | Only when the caller sends it. Forms do (inserts after the save). REST callers and extension classes by key or ItemId don't, and their inserts never get it (tests 4 to 9, 12) |
| A4 | `P()` returns keys the caller sends | Confirmed |
| A5 | A handler's writes roll back with a failed save | Only with Ignore Failure off. With it on, they stay |
| A6 | `P()` is empty in the collection events | Confirmed |
| A7 | An asynchronous handler runs after the save | Not seen: it never ran, and nothing was found in the queue. Cause unknown |
| A8 | Cost per record | About 0.2 to 0.4 seconds per record for the replication trigger alone, in form saves. In REST saves of 100 and 500 records that weren't replicated, the time per record stayed nearly flat (test 15) |
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
