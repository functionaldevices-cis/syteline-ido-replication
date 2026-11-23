# Replication Redesign

**Status: proposal.** Nothing on this page is built yet. It describes how replication is meant to change, and why, based on the tests in [Trigger Diagnostics](Trigger-Diagnostics). The rest of the wiki describes replication as it works today.

## Goals

1. **No change is lost.** Every insert, update and delete that reaches the database reaches the target, or shows in the replication log as not sent, with the reason.
2. **Close to real time.** A change reaches the target within seconds of its save, under the 30 seconds or so an external polling app takes.
3. **Little cost to the user's save.** A save of thousands of records shouldn't wait minutes for replication.
4. **Nothing beyond what a standard tenant has.** No indexes on Syteline's tables, no SQL access, no extending IDOs: an administrator sets it up with this project's forms.

## Why today's design can't meet them

Today a trigger on each record's save loads the record and sends it to the target, inside the save. The tests found:

| Problem | Found by |
| --- | --- |
| The trigger finds the record by `P("RowPointer")`, which only forms send. REST and extension class inserts are never replicated, and their updates and deletes only when the caller includes `RowPointer` | Tests 4 to 9 and 12 |
| A caller that names records only by ItemId gives the trigger nothing at all: no keys, no RowPointer | Tests 5, 8 and 12 |
| Replicating in the save costs 0.2 to 0.4 seconds per record before anything is sent, so a 5,000-record save would wait roughly 17 to 33 minutes | The form tests and test 15 |
| Deleting a parent removes its children without any event on the child IDO | The cascade delete test |
| Saving an order line updates the order header through a save that gives the trigger nothing | The cascade delete test |
| Changes made by Syteline's own stored procedures fire no IDO event | Not tested; follows from how the events work |
| Asynchronous event handlers can't be relied on to run | The asynchronous `IdoPostUpdateCollection` handler never ran |
| In-memory caches don't last: saves spread across several application servers | Every test |

## What the tests showed is possible

- **A synchronous event handler with Ignore Failure off writes inside the save's transaction.** Its rows commit with the save and roll back with it (tests 10b and D3).
- **A save can start a background task, and the task never starts before the save commits.** A failed save leaves no task. Task Manager started the task 0.4 to 3.9 seconds after the save's last event, and its code ran about 1 to 6 seconds after it (the doorbell tests).
- **A task definition with Max Concurrent 1 runs one copy at a time,** across every task server (B2).
- **A save can see whether a task is already queued, and skip submitting another.** A queued task shows in `ActiveBGTasks` as `READY` with no Start Date, and only committed submissions are visible. In a burst of 21 saves, 12 skipped and every one of them was picked up (B3).
- **Every caller except an ItemId caller gives the trigger the record's keys,** in `IdoPostItemInsert` and `IdoPostItemUpdate` (tests 4 to 9 and 12, and the cascade delete test, where a key Syteline assigns is filled in by `IdoPostItemInsert`).
- **Polling by `RecordDate` reads the whole table.** About 2 seconds per poll on the general ledger when the table is in memory, 4 to 9 seconds when it isn't, against 0.25 seconds on the key (the polling cost test).

## The design

### 1. A change queue

A new table, `ue_AIR_idorepl_queue_mst` (IDO `ue_AIR_IDOReplicationQueue`), holds one row per changed record: the IDO, the operation (`Insert`, `Update`, `Delete`), the record's key values, its RowPointer when the caller sent it, when it was queued, and, for a delete, what the target needs to delete the record. Rows are removed once the record is sent or logged.

### 2. Capture, in the save

The replication triggers stop sending. Each writes a queue row instead, with Ignore Failure off, so the row exists only if the save commits:

- **Inserts and updates:** `IdoPostItemInsert` and `IdoPostItemUpdate`, which have the record's keys, including one Syteline assigns during the insert.
- **Deletes:** `IdoOnItemDelete`, while the record still exists. Because the sender can't load a deleted record, the trigger also stores what each rule's target needs to delete it, such as the Salesforce external ID.
- **A save that gives the trigger nothing** (an ItemId caller, the header update after a line save) still writes a queue row, marked as having no keys. The sender treats it as "this IDO changed around this time": see the sweep below.

Writing one queue row should cost about 0.1 seconds per record, about what a diagnostic trigger's log row cost, against 0.2 to 0.4 seconds for replicating in the save today. To be measured once it's built.

### 3. The doorbell

One more handler, on `IdoPostUpdateCollection`, runs once per save, whatever its size. It submits the sender as a background task, unless `ActiveBGTasks` already shows a sender that is queued and hasn't started, which will pick this save up. It rings at the end of the save, not the start, so a queued sender can't start long before the save commits.

### 4. The sender

A background task definition with **Max Concurrent** 1, so two senders never run at once. Each run:

1. Reads the queue, and combines rows for the same record: the latest operation wins.
2. Loads the current values of the records by their keys, an indexed lookup, in batches.
3. Sends them to each rule's target, in the order the rules' dependencies need (below), and writes the replication log as today.
4. Before it ends, looks for queue rows written after it started, and runs again if there are any. That covers a save that skipped its own doorbell and committed just after the sender began.

The field maps, transformations, targets, log, retries and Resend form stay as they are. The sender calls the same code that sends today.

### 5. Dependencies between rules

A rule can name the rules it depends on (**Depends On**), for example order lines on orders and items.

- Inserts and updates go out parents first, and deletes children first.
- A child that fails because its parent isn't at the target yet is retried, not given up.
- **Deleting a parent deletes its children at the target.** Syteline removes child records without their IDO's events, so a child rule says how its records relate to the parent's keys, and when the parent's delete is sent, the sender deletes that parent's children at the target too. A Salesforce master-detail relationship already does this on its own; a lookup relationship doesn't.
- **A child's change can queue its parent.** A line save changed the order header through a save the trigger couldn't read, so a rule can say "when one of these records changes, send its parent again."

### 6. Safety nets

- **A scheduled run every minute,** so a change whose doorbell was missed waits a minute at most.
- **A `RecordDate` sweep per rule,** every 15 to 60 minutes, set on the rule, so an administrator can space it out on a very large table. It finds records changed since the last sweep that weren't queued: ItemId callers, Syteline's own stored procedures. A queue row with no keys asks for an early sweep of its IDO. Polling is too costly on large tables to be the main path (see the polling cost test), and can't see deletes.

## Known limits

- **A delete the triggers can't identify isn't sent:** a delete from a caller that names the record only by ItemId, or one done by a stored procedure such as a purge utility. It's logged as an unidentified delete where the trigger saw it. Finding the rest would take a reconciliation job that compares the keys at the target with those in Syteline: possible later, per target type, nightly or weekly.
- **The sweep catches stored-procedure inserts and updates only as often as it runs.**
- **The delay depends on Task Manager.** Usually a few seconds; under load, or on the hour when scheduled tasks start together, it was up to about 7 seconds in the tests.

## Still to test

- What a queue row costs in the save, on a server that's already running.
- Whether a purge utility fires any IDO event.
- Callers through ION.
- Whether other parent and child pairs cascade like orders and order lines.
- Salesforce lookup and master-detail relationships when a parent is deleted.

## Building it

In phases, each usable on its own:

1. The queue table and IDO, the capture triggers writing queue rows, and the sender, with the doorbell and Max Concurrent 1. The triggers can switch between today's in-save sending and the queue, rule by rule, while it's proven.
2. Dependencies between rules, cascade deletes and parent re-sends.
3. The `RecordDate` sweep and the handling of queue rows with no keys.
4. Measurements in the sandbox, then removing the in-save sending.
