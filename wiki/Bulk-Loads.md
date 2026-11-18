# Bulk Loads

Triggers replicate one record at a time as it changes. To send many records at once, run the replication method as a background task. Use it for:
- the initial load of a new rule
- catching up after triggers were inactive or a target was down
- records changed outside the IDO layer, which triggers don't see

## Running it

Background task: `ue_AIR_ReplicateRecordToExternalSystems` (see [Installation](Installation) for the definition). The Test tab on the Rules form is wired to submit this task.

| Parameter | Value |
| --- | --- |
| 1. Background task number | `BG~TASKID~` |
| 2. Filter | An IDO filter that selects the records, for example `CustNum LIKE 'C%'`. Blank selects every record. |
| 3. IDO name | The IDO collection, for example `SLCustomers` |
| 4. Record cap | The most records to load |
| 5. Rule number | Optional. Blank runs every active rule for the IDO. A rule named here runs even when it isn't active, so you can test a rule before switching it on. The Test tab passes the rule you are on. |
| 6. Operation | Optional. Leave it blank for a bulk load: the records are sent as `Upsert` (insert or update, whichever applies), and the log's Source is `BG Task: Bulk`. The Test tab passes `Test`, which sends the same way and is logged with the Source `BG Task: Test`. |

Example parameters:

```
BG~TASKID~, CustNum LIKE 'C%', SLCustomers, 5000
```

## How a bulk run differs from a trigger

| | Trigger | Bulk run |
| --- | --- | --- |
| Records | The one changed record | Every record that matches the filter, up to the record cap |
| Source in the log | `Event: After-Insert`, `Event: After-Update` or `Event: Before-Delete` | `BG Task: Bulk` |
| Operation | `Insert`, `Update` or `Delete` | `Upsert` |
| Event Hubs | Sends `operation` = `Insert`, `Update` or `Delete` | Sends `operation` = `Upsert` |
| Salesforce | Upserts, or deletes if the rule allows | Upserts |
| Replication log | Every record | Every record for 100 or fewer; otherwise failures and warnings only |
| Messages | Replication log only | Also written to the background task's log |

## Reading the result

A bulk run shows as **failed** when a rule fails, or when nothing could be sent because no rule or no record matched. Open the task's **Task Messages** to see why:

| Task message | Meaning |
| --- | --- |
| `Nothing was sent: IDO '...' has no active replication rules.` | No active rule has that IDO Name. Check the spelling and **Is Active**. |
| `Nothing was sent: no '...' record matches the filter "...".` | The filter selected no records |
| `Rule '2' (SalesforceRESTAPI, Upsert): FAILED for 1 record(s). ...` | The send was tried and failed. The error follows. |
| `Rule '2' (SalesforceRESTAPI, Upsert): succeeded for 1 record(s).` | The target accepted the records |

Each record's outcome is also in the [replication log](Replication-Log-and-Retries).

## Tips

- Test with a narrow filter and a small record cap first.
- Records are sent in batches: about 220 KB per batch for Event Hubs, 200 records per batch for Salesforce upserts.
- A bulk run can't send deletes. Records that no longer exist in Syteline aren't loaded, so there's nothing to send.
- Failed records from a bulk run go to the [replication log](Replication-Log-and-Retries) and are retried like any others.
