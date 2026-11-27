# Replication Redesign: AES Plan

**Status: being built.** Phase 1a is built: the queue, capture by the triggers for rules with **Use Queue** on, and a sender that runs on a schedule. See [Replication Queue](Replication-Queue). The doorbell, the retries inside the sender and the Resend form through it come next. The rest of this page is the plan as proposed. It describes one way replication could change, and why, based on the tests in [Trigger Diagnostics](Trigger-Diagnostics). This plan is built on the application event system (AES): event handlers on the replicated IDOs. It doesn't extend the replicated IDOs: everything it adds is event handlers, background tasks, and this project's own tables and IDOs. Another option, extension class code on IDOs that extend the replicated ones, is still to be explored and tested before either is built. The first test for it, of what an event handler can see when an IDO is created, checked in or imported, is [The IDO definition test](Trigger-Diagnostics#the-ido-definition-test), and [Comparing the Options](Replication-Redesign-Comparison) weighs the two. The rest of the wiki describes replication as it works today.

## Goals

1. **No change is lost.** Every insert, update and delete that reaches the database reaches the target, or shows in the replication log as not sent, with the reason.
2. **Close to real time.** A change reaches the target within seconds of its save, well under the delay of an external integration that polls Syteline (about 30 seconds in the case that prompted this work).
3. **Far less cost to the user's save than today.** A large save should spend a fraction of the time it spends replicating now.
4. **Nothing beyond what a standard tenant has.** No indexes on Syteline's tables and no SQL access. An administrator sets it up with this project's install package: its tables, IDOs, forms, event handlers and background task definitions.

## Why today's design can't meet them

Today a trigger on each record's save loads the record and sends it to the target, inside the save. The tests found:

| Problem | Found by |
| --- | --- |
| The trigger finds the record by `P("RowPointer")`, which forms send, and other callers only when they include it. REST and extension class inserts were never replicated, and their updates and deletes only when the caller included `RowPointer` | Tests 4 to 9 and 12 |
| A caller that names records only by ItemId gives the trigger nothing at all: no keys, no RowPointer | Tests 5, 8 and 12 |
| Replicating in the save cost 0.2 to 0.4 seconds per record in the form tests, before anything is sent. A 5,000-record save would, roughly by extrapolation, wait 17 to 33 minutes | The form tests |
| Deleting a parent removed its child without any event on the child IDO | The cascade delete test |
| Saving an order line updated the order header through a save that gave the trigger nothing | The cascade delete test |
| Changes made by Syteline's own stored procedures fire no IDO event | Not tested; follows from how the events work |
| Asynchronous event handlers can't be relied on to run | The asynchronous `IdoPostUpdateCollection` handler never ran |
| In-memory caches don't last: saves spread across several application servers | Every test |

## What the tests showed is possible

- **A synchronous event handler with Ignore Failure off writes inside the save's transaction.** Its rows commit with the save and roll back with it. Seen on `IdoPostItemInsert` (test 10b), `IdoOnUpdateCollection` (D3), and in failed inserts, updates and deletes, which left no row from the item events of the records saved before the failure ([F1](Trigger-Diagnostics#results-of-the-last-tests); for updates and deletes, that those events fired is inferred). `IdoPostUpdateCollection` can't be seen this way, because a failed save doesn't reach it.
- **A save can start a background task, and the task never started before the save's last event.** A failed save left no task. Task Manager started the task 0.4 to 3.9 seconds after the save's last event, and its code ran about 1 to 6 seconds after it (the doorbell tests). A doorbell on `IdoPostUpdateCollection` behaved the same way: its task started about 5 seconds after the doorbell rang (F2).
- **A task definition with Max Concurrent 1 runs one copy at a time,** across every task server (B2, B3).
- **A save can see whether a task is already queued, and skip submitting another.** A queued task shows in `ActiveBGTasks` as `READY` with no Start Date, and only committed submissions were visible. In a burst of 21 saves, 13 skipped and every one of them was picked up (B3).
- **`IdoPostItemInsert` and `IdoPostItemUpdate` get the record's keys from forms and from REST and extension class callers that name records by key,** including a key Syteline assigns during the insert (tests 4, 6, 7 and 12, and the cascade delete test). Not from ItemId callers, and not from Syteline's own follow-on updates, such as the order header after a line save.
- **On the general ledger, a filter on `RecordDate` behaved like a full table scan:** about 2 seconds per poll when the table was in memory and 9 seconds on the first run, against 0.25 seconds on the key (the polling cost test; inferred from timing). On `SLCos`, `SLCoitems` and `SLCustomers` in the sandbox, a date filter cost about what a key load did, medians of 260 to 917 ms (F5); the sandbox's table sizes weren't recorded, and a production table may be larger.
- **A parent's `IdoOnItemDelete` can load its children,** while they still exist: deleting an order logged each of its lines through a rule on the line IDO (F6).
- **A handler that writes one row cost about 90 ms per record in the save,** measured once with the diagnostic handler, which also does more than a queue row would (F4).
- **A REST caller that sends an `_ItemId` property, even one that isn't an ItemID, along with the record's keys, gets the row's real ItemID in `IdoPostItemUpdate`,** with both base tables' RowPointers in it (F8, seen once on `SLCustomers`).

And two results that change the plan:

- **A REST load reads a save's uncommitted rows,** and returns them before the save commits ([F3](Trigger-Diagnostics#results-of-the-last-tests)). A load from an extension class can ask for committed data instead (`ReadMode` `ReadCommitted`, which `SytelineInternalAPI.LoadRecords` already does). That load skipped a running insert's new rows, quickly and without waiting, and returned them once the save committed (F3b, seen once).
- **Lines saved as a nested update of their header fired no events on the line IDO,** and seem to have been written after the header's events (F7, seen once). So no trigger on the line IDO captures them.

## The plan

### 1. A change queue

A new table, `ue_AIR_idorepl_queue_mst` (IDO `ue_AIR_IDOReplicationQueue`), holds one row per changed record: the IDO, the operation (`Insert`, `Update`, `Delete`), the record's key values, its RowPointer when the caller sent it, when it was queued, and, for a delete, what the target needs to delete the record.

### 2. Capture, in the save

The replication triggers stop sending. Each writes a queue row instead, with Ignore Failure off, so the row exists only if the save commits. Like today's triggers, the capture code must never throw: with Ignore Failure off, a failure would fail the user's save.

- **Inserts and updates:** `IdoPostItemInsert` and `IdoPostItemUpdate`, which have the record's keys, including one Syteline assigns during the insert.
- **Deletes:** `IdoOnItemDelete`, while the record still exists. Because the sender can't load a deleted record, the trigger also stores what each rule's target needs to delete it, such as the Salesforce external ID. That loads the record in the save, so a delete costs more than an insert or update.
- **A save that gives the trigger nothing** (an ItemId caller, the header update after a line save) still writes a queue row, marked as having no keys. See the sweep below.

A queue row should cost under 0.1 seconds per record in the save: a diagnostic handler's log row cost about 90 ms (F4), and a queue row does less. Replicating in the save costs 0.2 to 0.4 seconds per record today. A 5,000-record save would then spend roughly 8 minutes or less, not half an hour. To be measured once it's built.

### 3. The doorbell

One more handler, on `IdoPostUpdateCollection`, runs once per save, whatever its size. It submits the sender as a background task, unless `ActiveBGTasks` already shows a sender that is queued and hasn't started, which will pick this save up. It rings at the end of the save, not the start, so a queued sender can't start long before the save commits.

### 4. The sender

One background task definition, with **Max Concurrent** 1, so two senders never run at once. The doorbell submits it, and the every-minute safety net (below) schedules the same definition. Max Concurrent only holds back copies of one definition, so everything that sends goes through this one:

- **Retries** and **the sweep** run inside the sender.
- **The Resend form** (Resend Errors and Bulk Resync) and **the Rules form's Test tab** queue their work for the sender, instead of starting their own task that sends.
- **The retry task, `ue_AIR_RetryFailedReplications`,** is unscheduled once every rule uses the queue. The sender takes over its purge of old `Succeeded` and `Skipped` log rows.

Each run:

Every load the sender makes, of the queue and of the records, asks for committed data (`ReadMode` `ReadCommitted`), as `SytelineInternalAPI.LoadRecords` does. The site's default can read rows a save hasn't committed (F3), and a sender that read those could send a change that later rolls back, or remove a queue row whose save then fails. A committed load skipped a running insert's new rows without waiting for it (F3b, seen once), so a long save shouldn't hold the sender up, and its queue rows wait for the next run. A record a running save is updating most likely reads as its last committed values (not tested); the save's own queue row brings it back once it commits.

1. Reads the queue, and combines rows for the same record, oldest first, so the target is told what happened overall:

   | Earlier | Later | Sent as |
   | --- | --- | --- |
   | Insert | Update | Insert |
   | Insert | Delete | Nothing; logged as inserted and deleted before it was sent |
   | Update | Update | Update |
   | Update | Delete | Delete |
   | Delete | Insert | Delete, then Insert, in that order (the record was deleted and created again) |

   A Salesforce rule upserts either way (today's `OperationAtTarget`), but an Event Hubs consumer is told the operation, so an insert followed by an update must still reach it as an `Insert`.
2. Loads the current values of the records by their keys, an indexed lookup, in batches. A record that no longer exists is logged as not found, and isn't sent unless its row is a delete.
3. Sends them to each rule's target, in the order the rules' dependencies need (below), and writes the replication log as today.
4. Removes each queue row only after its record is sent and logged, so a sender that fails partway sends those records again on the next run instead of losing them. Rows for an IDO with no active rule are logged as skipped and removed.
5. Resends failed records whose retries aren't used up, as the retry task does today.
6. Before it ends, checks whether any queue rows are left, and runs again if there are. A save that commits after that check waits for the next doorbell or the every-minute run.

The field maps, transformations, targets and log stay as they are, and so does the Resend form, apart from queueing its work for the sender. The sender calls the same code that sends today.

### 5. Dependencies between rules

A rule can name the rules it depends on (**Depends On**), for example order lines on orders and items.

- Inserts and updates go out parents first, and deletes children first.
- A child that fails because its parent isn't at the target yet keeps its retries for a time the rule sets, instead of today's 3 tries a minute apart.
- **Deleting a parent deletes its children at the target.** Syteline removed an order's line without its IDO's events, and by the time the sender runs, the children are gone from Syteline. So a child rule says how its records relate to the parent's keys, and when the parent's delete is sent, the sender deletes that parent's children at the target. A target that can be queried, such as Salesforce, is asked for the children by the parent's key; a Salesforce master-detail relationship already deletes them on its own. A target that can't be queried, such as Event Hubs, needs the children's keys captured in the parent's `IdoOnItemDelete`, which can still load them (F6), at a cost to the save, or a "parent deleted" event that the consumer acts on.
- **A parent's insert can queue its children.** Lines saved nested under a new header fired no events of their own (F7). So a child rule can say "when a parent is inserted, send its children": the parent's queue row is enough, and the sender, which reads only committed rows, loads the children by the parent's key. A nested save is one transaction (a failing line rolled the header back), so once the parent's queue row is committed, so are its children. Children added to the parent by a later save, through a path that fires no events, are left to the sweep.
- **A child's change can queue its parent.** A line save changed the order header through a save the trigger couldn't read, so a rule can say "when one of these records changes, send its parent again." That covers the header without a sweep.

### 6. Safety nets

- **A scheduled run every minute,** of the same sender task definition, so a change whose doorbell was missed waits a minute at most.
- **A sweep per rule, by the rule's Incremental Watermark Property** (`RecordDate` by default), every 15 to 60 minutes, set on the rule, so an administrator can space it out on a very large table. It runs inside the sender. It finds records changed since its last run that weren't sent since (for ItemId callers and Syteline's own stored procedures), and re-reads an overlap at least as long as the longest save, because a record's `RecordDate` is stamped before its save commits. Its times come from the site's clock. A queue row with no keys can bring a sweep forward, but no more often than a minimum the rule sets. Polling by a date is too costly on large tables to be the main path, and can't see deletes.

## Known limits

- **A delete the triggers can't identify isn't sent:** a delete from a caller that names the record only by ItemId, or one done by a stored procedure such as a purge utility. It's logged as an unidentified delete where the trigger saw it. Finding the rest would take a reconciliation job that compares the keys at the target with those in Syteline: possible later, per target type, nightly or weekly.
- **Children deleted with a parent that has no rule, or whose child rule doesn't name it in Depends On, aren't deleted at the target.**
- **Inserts and updates the triggers can't identify reach the target only as often as the sweep runs:** ItemId callers, Syteline's own stored procedures, and follow-on updates such as the order header, unless a "queue the parent" rule covers them. That includes children saved as a nested update of their parent, unless a "queue the children" rule covers them.
- **Until every rule uses the queue, an old value can still overwrite a newer one.** While some rules still send in the save (build phase 1), the retry task and the Resend form keep running as separate tasks, and can send a record at the same time as the sender, as they can today.
- **The delay depends on Task Manager.** A single task started up to about 7 seconds after its save in the tests, and a burst of 20 saves cleared about 15 seconds after the last one (B1, B3).
- **For a few minutes after an assembly upload, saves to a replicated IDO can fail,** because the capture triggers and the doorbell have Ignore Failure off and the extension class may not be loaded yet. Deactivate the triggers before uploading.

## Still to test

[The last tests before building](Trigger-Diagnostics#the-last-tests-before-building) answered most of this list ([results](Trigger-Diagnostics#results-of-the-last-tests)).

- ~~Whether Ignore Failure off puts a handler's writes in the save's transaction on `IdoPostItemUpdate`, `IdoOnItemDelete` and `IdoPostUpdateCollection`, and whether a doorbell on `IdoPostUpdateCollection` still never starts its task before the commit~~: yes on the item events (F1). `IdoPostUpdateCollection` can't be shown, because a failed save doesn't reach it, and its doorbell's task started about 5 seconds after the save (F2).
- ~~Whether a load that asks for committed data waits for a running save or skips its rows~~: it skipped a running insert's rows, without waiting (F3b, seen once). A REST load reads them (F3). Rows a running save updates or deletes weren't tested.
- A long save that skips its doorbell, and whether the sender's check for leftover rows catches it.
- ~~Whether `ActiveBGTasks` lists a scheduled run that isn't due yet as `READY`~~: in one read on the test system, scheduled tasks showed as `WAITING` with no Start Date, and the doorbell's look filters on its own task name anyway.
- ~~Whether a parent's `IdoOnItemDelete` can still load its children~~: it can (F6).
- ~~What a queue row costs in the save~~: under about 90 ms per record (F4, one run each way); to be measured again once built.
- ~~What a sweep costs on `SLCos`, `SLCoitems` and other tables~~: medians of 260 to 917 ms in the sandbox (F5); a production table may be larger, so measure there before setting the sweep's interval.
- Whether a purge utility fires any IDO event, callers through ION, whether other parent and child pairs cascade like orders and order lines, and Salesforce lookup and master-detail relationships when a parent is deleted.

## Building it

If this plan is chosen, in phases, each usable on its own:

1. The queue table and IDO, the capture triggers writing queue rows, and the sender with retries inside it, with the doorbell and Max Concurrent 1. Built in three parts: 1a, the queue, capture and a scheduled sender (done; it writes one queue row per rule rather than per record, so a delete can store what each rule sends, and failed sends still go to the retry task); 1b, the doorbell and the retries inside the sender; 1c, the Resend form and the Test tab through the sender. The triggers can switch between today's in-save sending and the queue, rule by rule, while it's proven. The Resend form and the Test tab queue their work for the sender, and the retry task is unscheduled once no rule sends in the save.
2. Dependencies between rules, cascade deletes and parent re-sends.
3. The sweep and the handling of queue rows with no keys.
4. Measurements in the sandbox, then removing the in-save sending.
