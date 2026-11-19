# Setting Up Triggers

Triggers are what make replication event-driven. They are Application Event System (AES) event handlers on the source IDO that call the replication method when a record is inserted, updated or deleted.

Form: **IDO Replication Configs** (`ue_AIR_IDOReplicationConfigs`). It lists every IDO collection, with its trigger status and its replication rules.

## Set up triggers on an IDO

1. Find the IDO on the form.
2. Click **Setup Triggers**.
3. Click **Activate All**. New triggers are created inactive, so nothing replicates until you activate them. The status shows **Inactive** until you do.

The IDO also needs at least one active [rule](Replication-Rules-and-Field-Maps). With triggers but no active rules, the replication method returns without sending anything.

## Buttons

| Button | What it does |
| --- | --- |
| Setup Triggers | Creates the three event handlers and their actions, inactive |
| Activate All | Activates the IDO's replication triggers |
| Deactivate All | Deactivates them. Use this to pause replication for an IDO without removing anything. |
| Fix Triggers | Removes and re-creates the triggers. Each trigger that was active before is active again afterward, and each that was inactive stays inactive. |
| Remove Triggers | Deletes the IDO's replication triggers and their actions |
| Configure Rules | Opens the Rules form |

## Status

| Status | Meaning |
| --- | --- |
| Not Setup | The IDO has no replication triggers |
| Active | All three triggers exist, their actions match what this version expects, and all of them are active. Changes to the IDO's records are replicated. |
| Inactive | The triggers are set up, but none is active, so nothing replicates. This is the status right after **Setup Triggers** and after **Deactivate All**. Click **Activate All**. |
| Partly Active | The triggers are set up, but only some are active. For example, inserts replicate and deletes don't. The **Is Active** column in the trigger grid shows which. Click **Activate All**. |
| Broken | Some triggers exist, but one is missing or its action doesn't match. Click **Fix Triggers**. |

If **Fix Triggers** fails partway, the error says what state the triggers are in and what to click next.

A status of **Broken** is expected after an upgrade that changes the trigger action. The comparison is exact, so any edit to a trigger's action parameters also shows as Broken.

## What gets created

Three event handlers on the IDO, each with the description `ue_AIR_ReplicationTrigger`:

| Event | Operation passed |
| --- | --- |
| `IdoPostItemInsert` | `Insert` |
| `IdoPostItemUpdate` | `Update` |
| `IdoOnItemDelete` | `Delete` |

Each handler has one event action that calls:

```
IDO("ue_AIR_IDOReplicationRules") METHOD("ue_ReplicateRecordToExternalSystems")
PARMS(-1, SUBSTITUTE("RowPointer = '{0}'", P("RowPointer")), "<IDO name>", 1, "", "<operation>")
```

The parameters are: no background task (`-1`), a filter for the changed row, the IDO name, a record cap of 1, a blank rule number (every active rule), and the operation.

The delete trigger runs before the row is removed, so the record can still be read and sent.

## Good to know

- **Only IDO-layer changes fire triggers.** A change made by a stored procedure or by direct SQL doesn't raise these events, so it isn't replicated. Use a [bulk load](Bulk-Loads) to catch those up.
- **Replication runs during the save.** The user's save waits while the record is sent. A slow target makes saves on that IDO slower. If a target is down for a while, consider **Deactivate All** until it's back, then run a bulk load.
- **Errors don't fail the save.** They go to the [replication log](Replication-Log-and-Retries).
