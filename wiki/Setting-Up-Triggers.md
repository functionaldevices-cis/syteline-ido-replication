# Setting Up Triggers

Triggers are what make replication event-driven. They are Application Event System (AES) event handlers on the source IDO that call the replication method when a record is inserted, updated or deleted.

Form: **IDO Replication Configs** (`ue_AIR_IDOReplicationConfigs`). It lists every IDO collection, with its trigger status and its replication rules.

## Set up triggers on an IDO

1. Find the IDO on the form.
2. Click **Setup Triggers**.
3. Click **Activate All**. New triggers are created inactive, so nothing replicates until you activate them.

The IDO also needs at least one active [rule](Replication-Rules-and-Field-Maps). With triggers but no active rules, the replication method returns without sending anything.

## Buttons

| Button | What it does |
| --- | --- |
| Setup Triggers | Creates the three event handlers and their actions, inactive |
| Activate All | Activates the IDO's replication triggers |
| Deactivate All | Deactivates them. Use this to pause replication for an IDO without removing anything. |
| Fix Triggers | Removes and re-creates the triggers. The new triggers are inactive, so click **Activate All** afterward. |
| Remove Triggers | Deletes the IDO's replication triggers and their actions |
| Configure Rules | Opens the Rules form |

## Status

| Status | Meaning |
| --- | --- |
| Not Setup | The IDO has no replication triggers |
| Setup | All three triggers exist and their actions match what this version expects |
| Broken | Some triggers exist, but one is missing or its action doesn't match. Click **Fix Triggers**. |

The status doesn't say whether the triggers are active. Check the **Active** column in the trigger grid.

A status of **Broken** is expected after an upgrade that changes the trigger action. The comparison is exact, so any edit to a trigger's action parameters also shows as Broken.

## What gets created

Three event handlers on the IDO, each with the description `ue_AIR_ReplicationTrigger`:

| Event | Operation code passed | Shown in the log as |
| --- | --- | --- |
| `IdoPostItemInsert` | `EAI` | Event: After-Insert |
| `IdoPostItemUpdate` | `EAU` | Event: After-Update |
| `IdoOnItemDelete` | `EBD` | Event: Before-Delete |

Each handler has one event action that calls:

```
IDO("ue_AIR_IDOReplicationRules") METHOD("ue_ReplicateRecordToExternalSystems")
PARMS(-1, SUBSTITUTE("RowPointer = '{0}'", P("RowPointer")), "<IDO name>", 1, "", "<operation code>")
```

The parameters are: no background task (`-1`), a filter for the changed row, the IDO name, a record cap of 1, a blank rule number (every active rule), and the operation code.

The delete trigger runs before the row is removed, so the record can still be read and sent.

## Good to know

- **Only IDO-layer changes fire triggers.** A change made by a stored procedure or by direct SQL doesn't raise these events, so it isn't replicated. Use a [bulk load](Bulk-Loads) to catch those up.
- **Replication runs during the save.** The user's save waits while the record is sent. A slow target makes saves on that IDO slower. If a target is down for a while, consider **Deactivate All** until it's back, then run a bulk load.
- **Errors don't fail the save.** They go to the [replication log](Replication-Log-and-Retries).
