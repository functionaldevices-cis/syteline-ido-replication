# Troubleshooting and Limitations

## Nothing is replicating

Check these in order:

1. **Triggers exist.** On the IDO Replication Configs form, look at the IDO's status. If it's **Not Setup**, click **Setup Triggers**. If it's **Broken**, click **Fix Triggers**.
2. **Triggers are active.** The status is **Active**. If it's **Inactive** or **Partly Active**, click **Activate All**. Triggers are inactive after **Setup Triggers**. **Fix Triggers** keeps each trigger active or inactive as it was.
3. **A rule is active.** The IDO has at least one rule with **Is Active** checked, and the rule's IDO Name matches the IDO collection name exactly.
4. **The change went through the IDO.** Changes made by stored procedures or direct SQL don't fire triggers.
5. **The log.** Look in `ue_AIR_IDOReplicationLogs` for `Failed` rows for the IDO, and for `Skipped` rows that say the filter matched no record. Such a row shows the filter the trigger passed. `RowPointer = ''` means the trigger wasn't given the saved record's RowPointer.

## Messages in the replication log

| Message | Meaning and fix |
| --- | --- |
| `Rule '...' couldn't load its records from IDO '...'` | The rule's source records couldn't be loaded. Most often its field map names a property the IDO doesn't have, and the message then lists the properties. Fix the field map, then run a [bulk load](Bulk-Loads) for the records that changed in the meantime. Only that rule is affected. If every rule on the IDO shows this message, the cause is something they share, such as the filter of a bulk load. |
| `Replication setup failed for IDO '...'` | The rules themselves couldn't be loaded, or something unexpected failed before the rules ran. The error follows the message. |
| `Unknown target type '...'` | The rule's Target Type isn't `AzureEventHub`, `SalesforceRESTAPI` or `LogOnly` |
| `The payload is over 4,000 characters, so it wasn't stored and can't be retried.` | The record was still sent. Only the log copy was dropped. If it failed, resend it with a [bulk load](Bulk-Loads). |
| `Warning: Field '...': couldn't apply ... so it was sent unchanged` | A [transformation](Replication-Rules-and-Field-Maps#transformations) couldn't convert the value. Check the source property's type and the transformation. |
| `Superseded by a newer replication of this record, so it wasn't resent.` | Expected. The retry task skipped an old failed row because newer data was sent. |
| `The stored payload couldn't be read, so it can't be retried.` | The log row's payload isn't valid JSON. Resend the record with a bulk load. |
| `Delete skipped, because the rule's DeleteBehavior isn't Delete.` | Expected for Salesforce rules with the default Delete Behavior |
| `Nothing was sent: the filter matched no '...' record. The filter was: ...` | A trigger ran but couldn't find the record it was for. The message shows the filter it was given. A blank RowPointer in it means the trigger wasn't given the saved record's RowPointer, which is what happens when a REST API caller saves without sending `RowPointer` (see [Known limitations](#known-limitations)). Nothing was sent, and the row isn't retried. The row has no record ID, so to send those records' current data, run a [Bulk Resync](Resending-Records) for the rule with Changed From set to the earliest such row's Create Date and Changed To blank. A delete can't be resent that way. |

Target-specific messages are on the [Azure Event Hubs](Target-Azure-Event-Hubs#common-errors) and [Salesforce](Target-Salesforce#common-errors) pages.

## Saves are slow on a replicated IDO

Replication runs during the save, so the save waits for the target to answer. Even with no target at all (a `LogOnly` rule), each record added about 0.2 to 0.4 seconds to a form save in testing. If that holds for every record, a save of 5,000 records would spend roughly 17 to 33 minutes in replication (an estimate: the time per record stayed about the same through REST saves of 500 records, but those records weren't replicated). If a target is slow or unreachable:

1. Click **Deactivate All** for the IDO to stop replicating during saves.
2. When the target is back, click **Activate All**.
3. Run a [bulk load](Bulk-Loads) for the records that changed in between.

## The retry task isn't retrying

- The row has `RetriesRemaining` of 0. See [When retries run out](Replication-Log-and-Retries#when-retries-run-out).
- The row has no stored payload.
- The row's rule is inactive or deleted.
- The background task isn't scheduled, or its definition doesn't call `ue_RetryFailedReplications` on the `ue_AIR_IDOReplicationRules` IDO. See [Installation](Installation#4-background-tasks).

## Known limitations

- **Pre-beta.** Test in a non-production environment first.
- **IDO-layer changes only.** Stored procedures and direct SQL don't fire triggers.
- **REST API and extension class saves often don't replicate.** A trigger can only see the properties the caller sent for that record, and it finds the record by its `RowPointer`. Saves made in Syteline's forms replicate. A REST API update replicates only when it includes the record's `RowPointer` (a delete that includes it should work the same way, but hasn't been tested). A REST insert isn't replicated at all, because its RowPointer doesn't exist until the save and the trigger isn't given it. Changes that aren't replicated are logged as `Skipped` with a blank RowPointer in the filter. Found with the [Trigger Diagnostics](Trigger-Diagnostics#results) tests. An extension class that saves through an IDO behaves the same way as a REST caller. ION hasn't been tested. Until replication is changed to handle these saves, send their inserts and updates with a [Bulk Resync](Resending-Records), using the earliest `Skipped` row's Create Date as Changed From. On an IDO with two base tables, check its Incremental Watermark Property (see [Resending Records](Resending-Records)). Deletes can't be caught that way.
- **Sends happen during the save.** A slow target slows the save. A record would also stay sent if the save is later rolled back, for example when a later record in the same save fails, and the replication log row for that send rolls back with the save, so nothing in the log would show it. Testing showed the log row rolling back; the send itself wasn't observed, because the test used a `LogOnly` rule.
- **Credentials are stored as plain text.** See [Credentials](Credentials#keeping-credentials-safe).
- **Log payloads are capped at 4,000 characters.** Larger records are sent but can't be retried from the log.
- **Three retries, no backoff.** A long outage uses them up.
- **Not finished:** Import Map on the Rules form, map field sources, and the output preview on the Rules form.
