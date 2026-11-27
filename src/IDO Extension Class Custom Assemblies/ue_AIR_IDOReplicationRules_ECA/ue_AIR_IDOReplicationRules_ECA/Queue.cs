using Mongoose.IDO;
using Mongoose.IDO.Protocol;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ue_AIR_IDOReplicationRules_ECA.Helpers;
using ue_AIR_IDOReplicationRules_ECA.Models;
using ue_AIR_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_AIR_IDOReplicationRules_ECA
{

    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /*
    /* The change queue: phase 1 of the AES plan (wiki page Replication-Redesign-AES-Plan).
    /*
    /* A rule with Use Queue on doesn't send in the user's save. Its triggers write one row per record to
    /* ue_AIR_IDOReplicationQueue instead, with the record's keys (and its RowPointer when the caller sent it).
    /* A delete also stores what the rule would send, because the record is gone by the time the sender runs.
    /* The rows are written inside the save, so they commit and roll back with it.
    /*
    /* ue_SendQueuedReplications, run as a background task, reads the queue with committed reads only, combines
    /* the rows for the same record, loads each record's current values by its keys, sends them through the same
    /* code as every other run, writes the replication log, and removes the rows it handled. A send that fails is
    /* logged as Failed with its payload, as today, so the retry task resends it.
    /*
    /* Rules without Use Queue still send in the save, as before.
    /*
    /**********************************************************************************************************/
    /**********************************************************************************************************/

    public partial class ue_AIR_IDOReplicationRules_ECA
    {

        // THE QUEUE'S IDO

        private const string QueueIDOName = "ue_AIR_IDOReplicationQueue";

        // WHAT SEPARATES THE KEYS IN THE KEY VALUES THE TRIGGERS PASS ("Name=Value|~|Name=Value"). IT MUST MATCH ReplicationKeySeparator IN
        // THE CONFIG ASSEMBLY.

        private const string KeyValueSeparator = "|~|";

        // THE SOURCE OF A LOG ROW WRITTEN BY THE SENDER, BY THE OPERATION IT SENT. THE VALUES MUST MATCH THE Source INLINE LIST ON
        // ue_AIR_IDOReplicationLogs.

        private const string SourceQueueAfterInsert = "Queue: After-Insert";

        private const string SourceQueueAfterUpdate = "Queue: After-Update";

        private const string SourceQueueBeforeDelete = "Queue: Before-Delete";

        // HOW MANY QUEUE ROWS ONE LOAD READS, HOW MANY LOADS ONE RUN OF THE SENDER MAKES AT MOST, AND HOW MANY RECORDS GO IN ONE FILTER
        // WHEN THE SENDER LOADS THEIR CURRENT VALUES

        private const int QueueBatchSize = 500;

        private const int QueueMaxBatchesPerRun = 20;

        private const int QueueRecordsPerLoad = 50;



        /**********************************************************************************************************/
        /*
        /* Capture, in the save
        /*
        /**********************************************************************************************************/

        // A TRIGGER RUN. THE RULES ARE LOADED ONCE. EVERY RULE WITH Use Queue ON GETS A QUEUE ROW; EVERY OTHER RULE IS SENT IN THE SAVE, AS
        // BEFORE. WHEN THE TRIGGER WASN'T GIVEN THE RECORD'S RowPointer BUT WAS GIVEN EVERY KEY, THE RECORD IS LOADED BY ITS KEYS INSTEAD,
        // FOR BOTH. NEVER THROWS: IT RUNS INSIDE THE USER'S SAVE.

        private void RunTriggerReplication(SytelineInternalAPI sytelineAPI, Utilities utils, string queryFilter, string IDOName, int recordCap, string ruleNum, string operation, string source, string keyValues)
        {

            Dictionary<string, ReplicationRule> replicationRules;
            Dictionary<string, string> keys;
            string recordFilter;

            try
            {

                keys = ParseKeyValues(keyValues);
                recordFilter = IsBlankRowPointerFilter(queryFilter) && HasEveryKey(keys) ? this.BuildKeyFilter(sytelineAPI, keys) : queryFilter;
                replicationRules = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum);

            }
            catch (Exception)
            {

                // THE RULES COULDN'T BE LOADED. RUN AS BEFORE, WHICH LOADS THEM AGAIN AND LOGS THE FAILURE IF IT HAPPENS AGAIN.

                this.RunReplication(sytelineAPI, utils, -1, queryFilter, IDOName, recordCap, ruleNum, operation, source);
                return;

            }

            Dictionary<string, ReplicationRule> queueRules = replicationRules.Where(rule => rule.Value.UseQueue).ToDictionary(rule => rule.Key, rule => rule.Value);
            Dictionary<string, ReplicationRule> inSaveRules = replicationRules.Where(rule => !rule.Value.UseQueue).ToDictionary(rule => rule.Key, rule => rule.Value);

            if (queueRules.Count > 0)
            {
                this.CaptureToQueue(sytelineAPI, utils, queryFilter, recordFilter, IDOName, recordCap, keys, operation, source, queueRules);
            }

            // WITH NO RULE AT ALL, THIS STILL RUNS, SO A TRIGGER ON AN IDO WITH NO ACTIVE RULE BEHAVES AS IT DID

            if (inSaveRules.Count > 0 || queueRules.Count == 0)
            {
                this.RunReplication(sytelineAPI, utils, -1, recordFilter, IDOName, recordCap, ruleNum, operation, source, inSaveRules);
            }

        }



        // WRITES ONE QUEUE ROW PER RULE FOR THE RECORD THE TRIGGER NAMED. NEVER THROWS.
        //
        // A DELETE ALSO LOADS THE RECORD NOW, WHILE IT STILL EXISTS, AND STORES WHAT EACH RULE WOULD SEND IN THE ROW'S PAYLOAD. A PAYLOAD
        // TOO LONG FOR THE COLUMN IS SENT IN THE SAVE INSTEAD, AS BEFORE. WHEN THE QUEUE ROWS CAN'T BE WRITTEN, THE RULES ARE SENT IN THE
        // SAVE INSTEAD, SO THE CHANGE ISN'T LOST.

        private void CaptureToQueue(SytelineInternalAPI sytelineAPI, Utilities utils, string queryFilter, string recordFilter, string IDOName, int recordCap, Dictionary<string, string> keys, string operation, string source, Dictionary<string, ReplicationRule> queueRules)
        {

            List<ReplicationRuleResult> ruleResults = new List<ReplicationRuleResult>();

            try
            {

                string recordRowPointer = RowPointerFromFilter(queryFilter);
                string keyJson = HasEveryKey(keys) ? JsonConvert.SerializeObject(keys) : null;

                // A CALL WITH SOME OTHER FILTER, SUCH AS AN EVENT HANDLER MADE BY HAND THAT NAMES RECORDS BY A PARENT'S KEY, ISN'T ONE
                // RECORD THE QUEUE CAN NAME. IT IS SENT IN THE SAVE, AS IT WOULD BE WITHOUT THE QUEUE.

                if (recordRowPointer == null && keyJson == null && !string.IsNullOrWhiteSpace(queryFilter) && !IsBlankRowPointerFilter(queryFilter))
                {
                    this.RunReplication(sytelineAPI, utils, -1, queryFilter, IDOName, recordCap, null, operation, source, queueRules);
                    return;
                }

                // A RECORD WITH NEITHER A RowPointer NOR EVERY KEY CAN'T BE FOUND AGAIN. EACH RULE GETS THE SAME Skipped ROW AS A TRIGGER
                // WHOSE FILTER MATCHED NOTHING, SO THE LOG SHOWS WHAT THE TRIGGER WAS GIVEN.

                if (recordRowPointer == null && keyJson == null)
                {

                    foreach (ReplicationRule replicationRule in queueRules.Values)
                    {
                        ruleResults.Add(BuildNoRecordRuleResult(replicationRule, IDOName, queryFilter, operation));
                    }

                    this.WriteReplicationLog(sytelineAPI, utils, IDOName, operation, source, ruleResults);
                    return;

                }

                List<IDOUpdateItem> queueRows = new List<IDOUpdateItem>();
                List<ReplicationRule> queuedRules = new List<ReplicationRule>();
                Dictionary<string, ReplicationRule> sendNowRules = new Dictionary<string, ReplicationRule>();

                if (operation == OperationDelete)
                {

                    // A RULE WHOSE RECORDS COULDN'T BE LOADED ISN'T IN recordsByRule, AND ALREADY HAS A FAILED RESULT IN ruleResults

                    Dictionary<string, LoadRecordsResponseData> recordsByRule = this.LoadSourceRecords(sytelineAPI, IDOName, recordFilter, 1, queueRules, operation, ruleResults);

                    foreach (ReplicationRule replicationRule in queueRules.Values)
                    {

                        LoadRecordsResponseData recordsResponse;

                        if (!recordsByRule.TryGetValue(replicationRule.RuleNum, out recordsResponse))
                        {
                            continue;
                        }

                        if (recordsResponse.Items.Count == 0)
                        {
                            ruleResults.Add(BuildNoRecordRuleResult(replicationRule, IDOName, recordFilter, operation));
                            continue;
                        }

                        Dictionary<Dictionary<string, object>, string> rowPointers = new Dictionary<Dictionary<string, object>, string>();
                        Dictionary<string, object> remappedRecord = this.RemapRecords(replicationRule.MapFieldsList, recordsResponse, null, rowPointers).First();
                        string payload = JsonConvert.SerializeObject(remappedRecord);
                        string loadedRowPointer;

                        if (payload.Length > MaxPayloadLength)
                        {
                            sendNowRules[replicationRule.RuleNum] = replicationRule;
                            continue;
                        }

                        queueRows.Add(this.BuildQueueRow(sytelineAPI, IDOName, replicationRule.RuleNum, operation, source, keyJson,
                            rowPointers.TryGetValue(remappedRecord, out loadedRowPointer) && loadedRowPointer != "" ? loadedRowPointer : recordRowPointer, payload));
                        queuedRules.Add(replicationRule);

                    }

                }
                else
                {

                    foreach (ReplicationRule replicationRule in queueRules.Values)
                    {
                        queueRows.Add(this.BuildQueueRow(sytelineAPI, IDOName, replicationRule.RuleNum, operation, source, keyJson, recordRowPointer, null));
                        queuedRules.Add(replicationRule);
                    }

                }

                if (queueRows.Count > 0)
                {

                    try
                    {
                        if (sytelineAPI.CreateRecords(QueueIDOName, queueRows) == null)
                        {
                            throw new Exception("Syteline didn't confirm the queue rows were saved.");
                        }
                    }
                    catch (Exception ex)
                    {

                        // THE CHANGE COULDN'T BE QUEUED. SEND IT IN THE SAVE INSTEAD, AS A RULE WITHOUT THE QUEUE WOULD.

                        utils.WriteLogMessage("Couldn't queue the change to '" + IDOName + "', so it was sent in the save: " + this.DescribeException(ex));

                        foreach (ReplicationRule queuedRule in queuedRules)
                        {
                            sendNowRules[queuedRule.RuleNum] = queuedRule;
                        }

                    }

                }

                if (sendNowRules.Count > 0)
                {
                    this.RunReplication(sytelineAPI, utils, -1, recordFilter, IDOName, recordCap, null, operation, source, sendNowRules);
                }

            }
            catch (Exception ex)
            {

                // SOMETHING UNEXPECTED. EVERY QUEUE RULE WITHOUT A RESULT GETS A FAILED ROW, WITHOUT A PAYLOAD, SO THE CHANGE IS VISIBLE IN
                // THE LOG. A BULK RESYNC CAN SEND IT LATER.

                string errorMessage = "Couldn't queue the change to '" + IDOName + "': " + this.DescribeException(ex);

                foreach (ReplicationRule replicationRule in queueRules.Values.Where(rule => !ruleResults.Any(ruleResult => ruleResult.RuleNum == rule.RuleNum)))
                {
                    ruleResults.Add(BuildFailedRuleResult(replicationRule, errorMessage, operation));
                }

            }

            if (ruleResults.Count > 0)
            {
                this.WriteReplicationLog(sytelineAPI, utils, IDOName, operation, source, ruleResults);
            }

        }



        // ONE QUEUE ROW. RecordRowPointer IS WRITTEN AS AN EXPLICIT NULL WHEN THERE IS NONE, BECAUSE A RowPointerType COLUMN LEFT OUT OF AN
        // INSERT GETS A NEW RANDOM GUID (SEE SetRecordRowPointer).

        private IDOUpdateItem BuildQueueRow(SytelineInternalAPI sytelineAPI, string IDOName, string ruleNum, string operation, string source, string keyJson, string recordRowPointer, string payload)
        {

            int ruleNumber;

            IDOUpdateItem queueRow = sytelineAPI.BuildInsertItem(new Dictionary<string, object>()
            {
                { "IDOName", IDOName },
                { "RuleNum", int.TryParse(ruleNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out ruleNumber) ? (object)ruleNumber : null },
                { "Operation", operation },
                { "Source", source },
                { "KeyValues", keyJson },
                { "Payload", payload },
                { "AttemptCount", 0 }
            });

            SetRecordRowPointer(queueRow, recordRowPointer);

            return queueRow;

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_SendQueuedReplications
        /* Purpose:  Background task method: the queue's sender. Reads the queue (committed rows only), combines
        /*           the rows for each record, sends each record's current values, or for a delete what was
        /*           stored when it was deleted, through each rule, writes the replication log, and removes each
        /*           part's rows as soon as it was sent and logged. Rows it couldn't handle are kept for the next
        /*           run and counted; after QueueMaxAttempts runs they are logged as Failed and removed. A rule
        /*           whose rows were kept is left alone for the rest of the run, so it can't hold up the others.
        /*           Run it on a schedule, or by hand, with Max Concurrent 1 on its Background Task Definition so
        /*           two never run at once. Returns 16 when rows were kept or couldn't be removed, or the queue
        /*           couldn't be read. Never throws.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        // HOW MANY RUNS A QUEUE ROW THE SENDER CAN'T HANDLE IS KEPT FOR, BEFORE IT IS LOGGED AS Failed AND REMOVED. ONLY A PROBLEM ON THE
        // SYTELINE SIDE KEEPS A ROW (A FAILED SEND IS LOGGED WITH ITS PAYLOAD FOR THE RETRY TASK INSTEAD), SO THIS IS ABOUT AN HOUR AT ONE RUN
        // A MINUTE: LONG ENOUGH TO RIDE OUT A SHORT OUTAGE, SHORT ENOUGH THAT A RULE THAT CAN'T WORK DOESN'T STAY QUEUED FOREVER.

        private const int QueueMaxAttempts = 60;

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_SendQueuedReplications(int BGTaskNum = -1)
        {

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands,
                BGTaskNum: BGTaskNum
            );

            int rowsHandled = 0;
            int rowsKept = 0;

            // THE IDO AND RULE PAIRS WHOSE ROWS WERE KEPT IN THIS RUN. LATER LOADS LEAVE THEM OUT, SO ONE RULE THAT KEEPS FAILING CAN'T FILL EVERY
            // LOAD WITH ITS OWN ROWS AND STOP THE OTHER RULES FROM BEING SENT.

            List<KeyValuePair<string, string>> setAside = new List<KeyValuePair<string, string>>();

            try
            {

                for (int batch = 0; batch < QueueMaxBatchesPerRun; batch++)
                {

                    List<QueueRow> queueRows = this.LoadQueueRows(sytelineAPI, setAside);

                    if (queueRows.Count == 0)
                    {
                        break;
                    }

                    foreach (IGrouping<string, QueueRow> ruleGroup in queueRows.GroupBy(row => row.IDOName + "\u0001" + row.RuleNum))
                    {

                        List<QueueRow> rows = ruleGroup.ToList();
                        List<QueueRow> kept = this.SendQueuedRule(sytelineAPI, utils, BGTaskNum, rows[0].IDOName, rows[0].RuleNum, rows);

                        rowsHandled += rows.Count - kept.Count;

                        if (kept.Count > 0)
                        {
                            rowsKept += kept.Count;
                            setAside.Add(new KeyValuePair<string, string>(rows[0].IDOName, rows[0].RuleNum));
                            this.CountQueueAttempts(sytelineAPI, utils, rows[0].IDOName, rows[0].RuleNum, kept);
                        }

                    }

                    if (queueRows.Count < QueueBatchSize)
                    {
                        break;
                    }

                }

            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("The queue's sender stopped after " + rowsHandled + " row(s): " + this.DescribeException(ex));
                return BackgroundTaskFailedReturnCode;
            }

            utils.WriteLogMessage("The queue's sender handled " + rowsHandled + " row(s)" + (rowsKept > 0 ? " and kept " + rowsKept + " for the next run, because they couldn't all be sent and logged. The task's messages and the replication log say why." : "."));
            return rowsKept > 0 ? BackgroundTaskFailedReturnCode : 0;

        }



        // ONE ROW OF THE QUEUE, AS THE SENDER READS IT

        private class QueueRow
        {
            public string ItemID;
            public string IDOName;
            public string RuleNum;
            public string Operation;
            public string KeyValues;
            public string RecordRowPointer;
            public string Payload;
            public int AttemptCount;

            // THE RECORD THE ROW IS FOR: ITS KEYS WHEN IT HAS THEM (NAMES IN ORDER, VALUES TRIMMED, WITHOUT REGARD TO CASE), ELSE ITS
            // RowPointer. A RECORD CAPTURED ONCE BY ITS KEYS AND ONCE BY ITS RowPointer ALONE STILL COUNTS AS TWO.
            public string RecordKey
            {
                get
                {
                    Dictionary<string, string> keys = ParseKeyJson(this.KeyValues);
                    return keys != null
                        ? "K:" + string.Join("\u0001", keys.OrderBy(key => key.Key, StringComparer.OrdinalIgnoreCase).Select(key => key.Key.ToLowerInvariant() + "=" + (key.Value ?? "").Trim().ToLowerInvariant()))
                        : "R:" + (this.RecordRowPointer ?? "").Trim().ToLowerInvariant();
                }
            }
        }



        // ONE RECORD'S QUEUED ROWS, AND WHAT IS SENT FOR THEM: A DELETE, AN INSERT, UPDATE OR UPSERT, BOTH, OR NOTHING. ITS ROWS ARE REMOVED ONCE
        // EVERY PART IS SENT AND LOGGED.

        private class QueuedRecord
        {
            public List<QueueRow> Rows;
            public QueueRow Delete;
            public QueueRow Upsert;
            public string UpsertOperation;
            public int PartsLeft;
            public bool Failed;
        }



        // THE OLDEST QUEUE ROWS, UP TO QueueBatchSize, LEAVING OUT THE IDO AND RULE PAIRS SET ASIDE IN THIS RUN. LoadRecords ASKS FOR
        // ReadCommitted, SO A SAVE THAT IS STILL RUNNING IS SKIPPED, NOT READ BEFORE IT COMMITS (TESTS F3 AND F3b ON THE WIKI PAGE
        // Trigger-Diagnostics).

        private List<QueueRow> LoadQueueRows(SytelineInternalAPI sytelineAPI, List<KeyValuePair<string, string>> setAside)
        {

            string filter = setAside.Count == 0
                ? ""
                : "NOT ( " + string.Join(" OR ", setAside.Select(pair => "( IDOName = " + sytelineAPI.QuoteFilterValue(pair.Key) + " AND RuleNum = " + sytelineAPI.QuoteFilterValue(pair.Value) + " )")) + " )";

            LoadRecordsResponseData response = sytelineAPI.LoadRecords(
                IDOName: QueueIDOName,
                properties: new List<string>() { "IDOName", "RuleNum", "Operation", "KeyValues", "RecordRowPointer", "Payload", "AttemptCount", "CreateDate", "RowPointer" },
                filter: filter,
                orderBy: "CreateDate ASC, RowPointer ASC",
                recordCap: QueueBatchSize
            );

            Func<IDOItem, string, string> read = (item, name) => item.PropertyValues[response.PropertyKeys[name]].Value;
            int attempts;

            return response.Items.Select(item => new QueueRow()
            {
                ItemID = item.ItemID,
                IDOName = (read(item, "IDOName") ?? "").Trim(),
                RuleNum = (read(item, "RuleNum") ?? "").Trim(),
                Operation = NormalizeOperation(read(item, "Operation")),
                KeyValues = read(item, "KeyValues"),
                RecordRowPointer = read(item, "RecordRowPointer"),
                Payload = read(item, "Payload"),
                AttemptCount = int.TryParse(read(item, "AttemptCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out attempts) ? attempts : 0
            }).ToList();

        }



        // REMOVES QUEUE ROWS THAT WERE SENT AND LOGGED. THROWS WHEN SYTELINE DOESN'T CONFIRM IT, WHICH STOPS THE RUN: THE NEXT RUN SENDS THEM
        // AGAIN.

        private void RemoveQueueRows(SytelineInternalAPI sytelineAPI, List<QueueRow> rows)
        {

            if (rows.Count == 0)
            {
                return;
            }

            if (sytelineAPI.DeleteRecords(QueueIDOName, rows.Select(row => sytelineAPI.BuildDeleteItem(row.ItemID)).ToList()) == null)
            {
                throw new Exception("Syteline didn't confirm that " + rows.Count + " queue row(s) for rule '" + rows[0].RuleNum + "' were removed.");
            }

        }



        // A PART (THE DELETES, OR ONE BATCH OF INSERTS OR UPDATES) WAS SENT AND LOGGED, OR COULDN'T BE. A RECORD WHOSE LAST PART WAS HANDLED HAS
        // ITS ROWS REMOVED AT ONCE, SO A LATER PART THAT FAILS DOESN'T SEND IT AGAIN. THROWS WHEN THE ROWS CAN'T BE REMOVED.

        private void FinishQueuedPart(SytelineInternalAPI sytelineAPI, IEnumerable<QueuedRecord> records, bool handled)
        {

            List<QueueRow> removable = new List<QueueRow>();

            foreach (QueuedRecord record in records)
            {

                if (!handled)
                {
                    record.Failed = true;
                    continue;
                }

                record.PartsLeft--;

                if (record.PartsLeft <= 0 && !record.Failed)
                {
                    removable.AddRange(record.Rows);
                }

            }

            this.RemoveQueueRows(sytelineAPI, removable);

        }



        // SENDS ONE RULE'S QUEUED RECORDS. THE ROWS ARE IN THE ORDER THEY WERE QUEUED. EACH RECORD'S ROWS ARE COMBINED, SO THE TARGET IS TOLD
        // WHAT HAPPENED OVERALL:
        //
        //   ROWS (OLDEST FIRST)                          SENT AS
        //   FIRST AN Insert, LAST NOT A Delete           Insert (ANY DELETE IN BETWEEN IS NEVER SENT: THE TARGET NEVER HAD IT)
        //   FIRST AN Insert, LAST A Delete               NOTHING (LOGGED AS Skipped)
        //   FIRST NOT AN Insert, LAST A Delete           Delete
        //   FIRST NOT AN Insert, A Delete, THEN MORE     Delete, THEN Insert (THE RECORD WAS DELETED AND CREATED AGAIN)
        //   Updates only                                 Update
        //   Updates and at least one Upsert              Upsert (A TRIGGER THAT PASSED NO OPERATION)
        //
        // DELETES GO FIRST, THEN INSERTS, UPDATES AND UPSERTS. EACH PART'S ROWS ARE REMOVED ONCE IT IS SENT AND LOGGED. RETURNS THE ROWS
        // THAT MUST BE KEPT FOR THE NEXT RUN. THROWS ONLY WHEN ROWS CAN'T BE REMOVED.

        private List<QueueRow> SendQueuedRule(SytelineInternalAPI sytelineAPI, Utilities utils, int BGTaskNum, string IDOName, string ruleNum, List<QueueRow> rows)
        {

            ReplicationRule replicationRule = null;

            try
            {
                replicationRule = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum).Values.FirstOrDefault();
            }
            catch (Exception ex)
            {

                // THE RULE COULDN'T BE LOADED, WHICH ISN'T THE SAME AS A RULE THAT IS GONE. ITS ROWS ARE KEPT FOR THE NEXT RUN.

                utils.WriteLogMessage("Couldn't load rule '" + ruleNum + "' for IDO '" + IDOName + "', so its " + rows.Count + " queued row(s) were kept: " + this.DescribeException(ex));
                return rows;

            }

            // A RULE THAT IS INACTIVE, GONE, OR NOW FOR ANOTHER IDO SENDS NOTHING. EACH RECORD GETS A Skipped ROW, AND THE ROWS ARE REMOVED, SO
            // A RULE SWITCHED OFF DOESN'T LEAVE ITS ROWS BEHIND. A BULK RESYNC CAN SEND THEM WHEN IT IS SWITCHED BACK ON.

            if (replicationRule == null)
            {

                List<QueueRow> keptRows = new List<QueueRow>();

                foreach (IGrouping<string, QueueRow> operationRows in rows.GroupBy(row => row.Operation))
                {

                    ReplicationRuleResult skipped = new ReplicationRuleResult(ruleNum, "", 0, true, "", operationRows.Key);

                    foreach (IGrouping<string, QueueRow> recordRows in operationRows.GroupBy(row => row.RecordKey))
                    {
                        skipped.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(recordRows.First()), null, ReplicationRecordOutcome.StatusSkipped, "Nothing was sent: rule '" + ruleNum + "' isn't active for IDO '" + IDOName + "' any more, or was deleted."));
                    }

                    if (this.WriteReplicationLog(sytelineAPI, utils, IDOName, operationRows.Key, QueueSource(operationRows.Key), new List<ReplicationRuleResult>() { skipped }))
                    {
                        this.RemoveQueueRows(sytelineAPI, operationRows.ToList());
                    }
                    else
                    {
                        keptRows.AddRange(operationRows);
                    }

                }

                return keptRows;

            }

            // COMBINE EACH RECORD'S ROWS

            List<QueuedRecord> records = new List<QueuedRecord>();
            List<QueuedRecord> nothingToSendRecords = new List<QueuedRecord>();
            ReplicationRuleResult nothingToSend = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, 0, true, "", OperationDelete)
            {
                CredentialName = replicationRule.CredentialName ?? ""
            };

            foreach (IGrouping<string, QueueRow> recordGroup in rows.GroupBy(row => row.RecordKey))
            {

                QueuedRecord record = new QueuedRecord() { Rows = recordGroup.ToList() };
                QueueRow first = record.Rows.First();
                QueueRow last = record.Rows.Last();
                QueueRow lastDelete = record.Rows.LastOrDefault(row => row.Operation == OperationDelete);

                if (first.Operation == OperationInsert)
                {

                    // THE TARGET NEVER HAD THE RECORD, SO A DELETE IN BETWEEN IS NEVER SENT

                    if (last.Operation == OperationDelete)
                    {
                        nothingToSend.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(first), null, ReplicationRecordOutcome.StatusSkipped, "Nothing was sent: the record was inserted and deleted before it was sent."));
                        nothingToSendRecords.Add(record);
                        continue;
                    }

                    record.Upsert = last;
                    record.UpsertOperation = OperationInsert;

                }
                else if (last.Operation == OperationDelete)
                {
                    record.Delete = lastDelete;
                }
                else if (lastDelete != null)
                {
                    record.Delete = lastDelete;
                    record.Upsert = last;
                    record.UpsertOperation = OperationInsert;
                }
                else
                {
                    record.Upsert = last;
                    record.UpsertOperation = record.Rows.Any(row => row.Operation == OperationUpsert) ? OperationUpsert : OperationUpdate;
                }

                record.PartsLeft = (record.Delete != null ? 1 : 0) + (record.Upsert != null ? 1 : 0);
                records.Add(record);

            }

            if (nothingToSendRecords.Count > 0)
            {
                bool written = this.WriteReplicationLog(sytelineAPI, utils, IDOName, OperationDelete, SourceQueueBeforeDelete, new List<ReplicationRuleResult>() { nothingToSend });
                nothingToSendRecords.ForEach(record => record.PartsLeft = 1);
                this.FinishQueuedPart(sytelineAPI, nothingToSendRecords, written);
            }

            // DELETES FIRST

            List<QueuedRecord> deletes = records.Where(record => record.Delete != null).ToList();

            if (deletes.Count > 0)
            {

                bool deletesHandled = this.SendQueuedDeletes(sytelineAPI, utils, IDOName, replicationRule, deletes.Select(record => record.Delete).ToList());

                // A RECORD THAT WAS DELETED AND CREATED AGAIN: ONCE ITS DELETE IS SENT, THE ROWS UP TO THAT DELETE ARE REMOVED, SO IF ITS INSERT
                // FAILS ONLY THE LATER ROWS ARE KEPT, AND THE NEXT RUN SENDS THEM AS AN INSERT WITHOUT SENDING THE DELETE AGAIN. IF THE DELETE
                // FAILED, THE INSERT ISN'T SENT EITHER, SO THE TWO CAN'T REACH THE TARGET IN THE WRONG ORDER.

                foreach (QueuedRecord record in deletes.Where(record => record.Upsert != null))
                {

                    if (!deletesHandled)
                    {
                        record.Upsert = null;
                        continue;
                    }

                    int deleteIndex = record.Rows.IndexOf(record.Delete);
                    this.RemoveQueueRows(sytelineAPI, record.Rows.Take(deleteIndex + 1).ToList());
                    record.Rows = record.Rows.Skip(deleteIndex + 1).ToList();
                    record.PartsLeft--;

                }

                this.FinishQueuedPart(sytelineAPI, deletes.Where(record => record.Upsert == null), deletesHandled);

            }

            // THEN INSERTS, UPDATES AND UPSERTS, QueueRecordsPerLoad AT A TIME. A BATCH THAT FAILS KEEPS ONLY ITS OWN RECORDS.

            foreach (string operation in new[] { OperationInsert, OperationUpdate, OperationUpsert })
            {

                List<QueuedRecord> upserts = records.Where(record => record.Upsert != null && record.UpsertOperation == operation).ToList();

                for (int i = 0; i < upserts.Count; i += QueueRecordsPerLoad)
                {

                    List<QueuedRecord> batch = upserts.Skip(i).Take(QueueRecordsPerLoad).ToList();

                    if (this.SendQueuedUpsertBatch(sytelineAPI, utils, BGTaskNum, IDOName, replicationRule, batch.Select(record => record.Upsert).ToList(), operation, QueueSource(operation)))
                    {
                        this.FinishQueuedPart(sytelineAPI, batch, true);
                        continue;
                    }

                    // A BATCH THAT FAILED IS TRIED AGAIN ONE RECORD AT A TIME, SO ONE RECORD THAT CAN'T BE LOADED (A KEY VALUE THE FILTER CAN'T
                    // CONVERT, SAY) KEEPS ONLY ITSELF. A RECORD THE FAILED BATCH ALREADY SENT IS SENT AGAIN.

                    foreach (QueuedRecord record in batch)
                    {
                        this.FinishQueuedPart(sytelineAPI, new[] { record }, batch.Count > 1 && this.SendQueuedUpsertBatch(sytelineAPI, utils, BGTaskNum, IDOName, replicationRule, new List<QueueRow>() { record.Upsert }, operation, QueueSource(operation)));
                    }

                }

            }

            return records.Concat(nothingToSendRecords).Where(record => record.Failed).SelectMany(record => record.Rows).ToList();

        }



        // THE SOURCE OF THE LOG ROW FOR A QUEUED OPERATION

        private static string QueueSource(string operation)
        {

            switch (operation)
            {
                case OperationInsert:
                    return SourceQueueAfterInsert;
                case OperationDelete:
                    return SourceQueueBeforeDelete;
                default:
                    return SourceQueueAfterUpdate;
            }

        }



        // COUNTS A RUN THAT KEPT THESE ROWS. A ROW KEPT QueueMaxAttempts TIMES IS LOGGED AS Failed, WITHOUT A PAYLOAD SO THE RETRY TASK LEAVES IT
        // ALONE, AND REMOVED, SO IT ISN'T TRIED FOREVER. NEVER THROWS.

        private void CountQueueAttempts(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, string ruleNum, List<QueueRow> kept)
        {

            try
            {

                List<QueueRow> givenUp = kept.Where(row => row.AttemptCount + 1 >= QueueMaxAttempts).ToList();
                List<QueueRow> stillQueued = kept.Where(row => row.AttemptCount + 1 < QueueMaxAttempts).ToList();

                if (stillQueued.Count > 0 && sytelineAPI.UpdateRecords(QueueIDOName, stillQueued.Select(row => sytelineAPI.BuildUpdateItem(row.ItemID, new List<IDOUpdateProperty>()
                    {
                        new IDOUpdateProperty("AttemptCount", (row.AttemptCount + 1).ToString(CultureInfo.InvariantCulture), true)
                    })).ToList()) == null)
                {
                    utils.WriteLogMessage("Syteline didn't confirm the new attempt counts of " + stillQueued.Count + " kept queue row(s) for rule '" + ruleNum + "'.");
                }

                foreach (IGrouping<string, QueueRow> operationRows in givenUp.GroupBy(row => row.Operation))
                {

                    string message = "The queue's sender couldn't handle this in " + QueueMaxAttempts + " runs, so it was taken off the queue. The task's messages from those runs say why."
                        + (operationRows.Key == OperationDelete ? " The delete wasn't sent; delete the record at the target by hand if it should be gone." : " A Bulk Resync or Resend Errors can send the record's current values.");
                    ReplicationRuleResult failed = new ReplicationRuleResult(ruleNum, "", operationRows.Count(), false, message, operationRows.Key);

                    foreach (QueueRow row in operationRows)
                    {
                        // NO PAYLOAD, SO THE RETRY TASK DOESN'T SEND AN OLD DELETE LATER, OVER A NEWER INSERT OF THE SAME RECORD

                        failed.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(row), null, ReplicationRecordOutcome.StatusFailed, message)
                        {
                            RecordRowPointer = row.RecordRowPointer ?? ""
                        });
                    }

                    if (this.WriteReplicationLog(sytelineAPI, utils, IDOName, operationRows.Key, QueueSource(operationRows.Key), new List<ReplicationRuleResult>() { failed }))
                    {
                        this.RemoveQueueRows(sytelineAPI, operationRows.ToList());
                    }

                }

            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Couldn't count the attempts of rule '" + ruleNum + "''s kept queue rows: " + this.DescribeException(ex));
            }

        }



        // SENDS THE QUEUED DELETES FROM THE PAYLOADS STORED WHEN THE RECORDS WERE DELETED, AS THE RETRY TASK SENDS A STORED PAYLOAD. A SEND
        // THAT FAILS IS LOGGED AS Failed WITH ITS PAYLOAD, FOR THE RETRY TASK. RETURNS FALSE WHEN THE LOG COULDN'T BE WRITTEN. NEVER THROWS.

        private bool SendQueuedDeletes(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, ReplicationRule replicationRule, List<QueueRow> deletes)
        {

            if (deletes.Count == 0)
            {
                return true;
            }

            ReplicationRuleResult ruleResult = null;
            List<Dictionary<string, object>> records = new List<Dictionary<string, object>>();
            List<ReplicationRecordOutcome> unreadable = new List<ReplicationRecordOutcome>();

            // BuildOutcome READS THESE: THE RowPointer OF EACH RECORD FOR ITS LOG ROW, AND NO RECORDS AWAITING RETRY, BECAUSE EVERY RECORD IS LOGGED

            this.recordRowPointersByRecord = new Dictionary<Dictionary<string, object>, string>();
            this.recordIDsAwaitingRetry = new Dictionary<string, HashSet<string>>();

            foreach (QueueRow delete in deletes)
            {

                Dictionary<string, object> record = null;

                try
                {
                    record = string.IsNullOrEmpty(delete.Payload) ? null : JsonConvert.DeserializeObject<Dictionary<string, object>>(delete.Payload);
                }
                catch (JsonException)
                {
                    record = null;
                }

                if (record == null)
                {
                    unreadable.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(delete), null, ReplicationRecordOutcome.StatusFailed, "The delete was queued without a payload it could send, so nothing was sent."));
                    continue;
                }

                this.recordRowPointersByRecord[record] = delete.RecordRowPointer ?? "";
                records.Add(record);

            }

            string operation = OperationAtTarget(replicationRule.TargetType, OperationDelete);

            try
            {

                if (records.Count > 0)
                {

                    try
                    {
                        ruleResult = this.SendRule(replicationRule, records, operation, true, utils);
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = this.DescribeException(ex);
                        ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, records.Count, false, errorMessage);
                        ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, records, ReplicationRecordOutcome.StatusFailed, errorMessage, true);
                    }

                }
                else
                {
                    ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, 0, true, "");
                }

                ruleResult.RecordOutcomes.AddRange(unreadable);
                ruleResult.Operation = operation;
                ruleResult.CredentialName = replicationRule.CredentialName ?? "";

            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Couldn't send the queued deletes of rule '" + replicationRule.RuleNum + "' for IDO '" + IDOName + "', so they were kept: " + this.DescribeException(ex));
                return false;
            }

            return this.WriteReplicationLog(sytelineAPI, utils, IDOName, OperationDelete, SourceQueueBeforeDelete, new List<ReplicationRuleResult>() { ruleResult });

        }



        // SENDS ONE BATCH OF QUEUED INSERTS, UPDATES OR UPSERTS. THE BATCH IS FIRST LOADED BY ITS KEYS (OR RowPointer), SO A RECORD DELETED
        // SINCE IT WAS QUEUED GETS A Skipped ROW, AND THEN SENT THROUGH RunReplication WITH THE SAME FILTER, WHICH LOADS THE RECORDS' CURRENT
        // VALUES, SENDS THEM AND LOGS THEM AS EVERY OTHER RUN DOES (A FAILED SEND WITH ITS PAYLOAD, FOR THE RETRY TASK).
        //
        // RETURNS FALSE WHEN THE BATCH COULDN'T BE LOADED OR LOGGED, SO ITS ROWS ARE KEPT: A LOAD THAT FAILS LEAVES ONE Failed ROW FOR THE RULE,
        // WITHOUT THE RECORDS OR THEIR PAYLOADS. NEVER THROWS.

        private bool SendQueuedUpsertBatch(SytelineInternalAPI sytelineAPI, Utilities utils, int BGTaskNum, string IDOName, ReplicationRule replicationRule, List<QueueRow> batch, string operation, string source)
        {

            try
            {

                List<string> recordFilters = batch.Select(row => this.BuildQueuedRecordFilter(sytelineAPI, row)).ToList();
                List<QueueRow> notFound = this.FindMissingRecords(sytelineAPI, IDOName, batch);

                if (notFound.Count > 0)
                {

                    ReplicationRuleResult missing = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, 0, true, "", OperationAtTarget(replicationRule.TargetType, operation))
                    {
                        CredentialName = replicationRule.CredentialName ?? ""
                    };

                    foreach (QueueRow row in notFound)
                    {
                        missing.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(row), null, ReplicationRecordOutcome.StatusSkipped, "Nothing was sent: the record no longer exists in Syteline."));
                    }

                    if (!this.WriteReplicationLog(sytelineAPI, utils, IDOName, operation, source, new List<ReplicationRuleResult>() { missing }))
                    {
                        return false;
                    }

                }

                if (notFound.Count < batch.Count)
                {

                    string filter = string.Join(" OR ", recordFilters.Select(recordFilter => "( " + recordFilter + " )"));
                    ReplicationRunResult runResult = this.RunReplication(sytelineAPI, utils, BGTaskNum, filter, IDOName, SytelineInternalAPI.NoRecordCap, replicationRule.RuleNum, operation, source, new Dictionary<string, ReplicationRule>() { { replicationRule.RuleNum, replicationRule } });

                    // A RESULT THAT FAILED WITHOUT ANY RECORD IN IT IS A LOAD OR SETUP FAILURE, NOT A SEND THAT FAILED: NOTHING WAS SENT, AND THE
                    // LOG HAS NO PAYLOAD TO RETRY

                    if (!runResult.LogWritten || runResult.RuleResults.Any(ruleResult => !ruleResult.Success && ruleResult.RecordOutcomes.Count == 0))
                    {
                        return false;
                    }

                }

                return true;

            }
            catch (Exception ex)
            {

                utils.WriteLogMessage("Couldn't send queued records of rule '" + replicationRule.RuleNum + "' for IDO '" + IDOName + "', so they were kept: " + this.DescribeException(ex));
                return false;

            }

        }



        // THE ROWS IN A BATCH WHOSE RECORD NO LONGER EXISTS. THE BATCH IS LOADED BY ITS KEYS (OR RowPointer), WITH THE KEYS ONLY, AND RowPointer
        // TOO WHEN A ROW HAS NO KEYS (SO AN IDO WITHOUT A RowPointer PROPERTY, WHOSE ROWS ALWAYS HAVE KEYS, ISN'T ASKED FOR ONE). EACH ROW IS
        // MATCHED TO A LOADED RECORD BY ITS KEY VALUES (TRIMMED, WITHOUT REGARD TO CASE, AND AS NUMBERS WHEN BOTH ARE NUMBERS) OR ITS RowPointer.

        private List<QueueRow> FindMissingRecords(SytelineInternalAPI sytelineAPI, string IDOName, List<QueueRow> batch)
        {

            List<string> keyNames = batch
                .Select(row => ParseKeyJson(row.KeyValues))
                .Where(keys => keys != null)
                .SelectMany(keys => keys.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool needsRowPointer = batch.Any(row => !HasEveryKey(ParseKeyJson(row.KeyValues)));
            string filter = string.Join(" OR ", batch.Select(row => "( " + this.BuildQueuedRecordFilter(sytelineAPI, row) + " )"));

            LoadRecordsResponseData loaded = sytelineAPI.LoadRecords(
                IDOName: IDOName,
                properties: needsRowPointer ? WithRowPointer(keyNames) : keyNames,
                filter: filter,
                orderBy: "",
                recordCap: SytelineInternalAPI.NoRecordCap
            );

            int rowPointerIndex = needsRowPointer ? RowPointerIndex(loaded) : -1;
            HashSet<string> loadedRowPointers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Dictionary<string, string>> loadedKeys = new List<Dictionary<string, string>>();

            foreach (IDOItem item in loaded.Items)
            {

                if (rowPointerIndex >= 0)
                {
                    loadedRowPointers.Add(item.PropertyValues[rowPointerIndex].Value ?? "");
                }

                loadedKeys.Add(keyNames.ToDictionary(name => name, name => item.PropertyValues[loaded.PropertyKeys[name]].Value ?? "", StringComparer.OrdinalIgnoreCase));

            }

            return batch.Where(row =>
            {

                Dictionary<string, string> keys = ParseKeyJson(row.KeyValues);

                if (HasEveryKey(keys))
                {
                    return !loadedKeys.Any(loadedKey => keys.All(key => loadedKey.ContainsKey(key.Key) && SameKeyValue(loadedKey[key.Key], key.Value)));
                }

                return string.IsNullOrEmpty(row.RecordRowPointer) || !loadedRowPointers.Contains(row.RecordRowPointer);

            }).ToList();

        }



        // THE FILTER THAT FINDS ONE QUEUED RECORD: ITS KEYS WHEN IT HAS THEM, ELSE ITS RowPointer

        private string BuildQueuedRecordFilter(SytelineInternalAPI sytelineAPI, QueueRow row)
        {

            Dictionary<string, string> keys = ParseKeyJson(row.KeyValues);

            if (keys != null && HasEveryKey(keys))
            {
                return this.BuildKeyFilter(sytelineAPI, keys);
            }

            Guid rowPointer;

            if (Guid.TryParse(row.RecordRowPointer ?? "", out rowPointer))
            {
                return RowPointerPropertyName + " = " + sytelineAPI.QuoteFilterValue(rowPointer.ToString());
            }

            throw new Exception("A queued row has neither the record's keys nor its RowPointer.");

        }



        // "K1 = 'v1' AND K2 = 'v2'". EVERY NAME MUST BE A PLAIN PROPERTY NAME, BECAUSE IT GOES INTO THE FILTER; EVERY VALUE IS QUOTED.

        private string BuildKeyFilter(SytelineInternalAPI sytelineAPI, Dictionary<string, string> keys)
        {

            foreach (string name in keys.Keys)
            {
                if (!IsPlainPropertyName(name))
                {
                    throw new Exception("'" + name + "' isn't a property name, so it can't be used as a key.");
                }
            }

            return string.Join(" AND ", keys.Select(key => key.Key + " = " + sytelineAPI.QuoteFilterValue(key.Value)));

        }



        // READS THE KEY VALUES A TRIGGER PASSES, "Name=Value|~|Name=Value", INTO AN ORDERED DICTIONARY. A PART WITHOUT "=" OR WITHOUT A NAME
        // IS LEFT OUT. EMPTY WHEN NOTHING WAS PASSED.

        private static Dictionary<string, string> ParseKeyValues(string keyValues)
        {

            Dictionary<string, string> keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in (keyValues ?? "").Split(new[] { KeyValueSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {

                int equalsIndex = part.IndexOf('=');

                if (equalsIndex > 0)
                {

                    string name = part.Substring(0, equalsIndex).Trim();

                    if (name != "" && !keys.ContainsKey(name))
                    {
                        keys[name] = part.Substring(equalsIndex + 1);
                    }

                }

            }

            return keys;

        }



        // READS A QUEUE ROW'S KeyValues (JSON). NULL WHEN IT HAS NONE OR IT CAN'T BE READ.

        private static Dictionary<string, string> ParseKeyJson(string keyJson)
        {

            if (string.IsNullOrWhiteSpace(keyJson))
            {
                return null;
            }

            try
            {
                Dictionary<string, string> keys = JsonConvert.DeserializeObject<Dictionary<string, string>>(keyJson);
                return keys != null && keys.Count > 0 ? new Dictionary<string, string>(keys, StringComparer.OrdinalIgnoreCase) : null;
            }
            catch (JsonException)
            {
                return null;
            }

        }



        // TRUE WHEN THERE IS AT LEAST ONE KEY AND NONE IS BLANK. A KEY THE CALLER DIDN'T SEND COMES THROUGH BLANK, AND A FILTER WITH IT WOULD
        // MATCH THE WRONG RECORD OR NONE.

        private static bool HasEveryKey(Dictionary<string, string> keys)
        {
            return keys != null && keys.Count > 0 && keys.Values.All(value => !string.IsNullOrWhiteSpace(value));
        }



        // WHETHER A LOADED KEY VALUE IS THE ONE QUEUED: THE SAME TEXT, TRIMMED AND WITHOUT REGARD TO CASE, OR THE SAME NUMBER

        private static bool SameKeyValue(string loaded, string queued)
        {

            if (string.Equals((loaded ?? "").Trim(), (queued ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            decimal loadedNumber;
            decimal queuedNumber;

            return decimal.TryParse(loaded, NumberStyles.Any, CultureInfo.InvariantCulture, out loadedNumber)
                && decimal.TryParse(queued, NumberStyles.Any, CultureInfo.InvariantCulture, out queuedNumber)
                && loadedNumber == queuedNumber;

        }



        // THE GUID IN A FILTER OF THE FORM "RowPointer = '<guid>'", AS THE TRIGGERS PASS IT. NULL FOR ANY OTHER FILTER, A BLANK ONE INCLUDED.

        private static string RowPointerFromFilter(string queryFilter)
        {

            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(queryFilter ?? "", @"^\s*\(?\s*RowPointer\s*=\s*N?'([0-9A-Fa-f\-{}]+)'\s*\)?\s*$");
            Guid rowPointer;

            return match.Success && Guid.TryParse(match.Groups[1].Value, out rowPointer) ? rowPointer.ToString() : null;

        }



        // HOW A QUEUED RECORD IS NAMED IN THE LOG'S RECORD ID WHEN IT WASN'T SENT: ITS KEY VALUES, ELSE ITS RowPointer

        private static string DescribeQueuedRecord(QueueRow row)
        {

            Dictionary<string, string> keys = ParseKeyJson(row.KeyValues);

            return keys != null ? string.Join(", ", keys.Select(key => key.Key + "=" + key.Value)) : (row.RecordRowPointer ?? "");

        }

    }

}
