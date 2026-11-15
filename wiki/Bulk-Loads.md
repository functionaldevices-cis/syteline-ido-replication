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
| 5. Rule number | Optional. Blank runs every active rule for the IDO. |

Example parameters:

```
BG~TASKID~, CustNum LIKE 'C%', SLCustomers, 5000
```

## How a bulk run differs from a trigger

| | Trigger | Bulk run |
| --- | --- | --- |
| Records | The one changed record | Every record that matches the filter, up to the record cap |
| Operation | `Insert`, `Update` or `Delete` | Unknown. Records are treated as inserts or updates. |
| Event Hubs | Sends the `operation` user property | Sends no `operation` property |
| Salesforce | Upserts, or deletes if the rule allows | Upserts |
| Replication log | Every record | Every record for 100 or fewer; otherwise failures and warnings only |
| Messages | Replication log only | Also written to the background task's log |

## Tips

- Test with a narrow filter and a small record cap first.
- Records are sent in batches: about 220 KB per batch for Event Hubs, 200 records per batch for Salesforce upserts.
- A bulk run can't send deletes. Records that no longer exist in Syteline aren't loaded, so there's nothing to send.
- Failed records from a bulk run go to the [replication log](Replication-Log-and-Retries) and are retried like any others.
