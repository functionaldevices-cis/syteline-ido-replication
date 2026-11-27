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
                this.CaptureToQueue(sytelineAPI, utils, queryFilter, recordFilter, IDOName, keys, operation, source, queueRules);
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

        private void CaptureToQueue(SytelineInternalAPI sytelineAPI, Utilities utils, string queryFilter, string recordFilter, string IDOName, Dictionary<string, string> keys, string operation, string source, Dictionary<string, ReplicationRule> queueRules)
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
                    this.RunReplication(sytelineAPI, utils, -1, queryFilter, IDOName, SytelineInternalAPI.NoRecordCap, null, operation, source, queueRules);
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
                    this.RunReplication(sytelineAPI, utils, -1, recordFilter, IDOName, 1, null, operation, source, sendNowRules);
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
                { "Payload", payload }
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
        /*           stored when it was deleted, through each rule, writes the replication log, and removes the
        /*           rows it handled. A rule whose records couldn't all be handled keeps its rows for the next
        /*           run. Run it on a schedule, or by hand, with Max Concurrent 1 on its Background Task
        /*           Definition so two never run at once. Returns 16 when the queue couldn't be read, rows
        /*           couldn't be removed, or rows were kept. Never throws.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

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

            try
            {

                for (int batch = 0; batch < QueueMaxBatchesPerRun; batch++)
                {

                    List<QueueRow> queueRows = this.LoadQueueRows(sytelineAPI);

                    if (queueRows.Count == 0)
                    {
                        break;
                    }

                    foreach (IGrouping<string, QueueRow> ruleGroup in queueRows.GroupBy(row => row.IDOName + "\u0001" + row.RuleNum))
                    {

                        List<QueueRow> rows = ruleGroup.ToList();

                        // A RULE WHOSE RECORDS COULDN'T ALL BE HANDLED (THE RULE OR ITS RECORDS COULDN'T BE LOADED, OR A LOG ROW COULDN'T BE
                        // WRITTEN) KEEPS ITS ROWS FOR THE NEXT RUN, WHICH SENDS THEM AGAIN. A RECORD ALREADY SENT IS THEN SENT TWICE, WHICH
                        // AN UPSERT OR A DELETE BY KEY TOLERATES; LOSING IT WOULD BE WORSE.

                        if (!this.SendQueuedRule(sytelineAPI, utils, BGTaskNum, rows[0].IDOName, rows[0].RuleNum, rows))
                        {
                            rowsKept += rows.Count;
                            continue;
                        }

                        // THE ROWS ARE REMOVED ONLY AFTER THEIR RECORDS WERE SENT AND LOGGED. A FAILED SEND IS IN THE LOG, WITH ITS PAYLOAD,
                        // FOR THE RETRY TASK. IF THE ROWS CAN'T BE REMOVED, THE RUN STOPS; THE NEXT RUN SENDS THEM AGAIN.

                        if (sytelineAPI.DeleteRecords(QueueIDOName, rows.Select(row => sytelineAPI.BuildDeleteItem(row.ItemID)).ToList()) == null)
                        {
                            throw new Exception("Syteline didn't confirm that " + rows.Count + " queue row(s) for rule '" + rows[0].RuleNum + "' were removed.");
                        }

                        rowsHandled += rows.Count;

                    }

                    // KEPT ROWS ARE THE OLDEST, SO ANOTHER LOAD WOULD READ THEM AGAIN. THE RUN STOPS, AND THE NEXT RUN TRIES THEM FIRST.

                    if (rowsKept > 0 || queueRows.Count < QueueBatchSize)
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



        // THE OLDEST QUEUE ROWS, UP TO QueueBatchSize. LoadRecords ASKS FOR ReadCommitted, SO A SAVE THAT IS STILL RUNNING IS SKIPPED, NOT READ
        // BEFORE IT COMMITS (TESTS F3 AND F3b ON THE WIKI PAGE Trigger-Diagnostics).

        private List<QueueRow> LoadQueueRows(SytelineInternalAPI sytelineAPI)
        {

            LoadRecordsResponseData response = sytelineAPI.LoadRecords(
                IDOName: QueueIDOName,
                properties: new List<string>() { "IDOName", "RuleNum", "Operation", "KeyValues", "RecordRowPointer", "Payload", "CreateDate", "RowPointer" },
                filter: "",
                orderBy: "CreateDate ASC, RowPointer ASC",
                recordCap: QueueBatchSize
            );

            Func<IDOItem, string, string> read = (item, name) => item.PropertyValues[response.PropertyKeys[name]].Value;

            return response.Items.Select(item => new QueueRow()
            {
                ItemID = item.ItemID,
                IDOName = (read(item, "IDOName") ?? "").Trim(),
                RuleNum = (read(item, "RuleNum") ?? "").Trim(),
                Operation = NormalizeOperation(read(item, "Operation")),
                KeyValues = read(item, "KeyValues"),
                RecordRowPointer = read(item, "RecordRowPointer"),
                Payload = read(item, "Payload")
            }).ToList();

        }



        // SENDS ONE RULE'S QUEUED RECORDS. THE ROWS ARE IN THE ORDER THEY WERE QUEUED. EACH RECORD'S ROWS ARE COMBINED, SO THE TARGET IS TOLD
        // WHAT HAPPENED OVERALL:
        //
        //   EARLIER   LATER    SENT AS
        //   Insert    Update   Insert
        //   Insert    Delete   NOTHING (LOGGED AS Skipped)
        //   Update    Update   Update
        //   Update    Delete   Delete
        //   Delete    Insert   Delete, THEN Insert
        //
        // DELETES GO FIRST, THEN INSERTS AND UPDATES. RETURNS FALSE WHEN THE RULE'S ROWS MUST BE KEPT FOR THE NEXT RUN. NEVER THROWS.

        private bool SendQueuedRule(SytelineInternalAPI sytelineAPI, Utilities utils, int BGTaskNum, string IDOName, string ruleNum, List<QueueRow> rows)
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
                return false;

            }

            // A RULE THAT IS INACTIVE, GONE, OR NOW FOR ANOTHER IDO SENDS NOTHING. EACH RECORD GETS A Skipped ROW, AND THE ROWS ARE REMOVED, SO
            // A RULE SWITCHED OFF DOESN'T LEAVE ITS ROWS BEHIND. A BULK RESYNC CAN SEND THEM WHEN IT IS SWITCHED BACK ON.

            if (replicationRule == null)
            {

                bool written = true;

                foreach (IGrouping<string, QueueRow> operationRows in rows.GroupBy(row => row.Operation))
                {

                    ReplicationRuleResult skipped = new ReplicationRuleResult(ruleNum, "", 0, true, "", operationRows.Key);

                    foreach (IGrouping<string, QueueRow> recordRows in operationRows.GroupBy(row => row.RecordKey))
                    {
                        skipped.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(recordRows.First()), null, ReplicationRecordOutcome.StatusSkipped, "Nothing was sent: rule '" + ruleNum + "' isn't active for IDO '" + IDOName + "' any more, or was deleted."));
                    }

                    written &= this.WriteReplicationLog(sytelineAPI, utils, IDOName, operationRows.Key, QueueSource(operationRows.Key), new List<ReplicationRuleResult>() { skipped });

                }

                return written;

            }

            List<QueueRow> deletes = new List<QueueRow>();
            List<QueueRow> inserts = new List<QueueRow>();
            List<QueueRow> updates = new List<QueueRow>();
            ReplicationRuleResult nothingToSend = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, 0, true, "", OperationDelete)
            {
                CredentialName = replicationRule.CredentialName ?? ""
            };

            foreach (IGrouping<string, QueueRow> recordGroup in rows.GroupBy(row => row.RecordKey))
            {

                List<QueueRow> recordRows = recordGroup.ToList();
                QueueRow first = recordRows.First();
                QueueRow last = recordRows.Last();
                QueueRow lastDelete = recordRows.LastOrDefault(row => row.Operation == OperationDelete);

                if (last.Operation == OperationDelete)
                {

                    if (first.Operation == OperationInsert)
                    {
                        nothingToSend.RecordOutcomes.Add(new ReplicationRecordOutcome(DescribeQueuedRecord(first), null, ReplicationRecordOutcome.StatusSkipped, "Nothing was sent: the record was inserted and deleted before it was sent."));
                    }
                    else
                    {
                        deletes.Add(lastDelete);
                    }

                }
                else
                {

                    if (lastDelete != null)
                    {
                        deletes.Add(lastDelete);
                        inserts.Add(last);
                    }
                    else if (first.Operation == OperationInsert)
                    {
                        inserts.Add(last);
                    }
                    else
                    {
                        updates.Add(last);
                    }

                }

            }

            bool handled = true;

            if (nothingToSend.RecordOutcomes.Count > 0)
            {
                handled &= this.WriteReplicationLog(sytelineAPI, utils, IDOName, OperationDelete, SourceQueueBeforeDelete, new List<ReplicationRuleResult>() { nothingToSend });
            }

            handled &= this.SendQueuedDeletes(sytelineAPI, utils, IDOName, replicationRule, deletes);
            handled &= this.SendQueuedUpserts(sytelineAPI, utils, BGTaskNum, IDOName, replicationRule, inserts, OperationInsert, SourceQueueAfterInsert);
            handled &= this.SendQueuedUpserts(sytelineAPI, utils, BGTaskNum, IDOName, replicationRule, updates, OperationUpdate, SourceQueueAfterUpdate);

            return handled;

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

            return this.WriteReplicationLog(sytelineAPI, utils, IDOName, OperationDelete, SourceQueueBeforeDelete, new List<ReplicationRuleResult>() { ruleResult });

        }



        // SENDS THE QUEUED INSERTS OR UPDATES, QueueRecordsPerLoad RECORDS AT A TIME. EACH BATCH IS FIRST LOADED BY ITS KEYS (OR RowPointer),
        // SO A RECORD DELETED SINCE IT WAS QUEUED GETS A Skipped ROW, AND THEN SENT THROUGH RunReplication WITH THE SAME FILTER, WHICH LOADS
        // THE RECORDS' CURRENT VALUES, SENDS THEM AND LOGS THEM AS EVERY OTHER RUN DOES (A FAILED SEND WITH ITS PAYLOAD, FOR THE RETRY TASK).
        //
        // RETURNS FALSE, AND STOPS, WHEN A BATCH COULDN'T BE LOADED OR LOGGED: THE RULE'S ROWS ARE THEN KEPT FOR THE NEXT RUN, BECAUSE A LOAD
        // THAT FAILS LEAVES ONE Failed ROW FOR THE RULE, WITHOUT THE RECORDS OR THEIR PAYLOADS. NEVER THROWS.

        private bool SendQueuedUpserts(SytelineInternalAPI sytelineAPI, Utilities utils, int BGTaskNum, string IDOName, ReplicationRule replicationRule, List<QueueRow> rows, string operation, string source)
        {

            for (int i = 0; i < rows.Count; i += QueueRecordsPerLoad)
            {

                List<QueueRow> batch = rows.Skip(i).Take(QueueRecordsPerLoad).ToList();

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

                        // A RESULT THAT FAILED WITHOUT ANY RECORD IN IT IS A LOAD OR SETUP FAILURE, NOT A SEND THAT FAILED: NOTHING WAS SENT, AND
                        // THE LOG HAS NO PAYLOAD TO RETRY

                        if (!runResult.LogWritten || runResult.RuleResults.Any(ruleResult => !ruleResult.Success && ruleResult.RecordOutcomes.Count == 0))
                        {
                            return false;
                        }

                    }

                }
                catch (Exception ex)
                {

                    utils.WriteLogMessage("Couldn't send the queued records of rule '" + replicationRule.RuleNum + "' for IDO '" + IDOName + "', so they were kept: " + this.DescribeException(ex));
                    return false;

                }

            }

            return true;

        }



        // THE ROWS IN A BATCH WHOSE RECORD NO LONGER EXISTS. THE BATCH IS LOADED BY ITS KEYS (OR RowPointer), WITH THE KEYS AND RowPointer
        // ONLY, AND EACH ROW IS MATCHED TO A LOADED RECORD BY ITS RowPointer OR ITS KEY VALUES (TRIMMED, WITHOUT REGARD TO CASE, AND AS
        // NUMBERS WHEN BOTH ARE NUMBERS).

        private List<QueueRow> FindMissingRecords(SytelineInternalAPI sytelineAPI, string IDOName, List<QueueRow> batch)
        {

            List<string> keyNames = batch
                .Select(row => ParseKeyJson(row.KeyValues))
                .Where(keys => keys != null)
                .SelectMany(keys => keys.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string filter = string.Join(" OR ", batch.Select(row => "( " + this.BuildQueuedRecordFilter(sytelineAPI, row) + " )"));

            LoadRecordsResponseData loaded = sytelineAPI.LoadRecords(
                IDOName: IDOName,
                properties: WithRowPointer(keyNames),
                filter: filter,
                orderBy: "",
                recordCap: SytelineInternalAPI.NoRecordCap
            );

            int rowPointerIndex = RowPointerIndex(loaded);
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

                if (!string.IsNullOrEmpty(row.RecordRowPointer) && loadedRowPointers.Contains(row.RecordRowPointer))
                {
                    return false;
                }

                Dictionary<string, string> keys = ParseKeyJson(row.KeyValues);

                return keys == null || !loadedKeys.Any(loadedKey => keys.All(key => loadedKey.ContainsKey(key.Key) && SameKeyValue(loadedKey[key.Key], key.Value)));

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
