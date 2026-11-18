# Resending Records

The retry task resends a failed record up to 3 times on its own. For anything it didn't recover, such as an outage that lasted longer than the retries, resend by hand from the **IDO Replication Resend** form.

Form: `ue_AIR_IDOReplicationResend`

## Using the form

1. Select the rule to resend for. One resend covers one rule.
2. Set **Changed From** to the start of the period. Leave **Changed To** blank to go up to now.
3. Choose what to resend:

   | Choice | Resends |
   | --- | --- |
   | Only the records that failed | Every record that has a `Failed` row in the replication log for this rule in the period |
   | Every record changed in the period | Every record whose Record Date falls in the period, plus the records that failed in it |

4. Click **Resend**. It runs as a background task, so the form doesn't wait. Look at the task's messages for the result.

## What gets sent

A resend reloads each record from Syteline and sends its **current** values. It never sends an old stored payload, so it can't put older data over newer data.

- The records go through the rule's current field map and options, as an `Upsert`.
- The replication log gets new rows with the Source `BG Task: Resend`.
- A rule that isn't active can be resent too.

## What happens to the failed rows

| The failed row's record | Result |
| --- | --- |
| Was reloaded and sent again | The row becomes `Skipped`, with the message that a manual resend replaced it. The resend's own row shows whether it worked. |
| No longer exists in Syteline | The row becomes `Skipped`. There is nothing left to send. |
| Was a delete | The row gets its 3 retries back. A deleted record can't be reloaded, so the [retry task](Replication-Log-and-Retries#the-retry-task) resends the delete from its stored payload. |
| Has no source row pointer | The row gets its 3 retries back, for the same reason. This applies to rows logged before the source row pointer was stored. |

## Things to know

- **Use an open-ended period for a recovery.** With Changed To left blank, a record changed several times since the start is still included. With a Changed To in the past, a record that was changed again after it isn't picked up by date, only if it has a failed row in the period.
- **"Every record changed" needs a Record Date.** The source IDO has to have the `RecordDate` property, which standard Syteline IDOs do.
- **It can't resend a delete that never failed.** If a delete was never replicated, for example because triggers were inactive, nothing in Syteline or the log says the record existed.
- **One resend looks at 10,000 failed rows at most.** The task's messages say when it reached that. Run it again for the rest.
- **Dates are read in these forms:** `2026-11-18 14:00:00`, `20261118 14:00:00`, or the server's own date format. If the task says a date couldn't be read, enter it in the first form.

## Running it without the form

Background task: `ue_AIR_ResendReplications` (see [Installation](Installation#4-background-tasks)).

| Parameter | Value |
| --- | --- |
| 1. Background task number | `BG~TASKID~` |
| 2. Rule number | The rule to resend for |
| 3. From | The start of the period |
| 4. To | The end of the period. Blank means now. |
| 5. Scope | `Failures` or `Everything` |
