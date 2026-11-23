# Replication Redesign: AES Plan

**Status: proposal.** Nothing on this page is built yet. It describes one way replication could change, and why, based on the tests in [Trigger Diagnostics](Trigger-Diagnostics). This plan is built on the application event system (AES): event handlers on the replicated IDOs. It doesn't extend the replicated IDOs: everything it adds is event handlers, background tasks, and this project's own tables and IDOs. Another option, extension class code on IDOs that extend the replicated ones, is still to be explored and tested before either is built. The first test for it, of what an event handler can see when an IDO is created, checked in or imported, is [The IDO definition test](Trigger-Diagnostics#the-ido-definition-test). The rest of the wiki describes replication as it works today.

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

- **A synchronous event handler with Ignore Failure off writes inside the save's transaction.** Its rows commit with the save and roll back with it. Seen on `IdoPostItemInsert` (test 10b) and `IdoOnUpdateCollection` (D3); the other events this design uses aren't tested yet.
- **A save can start a background task, and the task never started before the save's last event.** A failed save left no task. Task Manager started the task 0.4 to 3.9 seconds after the save's last event, and its code ran about 1 to 6 seconds after it (the doorbell tests).
- **A task definition with Max Concurrent 1 runs one copy at a time,** across every task server (B2, B3).
- **A save can see whether a task is already queued, and skip submitting another.** A queued task shows in `ActiveBGTasks` as `READY` with no Start Date, and only committed submissions were visible. In a burst of 21 saves, 13 skipped and every one of them was picked up (B3).
- **`IdoPostItemInsert` and `IdoPostItemUpdate` get the record's keys from forms and from REST and extension class callers that name records by key,** including a key Syteline assigns during the insert (tests 4, 6, 7 and 12, and the cascade delete test). Not from ItemId callers, and not from Syteline's own follow-on updates, such as the order header after a line save.
- **On the general ledger, a filter on `RecordDate` behaved like a full table scan:** about 2 seconds per poll when the table was in memory and 9 seconds on the first run, against 0.25 seconds on the key (the polling cost test; inferred from timing, other tables not tested).

## The plan

### 1. A change queue

A new table, `ue_AIR_idorepl_queue_mst` (IDO `ue_AIR_IDOReplicationQueue`), holds one row per changed record: the IDO, the operation (`Insert`, `Update`, `Delete`), the record's key values, its RowPointer when the caller sent it, when it was queued, and, for a delete, what the target needs to delete the record.

### 2. Capture, in the save

The replication triggers stop sending. Each writes a queue row instead, with Ignore Failure off, so the row exists only if the save commits. Like today's triggers, the capture code must never throw: with Ignore Failure off, a failure would fail the user's save.

- **Inserts and updates:** `IdoPostItemInsert` and `IdoPostItemUpdate`, which have the record's keys, including one Syteline assigns during the insert.
- **Deletes:** `IdoOnItemDelete`, while the record still exists. Because the sender can't load a deleted record, the trigger also stores what each rule's target needs to delete it, such as the Salesforce external ID. That loads the record in the save, so a delete costs more than an insert or update.
- **A save that gives the trigger nothing** (an ItemId caller, the header update after a line save) still writes a queue row, marked as having no keys. See the sweep below.

A queue row should cost about 0.1 seconds per record in the save, about what a diagnostic trigger's log row cost, against 0.2 to 0.4 seconds for replicating in the save today. A 5,000-record save would then spend roughly 8 minutes, not half an hour. To be measured once it's built.

### 3. The doorbell

One more handler, on `IdoPostUpdateCollection`, runs once per save, whatever its size. It submits the sender as a background task, unless `ActiveBGTasks` already shows a sender that is queued and hasn't started, which will pick this save up. It rings at the end of the save, not the start, so a queued sender can't start long before the save commits.

### 4. The sender

One background task definition, with **Max Concurrent** 1, so two senders never run at once. The doorbell submits it, and the every-minute safety net (below) schedules the same definition. Max Concurrent only holds back copies of one definition, so everything that sends goes through this one:

- **Retries** and **the sweep** run inside the sender.
- **The Resend form** (Resend Errors and Bulk Resync) and **the Rules form's Test tab** queue their work for the sender, instead of starting their own task that sends.
- **The retry task, `ue_AIR_RetryFailedReplications`,** is unscheduled once every rule uses the queue. The sender takes over its purge of old `Succeeded` and `Skipped` log rows.

Each run:

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
- **Deleting a parent deletes its children at the target.** Syteline removed an order's line without its IDO's events, and by the time the sender runs, the children are gone from Syteline. So a child rule says how its records relate to the parent's keys, and when the parent's delete is sent, the sender deletes that parent's children at the target. A target that can be queried, such as Salesforce, is asked for the children by the parent's key; a Salesforce master-detail relationship already deletes them on its own. A target that can't be queried, such as Event Hubs, needs the children's keys captured in the parent's `IdoOnItemDelete`, at a cost to the save, or a "parent deleted" event that the consumer acts on.
- **A child's change can queue its parent.** A line save changed the order header through a save the trigger couldn't read, so a rule can say "when one of these records changes, send its parent again." That covers the header without a sweep.

### 6. Safety nets

- **A scheduled run every minute,** of the same sender task definition, so a change whose doorbell was missed waits a minute at most.
- **A sweep per rule, by the rule's Incremental Watermark Property** (`RecordDate` by default), every 15 to 60 minutes, set on the rule, so an administrator can space it out on a very large table. It runs inside the sender. It finds records changed since its last run that weren't sent since (for ItemId callers and Syteline's own stored procedures), and re-reads an overlap at least as long as the longest save, because a record's `RecordDate` is stamped before its save commits. Its times come from the site's clock. A queue row with no keys can bring a sweep forward, but no more often than a minimum the rule sets. Polling by a date is too costly on large tables to be the main path, and can't see deletes.

## Known limits

- **A delete the triggers can't identify isn't sent:** a delete from a caller that names the record only by ItemId, or one done by a stored procedure such as a purge utility. It's logged as an unidentified delete where the trigger saw it. Finding the rest would take a reconciliation job that compares the keys at the target with those in Syteline: possible later, per target type, nightly or weekly.
- **Children deleted with a parent that has no rule, or whose child rule doesn't name it in Depends On, aren't deleted at the target.**
- **Inserts and updates the triggers can't identify reach the target only as often as the sweep runs:** ItemId callers, Syteline's own stored procedures, and follow-on updates such as the order header, unless a "queue the parent" rule covers them.
- **Until every rule uses the queue, an old value can still overwrite a newer one.** While some rules still send in the save (build phase 1), the retry task and the Resend form keep running as separate tasks, and can send a record at the same time as the sender, as they can today.
- **The delay depends on Task Manager.** A single task started up to about 7 seconds after its save in the tests, and a burst of 20 saves cleared about 15 seconds after the last one (B1, B3).
- **For a few minutes after an assembly upload, saves to a replicated IDO can fail,** because the capture triggers and the doorbell have Ignore Failure off and the extension class may not be loaded yet. Deactivate the triggers before uploading.

## Still to test

- Whether Ignore Failure off puts a handler's writes in the save's transaction on `IdoPostItemUpdate`, `IdoOnItemDelete` and `IdoPostUpdateCollection`, and whether a doorbell on `IdoPostUpdateCollection` still never starts its task before the commit.
- A long save that skips its doorbell, and whether the sender's check for leftover rows catches it.
- Whether the sender's read of the queue waits on, or skips, a save's uncommitted queue rows.
- Whether `ActiveBGTasks` lists a scheduled run that isn't due yet as `READY`, which would let a save rely on a run up to a minute away.
- Whether a parent's `IdoOnItemDelete` can still load its children, for targets that need their keys.
- What a queue row costs in the save, on a server that's already running.
- What a sweep costs on `SLCos`, `SLCoitems` and other tables, not only the ledger.
- Whether a purge utility fires any IDO event, callers through ION, whether other parent and child pairs cascade like orders and order lines, and Salesforce lookup and master-detail relationships when a parent is deleted.

## Building it

If this plan is chosen, in phases, each usable on its own:

1. The queue table and IDO, the capture triggers writing queue rows, and the sender with retries inside it, with the doorbell and Max Concurrent 1. The triggers can switch between today's in-save sending and the queue, rule by rule, while it's proven. The Resend form and the Test tab queue their work for the sender, and the retry task is unscheduled once no rule sends in the save.
2. Dependencies between rules, cascade deletes and parent re-sends.
3. The sweep and the handling of queue rows with no keys.
4. Measurements in the sandbox, then removing the in-save sending.
