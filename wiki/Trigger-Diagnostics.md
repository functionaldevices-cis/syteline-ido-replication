# Trigger Diagnostics

Tools for finding out how Syteline fires IDO events for each kind of save: a form, the REST API, and an extension class calling `UpdateCollection`. They are for a test system. Nothing here replicates data.

The results decide how replication is redesigned to stay reliable under bulk saves. The predictions below come from reading Infor's runtime (`IDOCore.dll` in `Lib/`). The [results](#results) of the first run, in a cloud sandbox, follow the tests.

## What's included

| Piece | What it does |
| --- | --- |
| `LogOnly` target type | A rule that sends nothing. Each record's outcome and payload go to the [replication log](Replication-Log-and-Retries) as usual. Options: **DelayMilliseconds** (wait this long per send, to stand in for a slow target, at most 60,000), **RecordIDFieldName** (the output field shown as the log's Record ID) and **SimulatedResult** (`Succeeded`, the default, or `Failed`). It needs no credential. |
| Diagnostic triggers | `ue_UpdateAESTriggers` with the action `SetupDiagnosticTriggers` creates event handlers (description `ue_AIR_DiagnosticTrigger`) for all eight update events on one IDO: `IdoOnUpdateCollection`, `IdoPostUpdateCollection`, and `IdoOn`/`IdoPost` for each of `ItemInsert`, `ItemUpdate` and `ItemDelete`. All are synchronous, plus one asynchronous `IdoPostUpdateCollection`. They are **active as soon as they're created**, and set to ignore a failure of their own, so an error inside one doesn't fail the save (but see [Results](#results): one failed a save when its extension class couldn't be loaded). `RemoveDiagnosticTriggers` deletes them. Only a super user can set them up, and not on this project's own IDOs or on `EventHandlers` and `EventActions`. They don't change the replication triggers or the Configs form's trigger status, though they do appear in its list of the IDO's event handlers. The setup also creates the doorbell handler (next row). |
| Doorbell | One more synchronous `IdoOnUpdateCollection` handler, with **Ignore Failure off**, that calls `ue_DiagRingDoorbell` once per save. It writes a `Doorbell=<token>` row, submits the `ue_AIR_DiagDoorbellTask` background task through `BGTaskDefinitions.BGTaskSubmit2`, and writes a `DoorbellSubmit=<token>` row with what the submission returned. It skips the submission when `ActiveBGTasks` already lists a doorbell task for the same IDO with the status `READY` and no Start Date, which B3 confirmed is how a queued task shows, and its row then names that task. Every `DoorbellSubmit` row lists the doorbell tasks it saw, with their statuses and start dates. Each task's row lists the doorbell rows it could see (the first 12 characters of each token). The task runs `ue_DiagDoorbellTask`, which sends nothing: it looks for the doorbell row at once and again for up to 60 seconds, then writes a `DoorbellTask=<token>` row. It waits like that only when `ActiveBGTasks` lists it as a running `ue_AIR_DiagDoorbellTask`, or for a super user; otherwise it looks once and says so, so a direct call can't hold a server thread. See [The doorbell test](#the-doorbell-test). Because Ignore Failure is off, every save to the IDO fails while the Rules assembly on the server doesn't have `ue_DiagRingDoorbell`, or can't be loaded right after an upload. Remove the diagnostics before uploading an assembly. The doorbell also adds two log rows, a background task and some time to every save, so tests 1 to 15 run again with it give different timings. |
| `ue_LogTriggerEvent` | What each diagnostic trigger calls. It writes one log row per event (see [Reading the results](#reading-the-results)). It never fails the save. |
| `ue_DiagUpdateCollection` | Saves test records through `this.Context.Commands.UpdateCollection`, the way an extension class does, in one request. It can delete records, so only a super user can run it, the key prefix must be at least 2 characters with no SQL wildcard, and it refuses this project's own IDOs. |
| `tools/Test-AIRTriggers.ps1` | A PowerShell script that drives the REST and extension-class tests, and times read-only polls (`-Action TimeLoad`). Run `Get-Help .\Test-AIRTriggers.ps1 -Full` for its parameters. |

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

The burst tests look at many saves close together. Fire 20 separate single-record saves from PowerShell, with keys `ZQA001` to `ZQT001`. Each job starts its own PowerShell and signs in on its own, so the saves arrive in waves over several seconds, not all at once:

```powershell
$script = (Resolve-Path .\Test-AIRTriggers.ps1).Path
$prefixes = 65..84 | ForEach-Object { 'ZQ' + [char]$_ }
$jobs = foreach ($p in $prefixes) { Start-Job -ScriptBlock { param($s, $c, $k) & $s @c -Action Insert -Count 1 -KeyPrefix $k } -ArgumentList $script, $conn, $p }
$jobs | Wait-Job | Receive-Job
$jobs | Remove-Job
```

| # | Test | Setup | What to look for |
| --- | --- | --- | --- |
| B1 | A burst, tasks may overlap | The task definition's **Max Concurrent** at its default (20), and the doorbell from before the queued-task check (#82) | How many tasks, whether they ran at the same time, and how long each waited |
| B2 | A burst, one task at a time | **Max Concurrent** 1, and the doorbell from before the check | Whether Task Manager runs them one after another, and how long the queue takes to clear |
| B3 | A burst with the check | **Max Concurrent** 1 and the current doorbell. Start a long save alongside the burst, as one more job: `-Action Insert -Count 100 -KeyPrefix ZQZ` | How many saves skipped, which statuses and start dates `ActiveBGTasks` showed, and how long the look took (`checkMs`; a long one means it waited on another save). For each save that skipped: did a task that ran after it list its token among the doorbell rows it could see? A skipped save no task saw is a change the real sender would miss until its next run. Also check that every task a save relied on (`queuedTaskNumber`) appears in Background Task History: one that doesn't means the look read a submission that later rolled back |

B1 and B2 were run with the doorbell from #82. **B1:** 20 saves in 5 waves made 20 tasks. Each wave's tasks started at the same instant, on one task server, and ran side by side, 0.5 to 7.1 seconds after their saves' last events. Most submissions took 8 to 130 ms; one took 1,178 ms while three other saves were submitting. **B2:** 20 saves made 20 tasks, which ran one at a time across four task servers, never overlapping, about 2.4 seconds apart (each ran under a second), so the last waited 43 seconds. Max Concurrent 1 keeps senders from overlapping, but one task per save builds a queue.

Run the doorbell test on one IDO at a time. Clean up the test records with `-Action Cleanup` between tests. If the submission raises a database error that ends the save's transaction, catching it doesn't save the transaction, and the save fails with the error; that is a result too. The tasks' history is under Background Task History, task name `ue_AIR_DiagDoorbellTask`.

### Doorbell results

Run on November 23, 2026, in the same cloud sandbox, on `SLEndtypes`, with the diagnostic and replication triggers active. Each save rang the doorbell once. The save's last event is the Create Date of its `Diag: IdoPostUpdateCollection` row, which is written before the save commits; the commit itself isn't timed. That and Task Manager's start are site times. The task's code start is by the task server's clock, so its gaps are rough.

| Save | Records | Last event (`Diag: IdoPostUpdateCollection`) | Task Manager started the task | The task's code began | Its first look |
| --- | --- | --- | --- | --- | --- |
| Form insert (D1) | 1 | 15:25:14.70 | 15:25:18.57 (3.9 s later) | 5.7 s after the save | Saw the doorbell row (46 ms) |
| Form insert (D1, again) | 1 | 15:26:52.63 | 15:26:55.23 (2.6 s) | 3.9 s | Saw it (103 ms) |
| REST delete by key (`Cleanup`) | 2 | 15:27:46.17 | 15:27:46.58 (0.4 s) | 0.9 s | Saw it (9 ms) |
| REST insert (D2) | 100 | 15:28:33.46, 42 s after the task was submitted | 15:28:34.63 (1.2 s) | 2.2 s | Saw it (43 ms) |
| REST delete by key (`Cleanup`) | 100 | 15:30:29.68, 45 s after the task was submitted | 15:30:32.97 (3.3 s) | under 4 s | Saw it |
| REST insert that fails (D3) | 3 | Rolled back | No task: none in Background Task History or `ActiveBGTasks` | | |

What the doorbell results establish:

- **D1: a synchronous event handler can submit a background task** through `BGTaskDefinitions.BGTaskSubmit2`. The task got its own number in place of `BG~TASKID~`. It was listed in `ActiveBGTasks` while it ran. Its Initiator and requesting user showed in Background Task History as passed.
- **D2 and D3: the submission is part of the save's transaction** when the handler has Ignore Failure off.
  - D2: the task never started before the save's last event, even when it was submitted 42 and 45 seconds earlier, and its first look found the doorbell row at once. Task Manager most likely couldn't see the submission until the commit.
  - D3: the save that failed left no task and no doorbell rows, while the `IdoOnUpdateCollection` diagnostic row, from a handler with Ignore Failure on, stayed. That is the same split test 10b showed on `IdoPostItemInsert`, now seen on `IdoOnUpdateCollection` too. Seen on one failing save.
- **Task Manager started the task 0.4 to 3.9 seconds after the save's last event**, so at most that long after the commit. The task's code began roughly 0.5 to 1.9 seconds after that, about 1 to 6 seconds after the save's last event in all.
- **The doorbell cost each save about 0.1 to 0.7 seconds**, whatever the number of records. From the `Diag: IdoOnUpdateCollection` row to the `DoorbellSubmit` row it was 0.12 to 0.66 seconds, which includes calling the method and part of the diagnostic handler's own work. The doorbell's own share, from its first row to its second, was 0.06 to 0.41 seconds, and the submission alone 49 to 322 ms. The two form saves cost the most. Every save ran on a newly started app domain (why isn't known), so a save on a server that's already running may cost less. A real doorbell would write one row, not two.

### Burst results

B1 and B2 are summarized with the burst tests above. B3 was run the same day, with **Max Concurrent** 1 and the doorbell with the queued-task check: 20 single-record saves, plus a 100-record save started alongside them.

| | |
| --- | --- |
| Saves | 21 |
| Tasks submitted | 8, one of them the long save's |
| Saves that skipped | 13, each relying on one of three queued tasks |
| Skipped saves the relied-on task could see | 13 of 13 |
| Time for the look in `ActiveBGTasks` (`checkMs`) | 4 to 399 ms, most under 160 ms |

What B3 established:

- **A queued task shows in `ActiveBGTasks` as `READY` with no Start Date.** Every task a save relied on looked like that, and every one of them ran.
- **Every save that skipped was seen by the task it relied on.** Each listed the skipped saves' doorbell rows among those it could see. They were short saves, so each had committed before its task started.
- **The look sees only committed submissions, and doesn't wait for others.** The 100-record save submitted its task at the start of its save. That task wasn't listed by any other save's look during the burst, although it most likely sat uncommitted for about a minute (it started 64 seconds after it was submitted), and no look waited for it. So a save can't rely on a submission that later rolls back.
- **Task Manager ran the committed tasks while the uncommitted one waited.** The long save's task started 64 seconds after its submission and didn't hold up the others; when its save committed wasn't checked.
- **Saves that look at the same moment can't see each other's uncommitted tasks,** so some submit a task of their own. Four saves at the same second each submitted one. That costs extra tasks, not lost changes.
- **The burst's queue cleared about 15 seconds after its last save,** against 43 seconds in B2 for a burst of 20 such saves.
- **Not tested: a long save that skips.** The doorbell rings at the start of a save. A long save that relied on a queued task could see that task start and finish before it commits, and its changes would then wait for the next run. Ringing the doorbell at the end of the save (`IdoPostUpdateCollection`) would leave only the time between that event and the commit.

## The polling cost test

A sender that finds changes by polling runs a query like `RecordDate >= <its last run>` on each IDO it covers. On a large table without an index on that column, every poll reads the whole table. The script's read-only `TimeLoad` action measures what such a poll costs: it runs one `ido/load` per repeat and prints how long each took, through the ION API, including the gateway's round trip. Run each test against the sandbox, with the same `$conn` as the other tests, and in the same PowerShell version throughout (Windows PowerShell 5.1 reuses its connection between calls; PowerShell 7 may not), so the times compare.

First, note how many rows the table has (for example, the record count on the IDO's form, or an aggregate count). A sandbox can hold far fewer rows than production, and a fast scan of a small table says nothing about a large one.

| # | What | Command |
| --- | --- | --- |
| P1 | The cost of the call alone (a filter that reads nothing) | `.\Test-AIRTriggers.ps1 @conn -Action TimeLoad -IDOName SLLedgers -LoadFilter "1=0"` |
| P2 | A poll that finds nothing: how long reading by `RecordDate` takes when no row matches | `.\Test-AIRTriggers.ps1 @conn -Action TimeLoad -IDOName SLLedgers -LoadFilter "RecordDate > '20990101'"` |
| P3 | The same kind of poll on the key (`TransNum`, a number), which has an index | `.\Test-AIRTriggers.ps1 @conn -Action TimeLoad -IDOName SLLedgers -LoadFilter "TransNum > 999999999"` |
| P4 | A poll of the last 5 minutes | `$since = (Get-Date).AddMinutes(-5).ToString('yyyyMMdd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)`, then `.\Test-AIRTriggers.ps1 @conn -Action TimeLoad -IDOName SLLedgers -LoadFilter "RecordDate >= '$since'"` |

Reading the results:

- Compare the first run as well as the median. After the first run, the table's pages are in SQL Server's memory, so later runs show a warm scan.
- P2 much slower than P1 and P3 suggests `RecordDate` has no index, so every poll scans the table. P2 close to P1 suggests an index, but only on a table as large as production's.
- A run that fails or times out is a result: a poll that slow can't run every few seconds.
- `$since` uses the computer's clock, so run P4 on a computer set to the site's time zone.

Repeat P2 on `SLCoitems` and `SLCos` for smaller tables.

### Polling results

Run on November 23, 2026, in the same cloud sandbox, on `SLLedgers` (the general ledger; its row count in the sandbox wasn't recorded), five runs each, through the ION API.

| # | Filter | First run | Median | Fastest |
| --- | --- | --- | --- | --- |
| P1 | `1=0` | 0.78 s | 0.66 s | 0.10 s |
| P2 | `RecordDate > '20990101'` | 9.2 s | 2.4 s | 2.4 s |
| P3 | `TransNum > 999999999` | 0.47 s | 0.25 s | 0.24 s |
| P4 | `RecordDate` within the last 5 minutes | 4.8 s | 2.0 s | 2.0 s |

A poll on the key costs no more than the call itself, while a poll by `RecordDate` takes about 2 seconds more, and 9.2 seconds on P2's first run (cold; P4's first run, 4.8 seconds, came after P2 had read the table). That suggests `RecordDate` has no index on the ledger, so each poll reads the whole table. An index on a standard table isn't an option for this project: it could be lost in an Infor update, and the project is meant to need no database work from an administrator. So polling by `RecordDate` can only be a slow safety net, not how changes are found every few seconds.

## The cascade delete test

Tests whether deleting a parent record fires events for the child records Syteline removes with it. The diagnostic triggers were set up on both `SLCos` and `SLCoitems` (`-Action SetupDiagnostics -IDOName SLCos`, then `-IDOName SLCoitems`), a customer order with one line was created in the Customer Orders form, the line was changed, and the order header was deleted.

| Step | `SLCos` (header) | `SLCoitems` (line) |
| --- | --- | --- |
| Insert the header | `IdoOnItemInsert` with the order number `TBD`, then `IdoPostItemInsert` with the assigned number. RowPointer and keys sent both times | |
| Insert the line | | Insert events with keys and RowPointer |
| After the line was saved | An update with **nothing** sent: no keys, no RowPointer | |
| Change the line | | Update events with keys and RowPointer |
| After the line was saved | An update with nothing sent, again | |
| Delete the header | Delete events with keys and RowPointer | **No events**, though the line was gone afterward |

What it established, seen once, on one order:

- **Deleting an order header removed its line without any `SLCoitems` event.** Syteline most likely deletes the child records some other way than through their IDO, so no trigger on the child sees the delete. Other parent and child pairs most likely behave the same; not tested.
- **A key Syteline assigns during an insert is only known by `IdoPostItemInsert`.** The order number was `TBD` in `IdoOnItemInsert`.
- **Saving an order line was followed by an update of the header that sent nothing the trigger could read.** Most likely Syteline recalculating the order's totals (not checked). A trigger that finds the record by its keys or RowPointer misses that change.

## The IDO definition test

Tests whether an event handler can see someone add an IDO that extends and replaces one that replication watches. That matters to the option beside the [AES plan](Replication-Redesign-AES-Plan), replication on an IDO that extends the replicated one: per the DLLs (below), such an IDO's extension class runs for a save to the base IDO only while it is in the chain of the base's replacer, and an administrator can later add an IDO that leaves it out, for example a second IDO that replaces the same base.

The diagnostic triggers were set up on the standard `IdoCollections` IDO (`-Action SetupDiagnostics -IDOName IdoCollections`), which holds one row per IDO and version (key `CollectionName` and `DevelopmentFlag`, 1 for a checked-out version). That includes the doorbell handler, with Ignore Failure off, so every save in the IDOs form fails while the Rules assembly can't be loaded. Run it only on a test system, and remove it with `-Action RemoveDiagnostics -IDOName IdoCollections` when done. Then a test IDO, `ue_AIRTEST_SLEndtypes`, was created with the New IDO wizard, extending and replacing an existing IDO that itself extends and replaces `SLEndtypes`. It was checked in, checked out and in again, exported, deleted in the IDOs form, and brought back with the IDO Import Wizard.

| Step | Events on `IdoCollections` | The `IdoCollections` row afterward |
| --- | --- | --- |
| Create with the New IDO wizard | `IdoOnUpdateCollection`, `IdoOnItemInsert`, `IdoPostItemInsert`, `IdoPostUpdateCollection`, and the doorbell. The item events had the keys (`CollectionName`, `DevelopmentFlag=1`), no RowPointer | A checked-out version |
| Check in | **None** | `DevelopmentFlag` 0, revision 2, `CreateDate` equal to `RecordDate` |
| Check out, then check in | **None** | Revision 3 |
| Export | None | |
| Delete in the IDOs form | `IdoOnUpdateCollection`, `IdoOnItemDelete`, `IdoPostItemDelete`, `IdoPostUpdateCollection`, and the doorbell. The item events had the RowPointer and the keys, with `DevelopmentFlag=1` though the IDO was checked in | Gone |
| Import with the IDO Import Wizard | **None** | `DevelopmentFlag` 0, revision 3 as in the file, `CreateDate` equal to `RecordDate` |

What it established, seen once:

- **Of the steps tried, only the wizard's create and the delete fired events on `IdoCollections`.** Check-in and import, the two steps that write an IDO's live version, fired none. Most likely they don't go through an `UpdateCollection` save at all: `IdoMethods` lists `IdoCollectionCheckIn` and `ImportFromXmlString` on `IdoCollections`, which are most likely what they call (inferred from the names, not traced). Editing a checked-out IDO in the IDOs form wasn't tried. So an event handler can't be how replication learns that a new replacer has been written. Reading `IdoCollections` can show what is written, but not when each application server starts using it (see below).
- **The Replace checkbox was disabled on the checked-out IDO in the IDOs form**, and **Extends** looked read-only (not confirmed). In earlier testing, outside this run, an import changed Replace on an existing IDO. A save to `IdoCollections` through REST or an extension class wasn't tried.
- **The `CreateDate` and `RecordDate` of an `IdoCollections` row looked like UTC.** They matched the UTC times the diagnostic rows recorded, about five hours ahead of the site's time in the log's Create Date.

### Replace chains in the DLLs

Not tested. This is what `IDOCore.dll` and `IDORequestProcessor.dll` in `Lib/` show:

- **Each runtime app domain works out the replace chains once.** Its IDO metadata cache reads the list of IDOs from the `Collections` table (`IDOListQuery`), checked-out versions included, and gives each base IDO one replacer (`IDOVersionInfoMap.ResolveInheritance`). Nothing reads the list again on a timer or by revision. It is read again when the cache is discarded (`IDORuntime.DiscardMetadataCache`, run by the internal `DiscardMetadataCache` request, most likely what **Refresh Form Metadata** sends; whether one request reaches every server isn't known), or when the app domain is replaced: after 10 minutes without a request (`RuntimeAppDomainManager`), or when the process restarts. So a busy server may keep an old chain until the cache is discarded, and a quiet one picks up a new one by itself.
- **A replacer created with the wizard can go live without a discard.** A server that read the list while the checked-out version existed already treats it as the base's replacer. Until check-in, everyone but the user who checked it out gets the base, because the live version isn't there yet and the runtime asks for it again on each request (`IDODefinitionCollection.LoadIDODefinition`). After check-in, it finds it. An IDO with both a checked-out and a live version takes its Extends and Replace from whichever row the query returns first.
- **A base has one replacer, the top of its chain, and every IDO below that stays in.** When an IDO replaces an IDO that itself replaces a base, the top one becomes the replacer of each IDO below it (`SetReplacedBy`). A save to the base loads the replacer and then each IDO it inherits from (`CreateAggregateIDODefinition`), with their extension classes. So an IDO that extends and replaces replication's IDO keeps it in. One that replaces the same base as replication's IDO cuts it out: the one read last wins, and the query has no `ORDER BY`, so which one that is isn't defined.
- **A replacing IDO whose base isn't in the list breaks IDO metadata on the app domain.** Reading the list throws `Error in IDO metadata: base IDO {0} not found for {1} IDO.`, the list isn't kept, and the next request that needs IDO metadata reads it and throws again, until the row is fixed.

## The SQL replication test

Tests whether replication could use Syteline's own replication framework, which works through SQL triggers on the tables, instead of IDO events. Run in a cloud sandbox on November 24, 2026, with read-only checks in production. It was set up through Syteline's forms; nothing here needs SQL access.

### How the framework fits together

Seen in the forms and through their IDOs:

| Piece | Form (IDO) | What it holds or does |
| --- | --- | --- |
| Categories | **Replication Categories** (`SLRepCategories`, `SLRepObjectCategories`) | The objects to replicate, each a table (Object Type 1) or a stored procedure (2, as in the BOD category), with a filter, Skip Insert, Skip Update, Skip Delete, Skip Method and a To Object Name. A category's **Replication Transfer Type** is **IDO XML**, **Data Lake** or **Business Object Document** |
| Rules | **Replication Rules** (`SLRepRules`) | A category from a source site to a target site, with an **Interval Type** (**Transactional**, **Immediate**, **Minutes**, **Hours**, **Days**), an Interval, a Start Interval At date, **Disable Replication** and **Update All Columns** |
| Triggers | **Replication Management** (`SLReplicationTriggers`, method `RegenerateReplicationTriggers`) | "Changes to replication rules do not take effect until you regenerate the replication triggers." The form has **Regenerate Replication Triggers** with a **Regenerate All Triggers** check box, and no field for a category or table. What a regenerate with the box cleared rebuilds wasn't checked. **View Logs** lists errors (`TriggerLogMessages`) |
| Queue | **Replication Viewer** (`ReplicatedRows3s`, `ShadowValues`) | One row per change: the table, the changed row's `RefRowPointer`, the operation, an `OperationNumber` and the target site. `ShadowValues` has name and value pairs marked old or new, per operation number; it never had rows |
| Sending | Not a form | The messages seen came from `InitiatorApp` `Replicator`. Its failed sends went to the outbox (`CorOutboxEntries`) with the logical ID `lid://ERROR`. No background task for it appeared in production's task history |
| Checks | **Find Replication Setup Issue** | For one table, type and target site, lists what stops it replicating, such as a disabled rule |

Infor's own feed to Data Lake is set up this way: an external target site `DATALAKE` on its own intranet ("Infor DataLake Connection Intranet", Transport `J`, ION transfer type 2), and a `DATALAKE` category of 154 tables with the transfer type **Data Lake**. In Data Lake each table is an object named `csi_` plus the table name (`csi_co_mst`). In production, the newest `csi_` object was from shortly after the tenant moved to new servers, both `DATALAKE` rules (one per site) were disabled, last changed one and six months later, and nothing depended on the feed, so its stop went unnoticed. Production's Replication Management log had errors on three days: lock request timeouts (September 2025 on `coitem_mst` and `itemwhse_mst`, and the day after the newest `csi_` object on `slsman_mst`, `state_mst` and `territory_mst`) and deadlocks (August 2026 on `coitem_mst`, `customer_mst`, `item_mst` and `site`). Whether each was a whole regenerate that failed, and how many regenerates worked without logging anything, isn't known.

### The test

A category `AIRTEST` holding only `endtype_mst`, and a rule from the sandbox's site to the existing `DATALAKE` site with the Interval Type **Days**, an interval of 365 and a Start Interval At date four months away, so that, if the interval held the rows, they would wait in the queue. Then **Regenerate Replication Triggers** (with **Regenerate All Triggers** cleared), and saves on End User Types. The LogOnly rule's AES triggers were active on `SLEndtypes`, so the replication log shows when each AES trigger ran.

| Run | Transfer type | Saves | What happened |
| --- | --- | --- | --- |
| 1 | Data Lake | Insert, update and delete, twice | Nothing in the tables checked: `ReplicatedRows3s`, `ShadowValues`, `DatalakeLogs`, the outbox and `IONMessagingErrors`. **Find Replication Setup Issue** said "No problems found. The replication triggers may need to be regenerated." |
| 2 | IDO XML (regenerated again) | Insert | A queue row (operation 1, the record's `RowPointer`, target `DATALAKE`) appeared 0.35 s after the row was written, and was gone by the next look. The form seemed stuck saving: the AES `IdoPostItemInsert` trigger ran 14.8 s after the row was written. 27 s after the change, the outbox had an error message holding the change |
| 3 | IDO XML | Delete, then an insert | The insert's AES trigger ran 1.1 s after the row was written (the delete's save wasn't timed). Each change became an outbox error message 9 to 11 s after it, one after the other |

Each error message was an IDO `UpdateCollection` request on `TABLE!endtype_mst`, from `Replicator` to the target configuration `DATALAKE`. The inserts carried every column of the row. The delete carried `Action="Delete" UseKeys="Y"` with the table's key columns (`end_user_type`, `site_ref`). After the rule and category were removed and the triggers regenerated, a delete produced no message.

What it established, seen once, with IDO XML, Days and a start date in the future:

- **The trigger's queue row was taken within seconds**, not held for the interval, so another program can't count on reading changes from `ReplicatedRows3s`. Other interval types weren't tried.
- **The messages carry the whole row, and a delete's keys.** Only failed sends were seen, all to `DATALAKE`; a send that worked, or one to another Syteline site, wasn't.
- **The first save of that run was held up by about 15 seconds**; the later insert wasn't. Why wasn't found.

Not established: whether the Data Lake transfer type sends another way or its triggers didn't take effect (run 1), and whether the triggers see changes IDO events miss, such as stored procedures and cascade deletes, which follows from their being SQL triggers but wasn't tried.

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
- **A handler with Ignore Failure on wrote outside the save's transaction**: its rows stayed when the save failed. **With Ignore Failure off, it wrote inside the save's transaction**, and its rows rolled back with the save. Seen on synchronous `IdoPostItemInsert` handlers in a REST insert, and, in the [doorbell test](#doorbell-results), on `IdoOnUpdateCollection`; other events and Transactional handlers weren't tested. The replication triggers have Ignore Failure off, so their log rows roll back with a failed save. Anything they had already sent to a target would stay sent (not observed: the test's rule was `LogOnly`, and a REST insert isn't sent anyway).
- **The asynchronous `IdoPostUpdateCollection` handler never wrote a row**, and the event queue (`EventQueues`) had no entry for it. The cause wasn't found. Until it is, an asynchronous handler can't be counted on to run in this tenant.
- **The replication trigger cost about 0.2 to 0.4 seconds per record** in the form saves, with a `LogOnly` rule, so with no network call at all: loading the rules, loading the record, reading the site's time and writing the log row. If that holds for every record, a form save of 5,000 records that replicates them would spend roughly 17 to 33 minutes in replication before any target is reached. That is an extrapolation. Test 14, which times the replication triggers alone, wasn't run.
- **The time per record barely grows with the number of records in one save.** In REST saves of 100 and 500 records (test 15), each was one `UpdateCollection`, and the time stayed about 0.4 seconds per record, rising by about a tenth over 500 records. Those records weren't replicated (the trigger only wrote a `Skipped` row), and the diagnostic handlers added their own log writes, so 0.4 seconds isn't the cost of replicating a record. Nothing backed up and nothing was dropped: the caller waited. REST calls of up to 3.8 minutes went through the ION API without being cut off. Whether a longer call is cut off, and what happens to the save if it is, wasn't tested. That these large saves were each one transaction was assumed from test 10, not shown.
- **Saves ran on at least five application servers.** Static values in the extension class lasted as long as the server's app domain (one kept counting across saves 8 minutes apart), but a save can land on any server, so an in-memory cache can't be relied on between saves.
- **`System.Transactions.Transaction.Current` was empty inside every handler**, so the payload's `transactionId` can't show which database transaction an event ran in. Tests 10 and 10b answer that question instead.
- **Right after an assembly upload, a handler with Ignore Failure on failed a save.** Right after the Rules assembly was uploaded again, a REST insert failed with `Extension class for ue_AIR_IDOReplicationRules IDO not found`, raised by the diagnostic `IdoOnUpdateCollection` handler, which has Ignore Failure on. The same insert worked when run again a few minutes later; **Refresh Form Metadata** may have been run in between. Seen once, and the cause is unknown. The doorbell test later showed Ignore Failure does take effect on `IdoOnUpdateCollection`, so the open question is whether it covers an extension class that can't be loaded. The replication triggers have Ignore Failure off, so a save to an IDO with active replication triggers would fail the same way in that window.
- **`processId` is blocked** in the cloud (`SecurityException`). `processStartedUtc` (when the assembly's static values were set up in that app domain) and `appDomain` identify the app domain instead.

| # | Assumption | Result |
| --- | --- | --- |
| A1 | A REST save of several records is one transaction, item events in sequence, collection events once | Confirmed for a REST insert of 3 (tests 4, 10). REST inserts and deletes of 100 and 500 records were each one `UpdateCollection` (test 15). A failing REST update (test 11) wasn't run |
| A2 | A form's multi-row save is one `UpdateCollection` | Confirmed (tests 1b, 2, 3). A failing form save wasn't tested, so its transaction wasn't shown |
| A3 | `P("RowPointer")` in item events | Only when the caller sends it. Forms do (inserts after the save). REST callers and extension classes by key or ItemId don't, and their inserts never get it (tests 4 to 9, 12) |
| A4 | `P()` returns keys the caller sends | Confirmed |
| A5 | A handler's writes roll back with a failed save | Only with Ignore Failure off. With it on, they stay. Seen on `IdoPostItemInsert` (test 10b) and `IdoOnUpdateCollection` (doorbell D3) |
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
