# Resending Records

The retry task resends a failed record up to 3 times on its own. For anything it didn't recover, such as an outage that lasted longer than the retries, resend by hand from the **IDO Replication Resend** form.

Form: `ue_AIR_IDOReplicationResend`

Select a rule in the list. Everything on the form applies to that one rule. The form has two groups:

| Group | What it resends |
| --- | --- |
| Resend Errors | Every record that has a `Failed` row in the replication log for the rule |
| Bulk Resync | The records changed in a period that you choose, narrowed by a filter and a limit if you want |

Both run as a background task, so the form doesn't wait. Look at the task's messages for the result.

## Resend Errors

The grid lists the rule's `Failed` log rows that the button acts on, newest first. It also lists failed deletes, and rows with no record row pointer, that still have retries left. The button leaves those alone, because the retry task is about to resend them. It shows up to 200 rows. The button takes all of them, not only the ones shown. A failed row that nothing can resend isn't listed: one with no stored payload whose record can't be reloaded, because it has no record row pointer or was a delete. Rows logged for a different IDO, from before the rule's IDO Name was changed, aren't listed either. All of those rows are on the [replication log](Replication-Log-and-Retries) form.

Click **Resend All Errors**. There is nothing to fill in.

The grid doesn't change when you click, because the task runs in the background. Refresh the form after the task finishes to see what is left. A row for a failed delete, or one with no record row pointer, stays listed until the retry task has sent its stored payload.

## Bulk Resync

1. **Changed From** and **Changed To** set the period. The dates are compared with the rule's [Incremental Watermark Property](Replication-Rules-and-Field-Maps#incremental-watermark-property), which is `RecordDate` unless the rule names another. Both are optional. A blank date puts no limit on that side.
2. **Filter** is an IDO filter on the rule's IDO, as on the Rules form's Test tab, for example `Item LIKE 'AB%'`. It is optional. Don't use a comma in it: the background task separates its parameters with commas.
3. **Limit** is the most records to send. `0` means no limit.
4. Click **Resync**.

With no dates, no filter and a limit of 0, a bulk resync sends every record of the rule's IDO. If nothing matches the period and the filter, the task shows as failed and its messages say so.

A bulk resync doesn't look for failed records. It sends what the period and the filter select. If one of those records has a `Failed` row, that row is brought up to date, as described below.

## What gets sent

A resend reloads each record from Syteline and sends its **current** values. It never sends an old stored payload, so it can't put older data over newer data.

- The records go through the rule's current field map and options, as an `Upsert`.
- The replication log gets new rows with the Source `BG Task: Resend`.
- A rule that isn't active can be resent too.

## What happens to the failed rows

After **Resend Errors**:

| The failed row's record | Result |
| --- | --- |
| Was reloaded and sent again | The row becomes `Skipped`, with the message that a manual resend replaced it. The resend's own row shows whether it worked. |
| Couldn't be sent, because the rule failed before it got to its records | The row gets its 3 retries back, so the retry task still resends its stored payload. Fix the rule, then resend again. |
| No longer exists in Syteline | The row becomes `Skipped`. There is nothing left to send. |
| Was a delete | If the row is out of retries, it gets its 3 retries back. A deleted record can't be reloaded, so the [retry task](Replication-Log-and-Retries#the-retry-task) resends the delete from its stored payload. |
| Has no record row pointer | The same. This applies to rows logged before the record row pointer was stored. |
| Has no stored payload, and is a delete or has no record row pointer | Nothing can resend the row. The task's messages count these. Use **Bulk Resync** to send those records' current data. |

After **Bulk Resync**:

| The failed row's record | Result |
| --- | --- |
| Was part of the resync | The row becomes `Skipped`, with the message that a manual resend replaced it. |
| Wasn't part of the resync | The row is left as it was, with the retries it had. A bulk resync never gives an old failure its retries back. |

## Things to know

- **Use an open-ended period for a recovery.** With Changed To left blank there is no end date at all, so a record changed several times since the start is still included, and the result doesn't depend on the server's clock. With a Changed To in the past, a record that was changed again after it isn't picked up.
- **A period needs a date property to compare with.** To use Changed From or Changed To, the rule's IDO has to have the rule's Incremental Watermark Property. The default, `RecordDate`, is on standard Syteline IDOs. If the IDO doesn't have the property, the load fails, nothing is sent, and the task's messages say so. If the IDO has it but it isn't a date, the task stops before it loads anything.
- **`RecordDate` doesn't see every change on some IDOs.** An IDO built on two tables can have a record date for each. `SLCustomers` has `RecordDate` and `AdrRecordDate`, and a change to an address field only moves the second. A period by `RecordDate` misses that record. Point the rule's Incremental Watermark Property at a property that covers both.
- **It can't resend a delete that never failed.** If a delete was never replicated, for example because triggers were inactive, nothing in Syteline or the log says the record existed.
- **One Resend Errors run reloads the records of 10,000 failed rows at most.** The task's messages say when it reached that. Run it again for the rest: the rows it has resent are `Skipped` by then, so the next run takes the following ones.
- **While a resend runs, the retry task leaves its records alone.** The resend takes the failed rows it may replace out of the retry queue first, so a stored payload can't be sent after the current data. A bulk resync does this for the failed rows whose record can be reloaded and that still have retries, and puts back the retries of the ones it didn't replace. If the task is stopped partway, those rows are left with no retries. Click **Resend All Errors** to pick them up.
- **Dates are read in these forms:** `2026-11-18 14:00:00`, `20261118 14:00:00`, or the server's own date format. If the task says a date couldn't be read, enter it in the first form.

## Running it without the form

Background task: `ue_AIR_ResendReplications` (see [Installation](Installation#4-background-tasks)).

| Parameter | Value |
| --- | --- |
| 1. Background task number | `BG~TASKID~` |
| 2. Rule number | The rule to resend for |
| 3. From | The start of the period. Blank means no start. |
| 4. To | The end of the period. Blank means no end. |
| 5. Scope | `Failures` (Resend Errors) or `Everything` (Bulk Resync) |
| 6. Filter | An IDO filter. Only used by `Everything`. |
| 7. Limit | The most records to send. Blank or `0` means no limit. Only used by `Everything`. |

With `Failures`, a From or To date takes only the rows that failed in that period. The form passes neither, which takes every failed row of the rule.
