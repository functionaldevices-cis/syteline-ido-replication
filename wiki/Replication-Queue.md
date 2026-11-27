# Replication Queue

**Status: new, phase 1 of the [AES plan](Replication-Redesign-AES-Plan).** A rule can send its changes through a queue instead of inside the user's save. Turn it on one rule at a time, with **Use Queue** on the rule, and try it on a test system first.

## Why

A rule without the queue sends each change while the user's save is still running. The save waits for the target, a slow target slows every save, and a send that fails is retried from a stored copy of what was sent.

A rule with the queue only writes a row to the queue in the save, which costs much less. A background task, the sender, sends the change a little later, after the save has committed.

## How it works

1. **Capture, in the save.** The replication triggers pass the record's keys as well as its RowPointer. For each active rule with **Use Queue** on, the trigger writes one row to the queue (`ue_AIR_IDOReplicationQueue`): the IDO, the rule, the operation, the source, the record's keys and its RowPointer when the caller sent it.
   - A **delete** also loads the record while it still exists and stores what the rule would send in the row, because the record is gone by the time the sender runs.
   - The row is written inside the save, so it commits with the save and rolls back with it.
   - Rules without **Use Queue** send in the save, as before.
2. **The sender.** The background task `ue_AIR_SendQueuedReplications` reads the queue, oldest first. It reads only committed rows, so it never picks up a save that is still running or that later fails. For each rule, it combines the rows for the same record:

   | Earlier | Later | Sent as |
   | --- | --- | --- |
   | Insert | Update | Insert |
   | Insert | Delete | Nothing, logged as `Skipped` |
   | Update | Update | Update |
   | Update | Delete | Delete |
   | Delete | Insert | Delete, then Insert |

   Deletes go first, from what was stored when the record was deleted. Inserts and updates load each record's current values by its keys, 50 records at a time, and send them through the same code as every other run. A record that no longer exists gets a `Skipped` row.
3. **The log.** Every record the sender handles gets a row in the [replication log](Replication-Log-and-Retries), with the Source `Queue: After-Insert`, `Queue: After-Update` or `Queue: Before-Delete`. A send that fails is logged as `Failed` with its payload, as today, and the [retry task](Replication-Log-and-Retries#the-retry-task) resends it.
4. **Cleanup.** The sender removes a rule's queue rows once its records were sent and logged. If it can't remove them, it stops, so it doesn't send them again and again.

## Setting it up

1. Install or upgrade (see [Installation](Installation)): the queue table and IDO, the new Rules form, and the Background Task Definition for the sender.
2. On the IDO Replication Configs form, click **Fix Triggers** for each replicated IDO, so its triggers pass the record's keys.
3. On the Rules form, check **Use Queue** on a rule.
4. Run the sender, `ue_AIR_SendQueuedReplications`, on a schedule, every minute, with **Max Concurrent** set to 1 so two never run at once. Until a later version starts it right after each save, a change waits for the next scheduled run.

## What it fixes and what it doesn't

- **REST and extension-class callers that name records by key are now seen.** The triggers pass the keys, so a record can be found without its RowPointer. This also helps rules without the queue: a trigger that gets every key but no RowPointer now loads the record by its keys instead of logging it as `Skipped`.
- **A caller that sends a key blank, or no keys,** still can't be found. Its change gets a `Skipped` row, as before.
- **Lines saved as a nested update of their header** fire no event on the line IDO, so they aren't queued (test F7 in [Trigger Diagnostics](Trigger-Diagnostics#results-of-the-last-tests)). Bulk Resync can send them.
- **A delete whose payload is over 4,000 characters** can't be stored in the queue, so it is sent in the save instead, as before.
- **If the queue row can't be written,** the change is sent in the save instead, so it isn't lost.
- **The Resend form, the Rules form's Test tab, bulk loads and the retry task** still send on their own, outside the queue. A later version moves them into the sender.

## The queue's columns

| Property | Holds |
| --- | --- |
| IDOName | The IDO the record belongs to |
| RuleNum | The rule the row is for |
| Operation | `Insert`, `Update`, `Delete`, or `Upsert` from a trigger that passed none |
| Source | The trigger event, such as `Event: After-Insert` |
| KeyValues | The record's keys, as JSON, when the trigger was given every key |
| RecordRowPointer | The record's RowPointer, when the trigger was given it |
| Payload | For a delete, what the rule would send |
