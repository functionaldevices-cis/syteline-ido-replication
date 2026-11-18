using Infor.DocumentManagement.ICP;
using Mongoose.Core.Extensions;
using Mongoose.IDO;
using Mongoose.IDO.DataAccess;
using Mongoose.IDO.Metadata;
using Mongoose.IDO.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ue_AIR_IDOReplicationRules_ECA.Helpers;
using ue_AIR_IDOReplicationRules_ECA.Models;
using ue_AIR_IDOReplicationRules_ECA.Models.AzureEventHubAPI;
using ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI;
using ue_AIR_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_AIR_IDOReplicationRules_ECA
{

    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /*
    /* Name:     ue_AIR_IDOReplicationRules_ECA
    /* Purpose:  Assembly class to hold all methods related to AES-Driven IDO-Layer Replication.
    /* Date:     2025-04-21
    /* Author:   Andy Mercer
    /*
    /* Copyright 2025, Functional Devices, Inc
    /*
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/

    [IDOExtensionClass("ue_AIR_IDOReplicationRules_ECA")]
    public class ue_AIR_IDOReplicationRules_ECA : ExtensionClassBase
    {

        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_ParseSavedMap
        /* Date:     2026-16-14
        /* Authors:  Andy Mercer
        /* Purpose:  This method will take a JSON-encoded string of ReplicationFields, parse it, and insert a matching
        /*           set of ue_AIR_ReplicationMapField records into Syteline.
        /*
        /* Free to use under MIT License, Copyright (c) 2025 FDI Information Systems. See full license at
        /* https://github.com/functionaldevices-cis/syteline-custom-load-method-examples?tab=MIT-1-ov-file#readme
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_ParseSavedMap(string encodedReplicationFields = "")
        {

            if (encodedReplicationFields != "")
            {

                List<ReplicationField> replicationFields = JsonConvert.DeserializeObject<List<ReplicationField>>(encodedReplicationFields) ?? throw new Exception("Error, unable to parse the jobs file. Please check that the file exists and has correctly formatted JSON data.");

            }

            return 0;

        }




        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_ReplicateRecordToExternalSystems
        /* 
        /* Purpose:  Method which accepts a RowPointer and IDOName, using those to load a record, and then pushes
        /*           that record to an external system such as an Azure Event Hub or Salesforce REST API.
        /* 
        /* Date:     2025-04-21
        /* 
        /* Author:   Andy Mercer
        /*
        /* Copyright 2025, Functional Devices, Inc
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        // THE OPERATION: WHAT A RUN DOES WITH ITS RECORDS AT THE TARGET. IT IS SENT TO THE TARGET AND WRITTEN TO THE REPLICATION LOG.
        // THE VALUES MUST MATCH THE Operation INLINE LIST ON ue_AIR_IDOReplicationLogs, AND THE FIRST THREE MUST MATCH WHAT THE
        // TRIGGERS PASS (OperationForEvent IN THE CONFIG ASSEMBLY).
        // Upsert MEANS INSERT OR UPDATE, WHICHEVER APPLIES. IT IS THE OPERATION OF EVERY RUN THAT DOESN'T PASS ONE OF THE OTHER THREE:
        // A BULK LOAD, A TEST, OR A TRIGGER THAT PASSES NONE.

        private const string OperationInsert = "Insert";

        private const string OperationUpdate = "Update";

        private const string OperationDelete = "Delete";

        private const string OperationUpsert = "Upsert";

        // THE SOURCE: WHAT STARTED A RUN. IT IS ONLY WRITTEN TO THE REPLICATION LOG, AND A RETRY LEAVES IT AS IT WAS.
        // THE VALUES MUST MATCH THE Source INLINE LIST ON ue_AIR_IDOReplicationLogs.

        private const string SourceEventAfterInsert = "Event: After-Insert";    // THE IdoPostItemInsert TRIGGER

        private const string SourceEventAfterUpdate = "Event: After-Update";    // THE IdoPostItemUpdate TRIGGER

        private const string SourceEventBeforeDelete = "Event: Before-Delete";  // THE IdoOnItemDelete TRIGGER

        private const string SourceEventUnknown = "Event: Unknown";             // A TRIGGER RUN THAT DIDN'T PASS Insert, Update OR Delete

        private const string SourceTaskBulk = "BG Task: Bulk";                  // A BACKGROUND TASK RUN (A BULK LOAD)

        private const string SourceTaskTest = "BG Task: Test";                  // A BACKGROUND TASK RUN FROM THE RULES FORM'S TEST TAB

        // WHAT THE RULES FORM'S TEST TAB PASSES AS THE OPERATION TO MARK ITS RUN AS A TEST. THE RUN'S OPERATION IS THEN Upsert.

        private const string TestOperationParameter = "Test";

        // SYTELINE'S ERROR SEVERITY. A BACKGROUND TASK WHOSE METHOD RETURNS IT IS SHOWN AS FAILED.

        private const int BackgroundTaskFailedReturnCode = 16;

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_ReplicateRecordToExternalSystems(int BGTaskNum = -1, string queryFilter = "", string IDOName = "", int recordCap = 1, string ruleNum = null, string operation = "")
        {

            // TRIGGERS PASS A BLANK RULE NUMBER TO REACH THE OPERATION PARAMETER. BLANK MEANS EVERY ACTIVE RULE FOR THE IDO.

            if (string.IsNullOrWhiteSpace(ruleNum))
            {
                ruleNum = null;
            }

            // THE OPERATION IS Insert, Update OR Delete WHEN THE CALLER PASSES ONE, AS THE TRIGGERS DO. ANYTHING ELSE, BLANK INCLUDED,
            // BECOMES Upsert: THE RECORD IS INSERTED OR UPDATED AT THE TARGET, WHICHEVER APPLIES. A BULK LOAD PASSES NO OPERATION,
            // AND THE RULES FORM'S TEST TAB PASSES Test, SO BOTH ARE UPSERTS.
            //
            // THE SOURCE SAYS WHAT STARTED THE RUN: ONE OF THE THREE TRIGGER EVENTS, A BULK LOAD OR A TEST. A BACKGROUND TASK RUN
            // (BGTaskNum ABOVE 0) IS A BULK LOAD UNLESS IT PASSES Test, WHATEVER OPERATION IT PASSES.

            bool isTest = string.Equals((operation ?? "").Trim(), TestOperationParameter, StringComparison.OrdinalIgnoreCase);

            operation = NormalizeOperation(operation);

            string source = BGTaskNum > 0 ? (isTest ? SourceTaskTest : SourceTaskBulk) : SourceForEvent(operation);

            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands,
                BGTaskNum: BGTaskNum
            );

            List<ReplicationRuleResult> ruleResults = new List<ReplicationRuleResult>();
            int recordCountLoaded = 0;

            // THIS METHOD RUNS INSIDE AES TRIGGERS ON THE SOURCE IDO, SO IT MUST NEVER THROW. AN UNHANDLED EXCEPTION
            // WOULD FAIL THE EVENT ACTION AND ROLL BACK THE USER'S SAVE. EVERY ERROR IS CAUGHT, RECORDED AND LOGGED,
            // AND A TRIGGER RUN ALWAYS RETURNS 0.
            //
            // A BACKGROUND TASK RUN (BGTaskNum ABOVE 0) HAS NO SAVE TO PROTECT, SO IT REPORTS WHAT HAPPENED: IT RETURNS
            // BackgroundTaskFailedReturnCode WHEN ANY RULE FAILED OR WHEN NOTHING COULD BE SENT, SO THE TASK DOESN'T SHOW
            // AS SUCCEEDED, AND ITS MESSAGES SAY WHY.

            bool isBackgroundTask = BGTaskNum > 0;
            bool nothingToSend = false;

            // A BACKGROUND TASK RUN THAT NAMES A RULE RUNS THAT RULE EVEN WHEN IT ISN'T ACTIVE, SO A RULE CAN BE TESTED
            // BEFORE IT IS SWITCHED ON. TRIGGERS NEVER NAME A RULE, SO THEY ONLY EVER RUN ACTIVE RULES.

            bool includeInactiveRules = isBackgroundTask && ruleNum != null;

            // DECLARED OUTSIDE THE TRY SO THE CATCH CAN SEE WHICH RULES WERE LOADED BEFORE A FAILURE

            Dictionary<string, ReplicationRule> replicationRules = new Dictionary<string, ReplicationRule>();

            try
            {

                // LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO

                replicationRules = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum, includeInactiveRules);

                if (replicationRules.Keys.Count == 0)
                {

                    // WITHOUT THIS MESSAGE A BACKGROUND RUN WITH NO MATCHING RULE WOULD FINISH WITHOUT SAYING ANYTHING

                    nothingToSend = true;

                    utils.WriteLogMessage(ruleNum != null
                        ? "Nothing was sent: there is no replication rule " + ruleNum + " for IDO '" + IDOName + "'."
                        : "Nothing was sent: IDO '" + IDOName + "' has no active replication rules. Check the rule's IDO Name and Is Active.");

                }

                if (replicationRules.Keys.Count > 0)
                {

                    // LOAD THE REPLICATION RECORD(S). A RULE WHOSE RECORDS COULDN'T BE LOADED ISN'T IN recordsByRule: IT ALREADY HAS A FAILED
                    // RESULT IN ruleResults, AND THE OTHER RULES STILL RUN.

                    Dictionary<string, LoadRecordsResponseData> recordsByRule = this.LoadSourceRecords(sytelineAPI, IDOName, queryFilter, recordCap, replicationRules, operation, ruleResults);

                    // EVERY RULE IS LOADED WITH THE SAME FILTER AND RECORD CAP, SO THE COUNTS ONLY DIFFER IF A RECORD CHANGED BETWEEN TWO LOADS

                    recordCountLoaded = recordsByRule.Values.Select(recordsResponse => recordsResponse.Items.Count).DefaultIfEmpty(0).Max();

                    // TRIGGER RUNS LOG EVERY RECORD. BACKGROUND TASK RUNS LOG EVERY RECORD UP TO LogAllRecordsLimit, AND ONLY FAILURES ABOVE IT.

                    bool logEveryRecord = BGTaskNum <= 0 || recordCountLoaded <= LogAllRecordsLimit;

                    // A RUN THAT ONLY LOGS FAILURES STILL LOGS A RECORD THAT HAS A FAILED ROW WAITING FOR A RETRY, SO THE RETRY TASK CAN SEE THAT
                    // THE RECORD WAS REPLICATED AGAIN AND DOESN'T RESEND THE OLDER DATA. BuildOutcome READS THIS.

                    this.recordIDsAwaitingRetry = logEveryRecord
                        ? new Dictionary<string, HashSet<string>>()
                        : this.LoadRecordIDsAwaitingRetry(sytelineAPI, utils, IDOName, recordsByRule.Keys);

                    if (recordsByRule.Count > 0)
                    {

                        utils.WriteLogMessage("Count records found: " + recordCountLoaded.ToString() + " records. (Record cap was " + recordCap.ToString() + ").");

                        if (recordCountLoaded == 0)
                        {
                            nothingToSend = true;
                            utils.WriteLogMessage("Nothing was sent: no '" + IDOName + "' record matches the filter \"" + queryFilter + "\".");
                        }

                    }

                    // STEP THROUGH EACH RULE
                    // REMAP THE RECORD(S) BASED ON THE MAP
                    // SEND

                    foreach (KeyValuePair<string, ReplicationRule> replicationRuleKeyValPair in replicationRules)
                    {

                        LoadRecordsResponseData replicationRecordsResponse;

                        if (!recordsByRule.TryGetValue(replicationRuleKeyValPair.Key, out replicationRecordsResponse) || replicationRecordsResponse.Items.Count == 0)
                        {
                            continue;
                        }

                        // EACH RULE IS ISOLATED, SO ONE FAILING TARGET DOESN'T STOP THE OTHERS

                        ruleResults.Add(this.ReplicateRule(
                            replicationRule: replicationRuleKeyValPair.Value,
                            replicationRecordsResponse: replicationRecordsResponse,
                            operation: operation,
                            logEveryRecord: logEveryRecord,
                            utils: utils
                        ));

                    }

                }

            }
            catch (Exception ex)
            {

                string setupErrorMessage = "Replication setup failed for IDO '" + IDOName + "': " + this.DescribeException(ex);

                // AN UNEXPECTED FAILURE AFTER THE RULES WERE LOADED STOPS EVERY RULE THAT HASN'T RUN YET.
                // EACH OF THOSE GETS ITS OWN RESULT, SO ITS LOG ROW CARRIES THE RULE'S NUMBER, TARGET TYPE AND CREDENTIAL NAME.

                List<ReplicationRule> rulesWithoutResult = replicationRules.Values
                    .Where(rule => !ruleResults.Any(ruleResult => ruleResult.RuleNum == rule.RuleNum))
                    .ToList();

                foreach (ReplicationRule replicationRule in rulesWithoutResult)
                {
                    ruleResults.Add(BuildFailedRuleResult(replicationRule, setupErrorMessage, operation));
                }

                // A FAILURE BEFORE THE RULES WERE LOADED HAS NO RULE TO NAME, SO IT GETS ONE RESULT WITH NO TARGET TYPE OR CREDENTIAL

                if (rulesWithoutResult.Count == 0)
                {

                    ruleResults.Add(new ReplicationRuleResult(
                        ruleNum: ruleNum ?? "",
                        targetType: "",
                        recordCount: 0,
                        success: false,
                        errorMessage: setupErrorMessage,
                        operation: operation
                    ));

                }

            }

            // LOG THE OUTCOME OF EACH RULE

            foreach (ReplicationRuleResult ruleResult in ruleResults)
            {
                utils.WriteLogMessage(ruleResult.ToString());
            }

            // WRITE THE REPLICATION LOG. TRIGGERS PASS -1 AS THE BACKGROUND TASK NUMBER; BACKGROUND TASKS PASS THEIR OWN.

            this.WriteReplicationLog(sytelineAPI, utils, IDOName, operation, source, ruleResults);

            // ONLY A BACKGROUND TASK RUN REPORTS FAILURE. A TRIGGER RUN ALWAYS RETURNS 0, SO IT CAN'T FAIL THE USER'S SAVE.

            if (isBackgroundTask && (nothingToSend || ruleResults.Any(ruleResult => !ruleResult.Success)))
            {
                return BackgroundTaskFailedReturnCode;
            }

            return 0;

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ReplicateRule
        /* Purpose:  Remaps the loaded records for a single rule and sends them to the rule's target. Never
        /*           throws. Any exception or rejected push is returned as a failed ReplicationRuleResult, with
        /*           one outcome per remapped record for the replication log.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        private ReplicationRuleResult ReplicateRule(ReplicationRule replicationRule, LoadRecordsResponseData replicationRecordsResponse, string operation, bool logEveryRecord, Utilities utils)
        {

            List<Dictionary<string, object>> remappedReplicationRecords = null;
            ReplicationRuleResult ruleResult;

            // WARNINGS FROM TRANSFORMATIONS THAT COULDN'T CONVERT A VALUE, KEYED BY THE REMAPPED RECORD (REFERENCE EQUALITY)

            Dictionary<Dictionary<string, object>, string> transformWarnings = new Dictionary<Dictionary<string, object>, string>();

            try
            {

                // @todo, need to extract this from the Rules loop, then I can loop through the records once, rather than rule count times. Should give major performance increase if you have lots of rules for a single IDO.

                remappedReplicationRecords = this.RemapRecords(
                    fields: replicationRule.MapFieldsList,
                    recordsResponseData: replicationRecordsResponse,
                    transformWarnings: transformWarnings
                );

                if (transformWarnings.Count > 0)
                {
                    utils.WriteLogMessage("Rule '" + replicationRule.RuleNum + "': " + transformWarnings.Count + " record(s) had a value a transformation couldn't convert, which was sent unchanged. The replication log rows say which.");
                }

                ruleResult = this.SendRule(replicationRule, remappedReplicationRecords, operation, logEveryRecord, utils, transformWarnings);

            }
            catch (Exception ex)
            {

                // IF THE RECORDS WERE REMAPPED BEFORE THE FAILURE (FOR EXAMPLE A NETWORK ERROR WHILE SENDING), EACH ONE IS LOGGED AS
                // FAILED WITH ITS PAYLOAD, SO THE RETRY TASK CAN RESEND IT

                string errorMessage = this.DescribeException(ex);

                ruleResult = new ReplicationRuleResult(
                    ruleNum: replicationRule.RuleNum,
                    targetType: replicationRule.TargetType,
                    recordCount: remappedReplicationRecords != null ? remappedReplicationRecords.Count : 0,
                    success: false,
                    errorMessage: errorMessage
                );

                if (remappedReplicationRecords != null)
                {
                    ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, remappedReplicationRecords, ReplicationRecordOutcome.StatusFailed, errorMessage, logEveryRecord, transformWarnings);
                }

            }

            ruleResult.Operation = operation;
            ruleResult.CredentialName = replicationRule.CredentialName ?? "";
            return ruleResult;

        }



        // A FAILED RESULT FOR A RULE THAT NEVER GOT AS FAR AS REMAPPING ITS RECORDS. IT BECOMES ONE FAILED LOG ROW WITHOUT A PAYLOAD.

        private static ReplicationRuleResult BuildFailedRuleResult(ReplicationRule replicationRule, string errorMessage, string operation)
        {

            return new ReplicationRuleResult(
                ruleNum: replicationRule.RuleNum ?? "",
                targetType: replicationRule.TargetType ?? "",
                recordCount: 0,
                success: false,
                errorMessage: errorMessage,
                operation: operation
            )
            {
                CredentialName = replicationRule.CredentialName ?? ""
            };

        }



        // LOADS THE SOURCE RECORDS FOR EVERY RULE AND RETURNS THEM KEYED BY RULE NUMBER.
        //
        // NORMALLY THAT IS ONE LOAD, WITH THE UNION OF EVERY RULE'S SOURCE PROPERTIES, AND EVERY RULE SHARES ITS RESULT. IF THAT LOAD
        // FAILS, MOST OFTEN BECAUSE ONE RULE'S FIELD MAP NAMES A PROPERTY THE IDO DOESN'T HAVE, EACH RULE'S RECORDS ARE LOADED ON
        // THEIR OWN, SO ONLY THE RULE THAT CAN'T BE LOADED FAILS. A FAILED RULE IS LEFT OUT OF THE RETURNED DICTIONARY AND GETS A
        // FAILED RESULT IN ruleResults.

        private Dictionary<string, LoadRecordsResponseData> LoadSourceRecords(SytelineInternalAPI sytelineAPI, string IDOName, string queryFilter, int recordCap, Dictionary<string, ReplicationRule> replicationRules, string operation, List<ReplicationRuleResult> ruleResults)
        {

            Dictionary<string, LoadRecordsResponseData> recordsByRule = new Dictionary<string, LoadRecordsResponseData>();
            Exception combinedLoadException;

            try
            {

                LoadRecordsResponseData combinedResponse = sytelineAPI.LoadRecords(
                    IDOName: IDOName,
                    properties: replicationRules.Values.Select(rule => rule.SourceIDOProperties).SelectMany(innerList => innerList).Distinct().ToList(),
                    filter: queryFilter,
                    orderBy: "",
                    recordCap: recordCap
                );

                foreach (string loadedRuleNum in replicationRules.Keys)
                {
                    recordsByRule[loadedRuleNum] = combinedResponse;
                }

                return recordsByRule;

            }
            catch (Exception ex)
            {
                combinedLoadException = ex;
            }

            // THE COMBINED LOAD FAILED. THE IDO'S PROPERTY NAMES ARE ONLY USED TO SAY WHICH PROPERTY IS WRONG IN THE MESSAGE.
            // WHETHER A RULE FAILS IS DECIDED BY ITS OWN LOAD, NOT BY THIS LIST.

            HashSet<string> idoPropertyNames = this.LoadIDOPropertyNames(IDOName);

            // THE FIRST SET OF RECORDS THAT LOADED. A RULE WHOSE FIELD MAP HAS ONLY LITERAL VALUES HAS NO PROPERTIES TO LOAD, SO IT
            // USES THESE RECORDS. THAT IS WHY RULES WITH SOURCE PROPERTIES GO FIRST.

            LoadRecordsResponseData firstLoadedResponse = null;

            foreach (KeyValuePair<string, ReplicationRule> replicationRuleKeyValPair in replicationRules.OrderBy(keyValPair => keyValPair.Value.SourceIDOProperties.Count == 0 ? 1 : 0))
            {

                ReplicationRule replicationRule = replicationRuleKeyValPair.Value;
                Exception ruleLoadException = combinedLoadException;

                // WITH ONE RULE, ITS OWN LOAD WOULD BE THE COMBINED LOAD AGAIN, SO IT ISN'T REPEATED

                if (replicationRules.Count > 1)
                {

                    if (replicationRule.SourceIDOProperties.Count == 0 && firstLoadedResponse != null)
                    {
                        recordsByRule[replicationRuleKeyValPair.Key] = firstLoadedResponse;
                        continue;
                    }

                    try
                    {

                        LoadRecordsResponseData ruleResponse = sytelineAPI.LoadRecords(
                            IDOName: IDOName,
                            properties: replicationRule.SourceIDOProperties,
                            filter: queryFilter,
                            orderBy: "",
                            recordCap: recordCap
                        );

                        recordsByRule[replicationRuleKeyValPair.Key] = ruleResponse;
                        firstLoadedResponse = firstLoadedResponse ?? ruleResponse;

                        continue;

                    }
                    catch (Exception ex)
                    {
                        ruleLoadException = ex;
                    }

                }

                string loadErrorMessage = "Rule '" + replicationRule.RuleNum + "' couldn't load its records from IDO '" + IDOName + "': " + this.DescribeException(ruleLoadException);

                if (idoPropertyNames == null)
                {
                    loadErrorMessage += " Check that every IDO Property in the rule's field map exists on the IDO.";
                }
                else
                {

                    List<string> unknownProperties = replicationRule.SourceIDOProperties.Where(propertyName => !idoPropertyNames.Contains((propertyName ?? "").Trim())).ToList();

                    if (unknownProperties.Count > 0)
                    {
                        loadErrorMessage += " The rule's field map names " + (unknownProperties.Count == 1 ? "a property" : "properties") + " the IDO doesn't have: " + string.Join(", ", unknownProperties) + ".";
                    }

                }

                ruleResults.Add(BuildFailedRuleResult(replicationRule, loadErrorMessage, operation));

            }

            return recordsByRule;

        }



        // THE NAMES OF AN IDO'S PROPERTIES, COMPARED WITHOUT REGARD TO CASE. NULL WHEN THEY COULDN'T BE READ.

        private HashSet<string> LoadIDOPropertyNames(string IDOName)
        {

            try
            {

                HashSet<string> propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Mongoose.IDO.Protocol.PropertyInfo propertyInfo in this.Context.Commands.GetPropertyInfo(IDOName).Properties)
                {
                    propertyNames.Add(propertyInfo.Name);
                }

                return propertyNames.Count > 0 ? propertyNames : null;

            }
            catch (Exception)
            {
                return null;
            }

        }



        private ReplicationRuleResult SendRule(ReplicationRule replicationRule, List<Dictionary<string, object>> remappedReplicationRecords, string operation, bool logEveryRecord, Utilities utils, Dictionary<Dictionary<string, object>, string> transformWarnings = null)
        {

            int recordCount = remappedReplicationRecords.Count;
            ReplicationRuleResult ruleResult;

            switch (replicationRule.TargetType)
            {

                case "AzureEventHub":

                    // CREDENTIAL VALUES: 01 = ConnectionString, 02 = EventSource, 03 = MaxBatchKBOverride (OPTIONAL)
                    // RULE OPTIONS:      01 = EventSchemaUri,   02 = UpsertFieldName (USED AS THE RECORD ID IN FAILURE MESSAGES),
                    //                    03 = OperationFieldName (OPTIONAL: BODY FIELD THAT RECEIVES THE OPERATION)
                    // THE CREDENTIAL POSITIONS MUST MATCH THE CREDENTIALS FORM SCRIPT (UpdateValueLabels), AND THE OPTION POSITIONS MUST MATCH THE
                    // RULES FORM SCRIPT (UpdateVariableUI).

                    string eventSchemaUriValue = (replicationRule.Option01 ?? "").Trim();
                    string eventSource = (replicationRule.CredentialValue02 ?? "").Trim();
                    FabricEventSchemaUri eventSchemaUri = null;

                    // MaxBatchKBOverride DEPENDS ON THE EVENT HUBS TIER. BLANK OR INVALID USES THE DEFAULT, WHICH IS SAFE FOR THE
                    // BASIC TIER'S 256 KB LIMIT. STANDARD AND HIGHER TIERS CAN SET UP TO 850.

                    int maxBatchKB;
                    int maxBatchBytes = int.TryParse((replicationRule.CredentialValue03 ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBatchKB) && maxBatchKB > 0
                        ? (int)Math.Min((long)maxBatchKB * 1000, AzureEventHubPusher.MaximumMaxBatchBytes)
                        : AzureEventHubPusher.DefaultMaxBatchBytes;

                    if (eventSchemaUriValue != "")
                    {

                        // FABRIC EVENT SCHEMA EVENTSTREAMS REJECT EVENTS WITHOUT A CLOUDEVENTS SOURCE, SO DON'T SEND ANYTHING

                        if (eventSource == "")
                        {
                            string missingSourceMessage = "The rule has an Event Schema URI, but its credential has no Event Source. Set Event Source on the credential (for example, your company's website URL).";
                            ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, recordCount, false, missingSourceMessage);
                            ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, remappedReplicationRecords, ReplicationRecordOutcome.StatusFailed, missingSourceMessage, logEveryRecord, transformWarnings);
                            return ruleResult;
                        }

                        eventSchemaUri = new FabricEventSchemaUri(eventSchemaUriValue);

                    }

                    AzureEventHubPushResult eventHubPushResult = AzureEventHubPusher.Push(
                        credential: AzureEventHubPusher.GetCredential(replicationRule.CredentialValue01),
                        records: remappedReplicationRecords,
                        idFieldName: replicationRule.Option02,
                        eventSchemaUri: eventSchemaUri,
                        eventSource: eventSource,
                        maxBatchBytes: maxBatchBytes,
                        operation: operation,
                        operationFieldName: replicationRule.Option03
                    );

                    ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, recordCount, eventHubPushResult.Success, eventHubPushResult.ErrorSummary);

                    for (int i = 0; i < recordCount; i++)
                    {
                        string recordError = "";
                        bool recordFailed = eventHubPushResult.FailedRecordErrors.TryGetValue(i, out recordError);
                        ReplicationRecordOutcome eventHubOutcome = this.BuildOutcome(replicationRule, remappedReplicationRecords[i], recordFailed ? ReplicationRecordOutcome.StatusFailed : ReplicationRecordOutcome.StatusSucceeded, recordFailed ? recordError : "", logEveryRecord, transformWarnings);

                        if (eventHubOutcome != null)
                        {
                            ruleResult.RecordOutcomes.Add(eventHubOutcome);
                        }
                    }

                    return ruleResult;

                case "SalesforceRESTAPI":

                    // CREDENTIAL VALUES: 01 = ClientId, 02 = ClientSecret, 03 = Domain
                    // RULE OPTIONS:      01 = DestinationName (sObject), 02 = ExternalIDFieldName, 03 = DeleteBehavior (Skip OR Delete)
                    // THE CREDENTIAL POSITIONS MUST MATCH THE CREDENTIALS FORM SCRIPT (UpdateValueLabels), AND THE OPTION POSITIONS MUST MATCH THE
                    // RULES FORM SCRIPT (UpdateVariableUI).

                    bool isDelete = operation == OperationDelete;
                    bool deleteInSalesforce = string.Equals((replicationRule.Option03 ?? "").Trim(), "Delete", StringComparison.OrdinalIgnoreCase);

                    // DELETES ARE SKIPPED UNLESS THE RULE OPTS IN, SO A SYTELINE DELETE NEVER REMOVES SALESFORCE DATA BY DEFAULT

                    if (isDelete && !deleteInSalesforce)
                    {
                        string skippedMessage = "Delete skipped, because the rule's DeleteBehavior isn't Delete.";
                        ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, recordCount, true, skippedMessage);
                        ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, remappedReplicationRecords, ReplicationRecordOutcome.StatusSkipped, skippedMessage, logEveryRecord, transformWarnings);
                        return ruleResult;
                    }

                    // SIGN-IN USES THE OAUTH CLIENT CREDENTIALS FLOW ONLY, SO THE CREDENTIAL IS THE CONSUMER KEY AND SECRET OF A SALESFORCE
                    // EXTERNAL CLIENT APP PLUS THE ORG'S MY DOMAIN NAME. Domain WORKS AS IT DOES IN SyteBridge AND THE Bridjer SALESFORCE
                    // LIBRARY: "mycompany" FOR PRODUCTION, "mycompany--sandboxname.sandbox" FOR A SANDBOX, AND SIGN-IN GOES TO
                    // https://<Domain>.my.salesforce.com. SalesforceAPICredential THROWS WHEN A VALUE IS MISSING, AND THE CALLER LOGS THAT.

                    SalesforceRestAPI salesforceRestAPI = new SalesforceRestAPI(
                        credential: new SalesforceAPICredential(
                            ClientId: replicationRule.CredentialValue01,
                            ClientSecret: replicationRule.CredentialValue02,
                            Domain: NormalizeSalesforceDomain(replicationRule.CredentialValue03)
                        )
                    );

                    utils.WriteLogMessage((isDelete ? "Deleting " : "Pushing ") + recordCount + " records " + (isDelete ? "from" : "to") + " Salesforce.");

                    SalesforceAPIUpsertResults salesforceUpsertResults = isDelete
                        ? salesforceRestAPI.DeleteRecords(
                            objectName: replicationRule.Option01,
                            externalIDFieldName: replicationRule.Option02,
                            records: remappedReplicationRecords
                        ).GetAwaiter().GetResult()
                        : salesforceRestAPI.UpsertRecords(
                            objectName: replicationRule.Option01,
                            externalIDFieldName: replicationRule.Option02,
                            records: remappedReplicationRecords
                        ).GetAwaiter().GetResult();

                    // BATCHES ARE SENT IN ORDER: UPSERTS 200 RECORDS AT A TIME, DELETES 25 (THE COMPOSITE API'S SUBREQUEST LIMIT)

                    int batchSize = isDelete ? 25 : 200;
                    List<string> salesforceErrors = new List<string>();
                    List<ReplicationRecordOutcome> salesforceOutcomes = new List<ReplicationRecordOutcome>();

                    for (int batchIndex = 0; batchIndex < salesforceUpsertResults.responses.Count; batchIndex++)
                    {

                        SalesforceAPIUpsertResponse response = salesforceUpsertResults.responses[batchIndex];
                        int firstRecordIndex = batchIndex * batchSize;
                        int lastRecordIndex = Math.Min(firstRecordIndex + batchSize, recordCount);

                        // A FAILED BATCH (AUTH, BAD OBJECT, HTTP ERROR) HAS NO RECORD RESULTS, ONLY AN ERROR CODE AND MESSAGE.
                        // EVERY RECORD IN IT FAILED.

                        string batchError = response.success ? "" : "Batch failed: " + (response.errorCode + " " + response.message).Trim();

                        if (batchError != "")
                        {
                            salesforceErrors.Add(batchError);
                        }

                        // A SUCCESSFUL BATCH CAN STILL CONTAIN INDIVIDUAL RECORD FAILURES. COLLECT THEIR MESSAGES FOR THE RULE'S SUMMARY,
                        // AND KEY DELETE FAILURES BY EXTERNAL ID FOR MATCHING BELOW.

                        Dictionary<string, string> recordErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (Dictionary<string, string> recordFailure in response.recordResultsFailures)
                        {

                            // A RECORD THAT'S ALREADY GONE FROM SALESFORCE COUNTS AS DELETED, SO A RETRY OR A SECOND DELETE ISN'T A FAILURE

                            string failureCode = recordFailure.ContainsKey("errorCode") ? recordFailure["errorCode"] : "";

                            if (isDelete && (failureCode == "NOT_FOUND" || failureCode == "ENTITY_IS_DELETED"))
                            {
                                continue;
                            }

                            string failedExternalID = recordFailure.ContainsKey("externalID") ? recordFailure["externalID"] : (recordFailure.ContainsKey("id") ? recordFailure["id"] : "");
                            string failureMessage = recordFailure.ContainsKey("errorMessage") ? recordFailure["errorMessage"] : "";

                            recordErrors[failedExternalID ?? ""] = failureMessage;
                            salesforceErrors.Add("Record '" + failedExternalID + "' failed: " + failureMessage);

                        }

                        for (int i = firstRecordIndex; i < lastRecordIndex; i++)
                        {

                            string recordError = batchError;

                            if (recordError == "")
                            {
                                if (isDelete)
                                {

                                    // DELETE RESULTS ARE KEYED BY EXTERNAL ID (RECORDS WITHOUT ONE COME BACK LAST), AND DeleteRecordBatch
                                    // READS THE ID WITH THE SAME CASE-INSENSITIVE, TRIMMED LOOKUP AS GetRecordID

                                    string deleteError;

                                    if (recordErrors.TryGetValue(GetRecordID(remappedReplicationRecords[i], replicationRule.Option02), out deleteError))
                                    {
                                        recordError = deleteError != "" ? deleteError : "Delete failed.";
                                    }

                                }
                                else
                                {

                                    // UPSERT RESULTS COME BACK IN THE ORDER SENT, SO MATCH BY POSITION IN THE BATCH, NOT BY EXTERNAL ID,
                                    // WHICH UpsertRecordBatch READS CASE-SENSITIVELY AND UNTRIMMED

                                    int resultIndex = i - firstRecordIndex;

                                    if (resultIndex >= response.recordResults.Count)
                                    {
                                        recordError = "Salesforce returned no result for this record.";
                                    }
                                    else if (response.recordResults[resultIndex].ContainsKey("success") && response.recordResults[resultIndex]["success"] == "False")
                                    {
                                        string upsertError = response.recordResults[resultIndex].ContainsKey("errorMessage") ? response.recordResults[resultIndex]["errorMessage"] : "";
                                        recordError = upsertError != "" ? upsertError : "Upsert failed.";
                                    }

                                }
                            }

                            ReplicationRecordOutcome salesforceOutcome = this.BuildOutcome(replicationRule, remappedReplicationRecords[i], recordError != "" ? ReplicationRecordOutcome.StatusFailed : ReplicationRecordOutcome.StatusSucceeded, recordError, logEveryRecord, transformWarnings);

                            if (salesforceOutcome != null)
                            {
                                salesforceOutcomes.Add(salesforceOutcome);
                            }

                        }

                    }

                    ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, recordCount, salesforceErrors.Count == 0, string.Join(" | ", salesforceErrors));
                    ruleResult.RecordOutcomes = salesforceOutcomes;
                    return ruleResult;

                default:

                    string unknownTargetMessage = "Unknown target type '" + replicationRule.TargetType + "'.";
                    ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, recordCount, false, unknownTargetMessage);
                    ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, remappedReplicationRecords, ReplicationRecordOutcome.StatusFailed, unknownTargetMessage, logEveryRecord, transformWarnings);
                    return ruleResult;

            }

        }



        private List<ReplicationRecordOutcome> BuildOutcomes(ReplicationRule replicationRule, List<Dictionary<string, object>> records, string status, string errorMessage, bool logEveryRecord, Dictionary<Dictionary<string, object>, string> transformWarnings = null)
        {
            return records.Select(record => this.BuildOutcome(replicationRule, record, status, errorMessage, logEveryRecord, transformWarnings)).Where(outcome => outcome != null).ToList();
        }



        // RETURNS NULL FOR A RECORD THAT WON'T BE LOGGED (A SUCCEEDED OR SKIPPED RECORD IN A RUN THAT ONLY LOGS FAILURES), SO
        // LARGE BACKGROUND RUNS DON'T SERIALIZE AND HOLD A PAYLOAD FOR EVERY RECORD

        private ReplicationRecordOutcome BuildOutcome(ReplicationRule replicationRule, Dictionary<string, object> record, string status, string errorMessage, bool logEveryRecord, Dictionary<Dictionary<string, object>, string> transformWarnings = null)
        {

            string warning;

            if (transformWarnings == null || !transformWarnings.TryGetValue(record, out warning))
            {
                warning = "";
            }

            string recordID = GetRecordID(record, replicationRule.Option02);

            // IN A RUN THAT ONLY LOGS FAILURES, THESE ARE STILL LOGGED:
            //   - A RECORD WITH A WARNING, SO THE PROBLEM IS VISIBLE
            //   - A RECORD THAT HAS A FAILED ROW WAITING FOR A RETRY, SO THE RETRY TASK SEES THIS NEWER REPLICATION OF IT

            if (!logEveryRecord && status != ReplicationRecordOutcome.StatusFailed && warning == "" && !this.IsAwaitingRetry(replicationRule.RuleNum, recordID))
            {
                return null;
            }

            ReplicationRecordOutcome outcome = new ReplicationRecordOutcome(
                recordID: recordID,
                payload: JsonConvert.SerializeObject(record),
                status: status,
                errorMessage: errorMessage
            );

            outcome.Warning = warning;
            return outcome;

        }



        // THE SALESFORCE CREDENTIAL'S Domain, AS THE MY DOMAIN NAME SalesforceAPICredential EXPECTS, OR NULL WHEN BLANK. A MY DOMAIN
        // NAME IS ONE LABEL ("mycompany"), OR A LABEL AND A PARTITION ("mycompany--sandboxname.sandbox" FOR A SANDBOX,
        // "mycompany-dev-ed.develop" FOR A DEVELOPER EDITION ORG). A PASTED URL OR HOST NAME THAT CONTAINS THE NAME, SUCH AS
        // "https://mycompany.my.salesforce.com/" OR "mycompany.lightning.force.com", IS CUT DOWN TO IT.
        //
        // ANYTHING ELSE IS REJECTED HERE WITH A MESSAGE THAT SAYS WHAT TO ENTER, BECAUSE THE NAME BECOMES PART OF THE HOST THE
        // CLIENT SECRET IS SENT TO, AND BECAUSE A HOST SUCH AS "test.salesforce.com" WOULD OTHERWISE ONLY FAIL LATER, AS A
        // CONNECTION ERROR TO A HOST THAT DOESN'T EXIST.

        private static string NormalizeSalesforceDomain(string domain)
        {

            string trimmed = (domain ?? "").Trim();
            int schemeIndex = trimmed.IndexOf("://", StringComparison.Ordinal);

            if (schemeIndex >= 0)
            {
                trimmed = trimmed.Substring(schemeIndex + 3);
            }

            int pathIndex = trimmed.IndexOf('/');

            if (pathIndex >= 0)
            {
                trimmed = trimmed.Substring(0, pathIndex);
            }

            foreach (string hostSuffix in new string[] { ".my.salesforce.com", ".lightning.force.com" })
            {
                if (trimmed.EndsWith(hostSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - hostSuffix.Length);
                    break;
                }
            }

            if (trimmed == "")
            {
                return null;
            }

            // ONE OR TWO LABELS OF LETTERS, DIGITS AND HYPHENS. A SECOND LABEL OF "com" MEANS A HOST NAME WAS ENTERED.

            string[] labels = trimmed.Split('.');

            bool isMyDomainName = labels.Length <= 2
                && labels.All(label => label != "" && label.All(character => (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9') || character == '-'))
                && !(labels.Length == 2 && string.Equals(labels[1], "com", StringComparison.OrdinalIgnoreCase));

            if (!isMyDomainName)
            {
                throw new ArgumentException("The Salesforce credential's Domain '" + trimmed + "' isn't a My Domain name. Enter only the name, for example 'mycompany', or 'mycompany--sandboxname.sandbox' for a sandbox. Don't enter a sign-in host such as test.salesforce.com.");
            }

            return trimmed;

        }



        // THE RECORD'S VALUE FOR THE RULE'S ID FIELD (Option02 FOR BOTH TARGETS), MATCHED WITHOUT REGARD TO CASE. BLANK WHEN THE
        // RULE HAS NO ID FIELD OR THE RECORD DOESN'T MAP IT.

        private static string GetRecordID(Dictionary<string, object> record, string idFieldName)
        {

            if (!string.IsNullOrWhiteSpace(idFieldName))
            {
                foreach (KeyValuePair<string, object> field in record)
                {
                    if (string.Equals(field.Key, idFieldName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return (Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "").Trim();
                    }
                }
            }

            return "";

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     WriteReplicationLog
        /* Purpose:  Writes the outcomes to ue_AIR_IDOReplicationLogs. Trigger runs log every record. Background
        /*           task runs log every record when the run loaded LogAllRecordsLimit records or fewer, and only
        /*           failed records above that. A rule that failed before its records were remapped gets one row
        /*           with no payload. Never throws: a logging problem must not fail the user's save.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        private const int LogAllRecordsLimit = 100;

        private const int MaxPayloadLength = 4000;

        private const int MaxErrorMessageLength = 2000;

        private const int MaxRecordIDLength = 255;

        private const int DefaultRetries = 3;

        // THE MOST FAILED ROWS LOOKED AT WHEN A RUN CHECKS WHICH RECORDS ARE WAITING FOR A RETRY

        private const int AwaitingRetryRowLimit = 10000;

        // RECORD IDS THAT HAVE A FAILED ROW WAITING FOR A RETRY, BY RULE NUMBER. ONLY FILLED IN FOR A RUN THAT DOESN'T LOG EVERY RECORD.
        // AN EXTENSION CLASS OBJECT SERVES ONE REQUEST, SO THIS BELONGS TO THE RUN THAT SET IT.

        private Dictionary<string, HashSet<string>> recordIDsAwaitingRetry = new Dictionary<string, HashSet<string>>();



        private bool IsAwaitingRetry(string ruleNum, string recordID)
        {

            if (string.IsNullOrEmpty(recordID) || this.recordIDsAwaitingRetry.Count == 0)
            {
                return false;
            }

            // THE LOG STORES AT MOST MaxRecordIDLength CHARACTERS OF A RECORD ID, SO COMPARE THE SAME LENGTH

            if (recordID.Length > MaxRecordIDLength)
            {
                recordID = recordID.Substring(0, MaxRecordIDLength);
            }

            HashSet<string> recordIDs;
            return this.recordIDsAwaitingRetry.TryGetValue(ruleNum ?? "", out recordIDs) && recordIDs.Contains(recordID);

        }



        // LOADS THE RECORD IDS OF THE FAILED ROWS THAT ARE WAITING FOR A RETRY, FOR THE GIVEN RULES OF ONE IDO. NEVER THROWS: IF THE LOG CAN'T
        // BE READ, THE RUN CARRIES ON AND LOGS ONLY ITS FAILURES.

        private Dictionary<string, HashSet<string>> LoadRecordIDsAwaitingRetry(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, IEnumerable<string> ruleNums)
        {

            Dictionary<string, HashSet<string>> awaitingRetry = new Dictionary<string, HashSet<string>>();

            try
            {

                List<string> ruleNumList = (ruleNums ?? Enumerable.Empty<string>()).Where(ruleNum => !string.IsNullOrEmpty(ruleNum)).ToList();

                if (ruleNumList.Count == 0)
                {
                    return awaitingRetry;
                }

                LoadRecordsResponseData awaitingResponse = sytelineAPI.LoadRecords(
                    IDOName: "ue_AIR_IDOReplicationLogs",
                    properties: new List<string>() { "RuleNum", "RecordID" },
                    filter: "Status = 'Failed' AND RetriesRemaining > 0 AND RecordID IS NOT NULL AND IDOName = " + sytelineAPI.QuoteFilterValue(IDOName) + " AND " + sytelineAPI.BuildInFilter("RuleNum", ruleNumList),
                    orderBy: "CreateDate DESC",
                    recordCap: AwaitingRetryRowLimit
                );

                foreach (IDOItem awaitingItem in awaitingResponse.Items)
                {

                    string awaitingRuleNum = awaitingItem.PropertyValues[awaitingResponse.PropertyKeys["RuleNum"]].Value ?? "";
                    string awaitingRecordID = awaitingItem.PropertyValues[awaitingResponse.PropertyKeys["RecordID"]].Value ?? "";

                    if (awaitingRecordID == "")
                    {
                        continue;
                    }

                    HashSet<string> recordIDs;

                    if (!awaitingRetry.TryGetValue(awaitingRuleNum, out recordIDs))
                    {
                        recordIDs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        awaitingRetry[awaitingRuleNum] = recordIDs;
                    }

                    recordIDs.Add(awaitingRecordID);

                }

            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Couldn't check the replication log for records waiting for a retry: " + this.DescribeException(ex));
            }

            return awaitingRetry;

        }



        private void WriteReplicationLog(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, string operation, string source, List<ReplicationRuleResult> ruleResults)
        {

            try
            {

                List<IDOUpdateItem> logRows = new List<IDOUpdateItem>();
                DateTime attemptDate = DateTime.Now;

                foreach (ReplicationRuleResult ruleResult in ruleResults)
                {

                    if (ruleResult.RecordOutcomes.Count == 0)
                    {

                        if (!ruleResult.Success)
                        {
                            logRows.Add(this.BuildLogRow(sytelineAPI, ruleResult, IDOName, operation, source, new ReplicationRecordOutcome("", null, ReplicationRecordOutcome.StatusFailed, ruleResult.ErrorMessage), attemptDate));
                        }

                        continue;

                    }

                    // BuildOutcome ALREADY LEFT OUT THE RECORDS A RUN DOESN'T LOG, SO EVERY OUTCOME HERE GETS A ROW

                    foreach (ReplicationRecordOutcome outcome in ruleResult.RecordOutcomes)
                    {
                        logRows.Add(this.BuildLogRow(sytelineAPI, ruleResult, IDOName, operation, source, outcome, attemptDate));
                    }

                }

                // EACH GROUP OF 200 IS INSERTED ON ITS OWN, SO ONE BAD ROW CAN'T STOP THE GROUPS AFTER IT

                for (int i = 0; i < logRows.Count; i += 200)
                {
                    try
                    {
                        sytelineAPI.CreateRecords("ue_AIR_IDOReplicationLogs", logRows.Skip(i).Take(200).ToList());
                    }
                    catch (Exception ex)
                    {
                        utils.WriteLogMessage("Couldn't write replication log rows " + (i + 1) + " to " + Math.Min(i + 200, logRows.Count) + ": " + this.DescribeException(ex));
                    }
                }

            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Couldn't write the replication log: " + this.DescribeException(ex));
            }

        }



        // THE CREDENTIAL NAME WRITTEN TO A LOG ROW. THE ROW KEEPS ITS OWN COPY, A SNAPSHOT, SO IT STILL SAYS WHICH CREDENTIAL WAS USED
        // AFTER THE RULE OR THE CREDENTIAL IS CHANGED OR DELETED. THE COLUMN IS REQUIRED, SO A RULE WITH NO CREDENTIAL, OR A FAILURE
        // BEFORE THE RULE WAS LOADED, IS WRITTEN AS NoCredentialName AND NOT LEFT BLANK, WHICH WOULD FAIL THE INSERT.

        private const string NoCredentialName = "(none)";

        private static string LoggedCredentialName(string credentialName)
        {
            return string.IsNullOrWhiteSpace(credentialName) ? NoCredentialName : credentialName.Trim();
        }



        private IDOUpdateItem BuildLogRow(SytelineInternalAPI sytelineAPI, ReplicationRuleResult ruleResult, string IDOName, string operation, string source, ReplicationRecordOutcome outcome, DateTime attemptDate)
        {

            string payload = outcome.Payload;
            string errorMessage = outcome.ErrorMessage ?? "";

            if (!string.IsNullOrEmpty(outcome.Warning))
            {
                errorMessage = (errorMessage + " Warning: " + outcome.Warning).Trim();
            }

            // A PAYLOAD OVER THE COLUMN'S 4,000 CHARACTERS ISN'T STORED, BECAUSE A CUT-OFF PAYLOAD COULDN'T BE RESENT

            if (payload != null && payload.Length > MaxPayloadLength)
            {
                payload = null;
                errorMessage = (errorMessage + " The payload is over 4,000 characters, so it wasn't stored and can't be retried.").Trim();
            }

            string recordID = outcome.RecordID ?? "";

            if (recordID.Length > MaxRecordIDLength)
            {
                recordID = recordID.Substring(0, MaxRecordIDLength);
            }

            if (errorMessage.Length > MaxErrorMessageLength)
            {
                errorMessage = errorMessage.Substring(0, MaxErrorMessageLength);
            }

            // ONLY A FAILED RECORD WITH A STORED PAYLOAD CAN BE RETRIED

            bool canRetry = outcome.Status == ReplicationRecordOutcome.StatusFailed && payload != null;
            int ruleNumber;

            return sytelineAPI.BuildInsertItem(new Dictionary<string, object>()
            {
                { "RuleNum", int.TryParse(ruleResult.RuleNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out ruleNumber) ? (object)ruleNumber : null },
                { "IDOName", IDOName },
                { "TargetType", ruleResult.TargetType != "" ? ruleResult.TargetType : null },
                { "CredentialName", LoggedCredentialName(ruleResult.CredentialName) },
                { "Source", source },
                { "Operation", operation },
                { "RecordID", recordID != "" ? recordID : null },
                { "Payload", payload },
                { "Status", outcome.Status },
                { "ErrorMessage", errorMessage != "" ? errorMessage : null },
                { "RetriesRemaining", canRetry ? DefaultRetries : 0 },
                { "AttemptCount", 1 },
                { "LastAttemptDate", attemptDate }
            });

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_RetryFailedReplications
        /* Purpose:  Background task method. Resends failed records from the replication log
        /*           (ue_AIR_IDOReplicationLogs) using each row's stored payload, so records that SyteLine has since
        /*           deleted can still be retried. Then purges Succeeded and Skipped rows older than purgeAfterDays
        /*           (0 turns the purge off). Never throws.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        private const int RetryRowLimit = 500;

        private const int PurgeBatchSize = 500;

        private const int PurgeMaxBatches = 20;

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_RetryFailedReplications(int BGTaskNum = -1, int purgeAfterDays = 90)
        {

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands,
                BGTaskNum: BGTaskNum
            );

            try
            {
                this.RetryFailedReplications(sytelineAPI, utils);
            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Retrying failed replications stopped: " + this.DescribeException(ex));
            }

            try
            {
                this.PurgeReplicationLog(sytelineAPI, utils, purgeAfterDays);
            }
            catch (Exception ex)
            {
                utils.WriteLogMessage("Purging the replication log stopped: " + this.DescribeException(ex));
            }

            return 0;

        }



        private void RetryFailedReplications(SytelineInternalAPI sytelineAPI, Utilities utils)
        {

            // ONLY ROWS FOR CURRENTLY ACTIVE RULES ARE LOADED, SO ROWS LEFT FOR INACTIVE OR DELETED RULES CAN'T FILL THE
            // RetryRowLimit WINDOW AND STOP ACTIVE RULES FROM BEING RETRIED

            LoadRecordsResponseData activeRulesResponse = sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_IDOReplicationRules",
                properties: new List<string>() { "RuleNum" },
                filter: "IsActive = 1",
                orderBy: "RuleNum ASC",
                recordCap: 10000
            );

            List<string> activeRuleNums = activeRulesResponse.Items
                .Select(item => item.PropertyValues[activeRulesResponse.PropertyKeys["RuleNum"]].Value ?? "")
                .Where(ruleNum => ruleNum != "")
                .ToList();

            if (activeRuleNums.Count == 0)
            {
                utils.WriteLogMessage("There are no active replication rules, so nothing was retried.");
                return;
            }

            // OLDEST FIRST, AT MOST RetryRowLimit ROWS PER RUN. ROWS WITHOUT A PAYLOAD CAN'T BE RESENT, AND ARE WRITTEN WITH NO RETRIES.

            LoadRecordsResponseData logResponse = sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_IDOReplicationLogs",
                properties: new List<string>() { "RowPointer", "RuleNum", "IDOName", "Operation", "RecordID", "Payload", "RetriesRemaining", "AttemptCount" },
                filter: "Status = 'Failed' AND RetriesRemaining > 0 AND Payload IS NOT NULL AND " + sytelineAPI.BuildInFilter("RuleNum", activeRuleNums),
                orderBy: "CreateDate ASC",
                recordCap: RetryRowLimit
            );

            utils.WriteLogMessage("Failed replication log rows to retry: " + logResponse.Items.Count + ".");

            string ReadValue(IDOItem item, string propertyName)
            {
                IDOPropertyValue propertyValue = item.PropertyValues[logResponse.PropertyKeys[propertyName]];
                return propertyValue.IsNull ? "" : (propertyValue.Value ?? "");
            }

            int ReadInt(IDOItem item, string propertyName)
            {
                int parsed;
                return int.TryParse(ReadValue(item, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
            }

            var logRows = logResponse.Items.Select(item => new
            {
                Item = item,
                RuleNum = ReadValue(item, "RuleNum"),
                IDOName = ReadValue(item, "IDOName"),
                Operation = NormalizeOperation(ReadValue(item, "Operation")),
                RecordID = ReadValue(item, "RecordID"),
                RowPointer = ReadValue(item, "RowPointer"),
                Payload = ReadValue(item, "Payload"),
                RetriesRemaining = ReadInt(item, "RetriesRemaining"),
                AttemptCount = ReadInt(item, "AttemptCount")
            }).ToList();

            // A ROW WITH NO RECORD ID CAN'T BE CHECKED THIS WAY, BECAUSE NOTHING SAYS WHICH RECORD IT IS. IT IS STILL RETRIED, UP TO ITS
            // RETRIES, SO A RULE WITH NO ID FIELD (Option02) CAN HAVE OLDER DATA RESENT AFTER NEWER DATA.
            //
            // A FAILED ROW IS SUPERSEDED WHEN THE SAME RECORD WAS REPLICATED AGAIN BY THE SAME RULE LATER (WHATEVER THAT ROW'S
            // STATUS). RESENDING IT WOULD OVERWRITE NEWER DATA, OR RE-CREATE A RECORD THAT WAS DELETED SINCE. SUPERSEDED ROWS ARE
            // MARKED SKIPPED, WHICH ALSO COLLAPSES SEVERAL FAILED ROWS FOR ONE RECORD DOWN TO THE NEWEST.

            HashSet<string> supersededRowPointers = this.FindSupersededLogRows(
                sytelineAPI,
                logRows.Where(logRow => logRow.RecordID != "").Select(logRow => new KeyValuePair<string, string>(logRow.IDOName + "|" + logRow.RuleNum, logRow.RecordID)).ToList(),
                logRows.Where(logRow => logRow.RecordID != "").Select(logRow => logRow.RowPointer).ToList()
            );

            if (supersededRowPointers.Count > 0)
            {

                List<IDOUpdateItem> supersededUpdates = logRows
                    .Where(logRow => supersededRowPointers.Contains(logRow.RowPointer))
                    .Select(logRow => sytelineAPI.BuildUpdateItem(logRow.Item.ItemID, new List<IDOUpdateProperty>()
                    {
                        new IDOUpdateProperty("Status", ReplicationRecordOutcome.StatusSkipped, true),
                        new IDOUpdateProperty("ErrorMessage", "Superseded by a newer replication of this record, so it wasn't resent.", true),
                        new IDOUpdateProperty("RetriesRemaining", 0, true)
                    }))
                    .ToList();

                for (int i = 0; i < supersededUpdates.Count; i += 200)
                {
                    try
                    {
                        sytelineAPI.UpdateRecords("ue_AIR_IDOReplicationLogs", supersededUpdates.Skip(i).Take(200).ToList());
                    }
                    catch (Exception ex)
                    {
                        utils.WriteLogMessage("Couldn't mark superseded replication log rows: " + this.DescribeException(ex));
                    }
                }

                utils.WriteLogMessage("Skipped " + supersededUpdates.Count + " failed row(s) superseded by a newer replication of the same record.");

                logRows = logRows.Where(logRow => !supersededRowPointers.Contains(logRow.RowPointer)).ToList();

            }

            // EACH GROUP SHARES A RULE AND AN OPERATION, SO IT CAN BE SENT TOGETHER

            foreach (var group in logRows.GroupBy(logRow => new { logRow.IDOName, logRow.RuleNum, logRow.Operation }))
            {

                DateTime attemptDate = DateTime.Now;
                List<IDOUpdateItem> logUpdates = new List<IDOUpdateItem>();

                // ONLY ACTIVE RULES ARE RETRIED. ROWS FOR A RULE THAT'S INACTIVE OR GONE ARE LEFT AS THEY ARE.

                ReplicationRule replicationRule = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, group.Key.IDOName, group.Key.RuleNum).Values.FirstOrDefault();

                if (replicationRule == null)
                {
                    utils.WriteLogMessage("Rule '" + group.Key.RuleNum + "' for IDO '" + group.Key.IDOName + "' isn't active, so its " + group.Count() + " failed record(s) weren't retried.");
                    continue;
                }

                // READ THE STORED PAYLOADS. A PAYLOAD THAT CAN'T BE READ IS MARKED OUT OF RETRIES.

                var sendableRows = group.Take(0).Select(emptyRow => new { Row = emptyRow, Record = (Dictionary<string, object>)null }).ToList();

                foreach (var groupRow in group)
                {
                    Dictionary<string, object> record = null;

                    try
                    {
                        record = JsonConvert.DeserializeObject<Dictionary<string, object>>(groupRow.Payload);
                    }
                    catch (JsonException)
                    {
                        record = null;
                    }

                    if (record == null)
                    {
                        logUpdates.Add(this.BuildLogRetryUpdate(sytelineAPI, groupRow.Item.ItemID, ReplicationRecordOutcome.StatusFailed, "The stored payload couldn't be read, so it can't be retried.", 0, groupRow.AttemptCount, attemptDate));
                    }
                    else
                    {
                        sendableRows.Add(new { Row = groupRow, Record = record });
                    }
                }

                if (sendableRows.Count > 0)
                {

                    List<Dictionary<string, object>> records = sendableRows.Select(sendable => sendable.Record).ToList();
                    ReplicationRuleResult ruleResult;

                    try
                    {
                        ruleResult = this.SendRule(replicationRule, records, group.Key.Operation, true, utils);
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = this.DescribeException(ex);
                        ruleResult = new ReplicationRuleResult(replicationRule.RuleNum, replicationRule.TargetType, records.Count, false, errorMessage);
                        ruleResult.RecordOutcomes = this.BuildOutcomes(replicationRule, records, ReplicationRecordOutcome.StatusFailed, errorMessage, true);
                    }

                    // WITH logEveryRecord TRUE, SendRule RETURNS ONE OUTCOME PER RECORD, IN ORDER

                    int countSucceeded = 0;

                    for (int i = 0; i < sendableRows.Count; i++)
                    {

                        var retriedRow = sendableRows[i].Row;
                        ReplicationRecordOutcome outcome = i < ruleResult.RecordOutcomes.Count
                            ? ruleResult.RecordOutcomes[i]
                            : new ReplicationRecordOutcome("", null, ReplicationRecordOutcome.StatusFailed, "The retry returned no result for this record.");

                        bool stillFailed = outcome.Status == ReplicationRecordOutcome.StatusFailed;

                        if (!stillFailed)
                        {
                            countSucceeded++;
                        }

                        logUpdates.Add(this.BuildLogRetryUpdate(sytelineAPI, retriedRow.Item.ItemID, outcome.Status, outcome.ErrorMessage, stillFailed ? Math.Max(retriedRow.RetriesRemaining - 1, 0) : 0, retriedRow.AttemptCount, attemptDate, LoggedCredentialName(replicationRule.CredentialName)));

                    }

                    utils.WriteLogMessage("Retried " + sendableRows.Count + " record(s) for rule '" + replicationRule.RuleNum + "' (" + replicationRule.TargetType + (group.Key.Operation != "" ? ", " + group.Key.Operation : "") + "): " + countSucceeded + " no longer failed.");

                }

                // EACH GROUP OF 200 UPDATES IS SAVED ON ITS OWN, SO ONE BAD ROW CAN'T STOP THE REST

                for (int i = 0; i < logUpdates.Count; i += 200)
                {
                    try
                    {
                        sytelineAPI.UpdateRecords("ue_AIR_IDOReplicationLogs", logUpdates.Skip(i).Take(200).ToList());
                    }
                    catch (Exception ex)
                    {
                        utils.WriteLogMessage("Couldn't update replication log rows after the retry: " + this.DescribeException(ex));
                    }
                }

            }

        }



        // RETURNS THE ROWPOINTERS OF CANDIDATE ROWS THAT AREN'T THE NEWEST LOG ROW FOR THEIR RECORD. candidateKeys PAIRS
        // "IDOName|RuleNum" WITH THE RECORD ID. ALL LOG ROWS FOR THOSE RECORDS ARE LOADED NEWEST FIRST, SO THE FIRST ROW SEEN FOR
        // EACH RECORD IS THE NEWEST, WITHOUT HAVING TO PARSE DATES.

        private HashSet<string> FindSupersededLogRows(SytelineInternalAPI sytelineAPI, List<KeyValuePair<string, string>> candidateKeys, List<string> candidateRowPointers)
        {

            HashSet<string> superseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> newestRowPointerByRecord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ruleGroup in candidateKeys.GroupBy(candidateKey => candidateKey.Key))
            {

                string[] keyParts = ruleGroup.Key.Split('|');
                List<string> recordIDs = ruleGroup.Select(candidateKey => candidateKey.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                for (int i = 0; i < recordIDs.Count; i += 200)
                {

                    LoadRecordsResponseData historyResponse = sytelineAPI.LoadRecords(
                        IDOName: "ue_AIR_IDOReplicationLogs",
                        properties: new List<string>() { "RowPointer", "RecordID" },
                        filter: "IDOName = " + sytelineAPI.QuoteFilterValue(keyParts[0]) + " AND RuleNum = " + sytelineAPI.QuoteFilterValue(keyParts[1]) + " AND " + sytelineAPI.BuildInFilter("RecordID", recordIDs.Skip(i).Take(200)),
                        orderBy: "CreateDate DESC",
                        recordCap: 100000
                    );

                    foreach (IDOItem historyItem in historyResponse.Items)
                    {
                        string recordKey = ruleGroup.Key + "|" + (historyItem.PropertyValues[historyResponse.PropertyKeys["RecordID"]].Value ?? "");

                        if (!newestRowPointerByRecord.ContainsKey(recordKey))
                        {
                            newestRowPointerByRecord[recordKey] = historyItem.PropertyValues[historyResponse.PropertyKeys["RowPointer"]].Value ?? "";
                        }
                    }

                }

            }

            for (int i = 0; i < candidateKeys.Count; i++)
            {
                string newestRowPointer;

                if (newestRowPointerByRecord.TryGetValue(candidateKeys[i].Key + "|" + candidateKeys[i].Value, out newestRowPointer) && !string.Equals(newestRowPointer, candidateRowPointers[i], StringComparison.OrdinalIgnoreCase))
                {
                    superseded.Add(candidateRowPointers[i]);
                }
            }

            return superseded;

        }



        // credentialName IS THE CREDENTIAL THE RETRY USED. NULL LEAVES THE ROW'S CREDENTIAL NAME AS IT IS, FOR A ROW THAT WASN'T RESENT.

        private IDOUpdateItem BuildLogRetryUpdate(SytelineInternalAPI sytelineAPI, string itemID, string status, string errorMessage, int retriesRemaining, int previousAttemptCount, DateTime attemptDate, string credentialName = null)
        {

            errorMessage = errorMessage ?? "";

            if (errorMessage.Length > MaxErrorMessageLength)
            {
                errorMessage = errorMessage.Substring(0, MaxErrorMessageLength);
            }

            List<IDOUpdateProperty> propertyUpdates = new List<IDOUpdateProperty>()
            {
                new IDOUpdateProperty("Status", status, true),
                new IDOUpdateProperty("ErrorMessage", errorMessage, true),
                new IDOUpdateProperty("RetriesRemaining", retriesRemaining, true),
                new IDOUpdateProperty("AttemptCount", previousAttemptCount + 1, true),
                new IDOUpdateProperty("LastAttemptDate", attemptDate, true)
            };

            if (credentialName != null)
            {
                propertyUpdates.Add(new IDOUpdateProperty("CredentialName", credentialName, true));
            }

            return sytelineAPI.BuildUpdateItem(itemID, propertyUpdates);

        }



        private void PurgeReplicationLog(SytelineInternalAPI sytelineAPI, Utilities utils, int purgeAfterDays)
        {

            if (purgeAfterDays <= 0)
            {
                return;
            }

            // SUCCEEDED AND SKIPPED ROWS OLDER THAN THE CUTOFF ARE DELETED. FAILED ROWS ARE KEPT, FOR INVESTIGATION.

            // yyyyMMdd IS READ THE SAME WAY UNDER EVERY SQL SERVER DATEFORMAT. yyyy-MM-dd ISN'T: DMY LOGINS READ IT AS YEAR-DAY-MONTH.

            DateTime cutoff = DateTime.Now.AddDays(-purgeAfterDays);
            string cutoffDate = cutoff.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            int countPurged = 0;

            for (int batch = 0; batch < PurgeMaxBatches; batch++)
            {

                LoadRecordsResponseData purgeResponse = sytelineAPI.LoadRecords(
                    IDOName: "ue_AIR_IDOReplicationLogs",
                    properties: new List<string>() { "RowPointer" },
                    filter: "( Status = 'Succeeded' OR Status = 'Skipped' ) AND CreateDate < '" + cutoffDate + "'",
                    orderBy: "CreateDate ASC",
                    recordCap: PurgeBatchSize
                );

                if (purgeResponse.Items.Count == 0)
                {
                    break;
                }

                sytelineAPI.DeleteRecords(
                    IDOName: "ue_AIR_IDOReplicationLogs",
                    records: purgeResponse.Items.Select(item => sytelineAPI.BuildDeleteItem(item.ItemID)).ToList()
                );

                countPurged += purgeResponse.Items.Count;

                if (purgeResponse.Items.Count < PurgeBatchSize)
                {
                    break;
                }

            }

            utils.WriteLogMessage("Purged " + countPurged + " succeeded or skipped replication log row(s) created before " + cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");

        }



        // RETURNS Insert, Update OR Delete FOR AN OPERATION PASSED IN OR READ FROM A LOG ROW, AND Upsert FOR ANYTHING ELSE, BLANK INCLUDED

        private static string NormalizeOperation(string operation)
        {

            switch ((operation ?? "").Trim().ToLowerInvariant())
            {
                case "insert":
                    return OperationInsert;
                case "update":
                    return OperationUpdate;
                case "delete":
                    return OperationDelete;
                default:
                    return OperationUpsert;
            }

        }



        // THE SOURCE OF A TRIGGER RUN, FROM THE OPERATION ITS TRIGGER PASSED. EACH TRIGGER PASSES THE OPERATION OF ITS OWN EVENT.

        private static string SourceForEvent(string operation)
        {

            switch (operation)
            {
                case OperationInsert:
                    return SourceEventAfterInsert;
                case OperationUpdate:
                    return SourceEventAfterUpdate;
                case OperationDelete:
                    return SourceEventBeforeDelete;
                default:
                    return SourceEventUnknown;
            }

        }



        private string DescribeException(Exception ex)
        {

            Exception baseException = ex.GetBaseException();

            return baseException.GetType().Name + ": " + baseException.Message;

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_LoadOutputPreview
        /* Date:     2026-16-14
        /* Authors:  Andy Mercer
        /* Purpose:  This CLM loads a preview of the output that the rule and fields have configured, to allow the
        /*           end user to validate the field mapping and transformations before enabling.
        /*
        /* Free to use under MIT License, Copyright (c) 2025 FDI Information Systems. See full license at
        /* https://github.com/functionaldevices-cis/syteline-custom-load-method-examples?tab=MIT-1-ov-file#readme
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.CustomLoad)]
        public DataTable ue_LoadOutputPreview(string ruleNum, string IDOName, string queryFilter)
        {

            /********************************************************************/
            /* SET UP HELPER VARIABLES
            /********************************************************************/

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands
            );



            /********************************************************************/
            /* CREATE EMPTY TABLE
            /********************************************************************/

            DataTable outputTable = new DataTable("FullTable");

            // ADD COLUMN STRUCTURE

            outputTable.Columns.Add("OutputPreviewField01", typeof(string));
            outputTable.Columns.Add("OutputPreviewField02", typeof(string));
            outputTable.Columns.Add("OutputPreviewField03", typeof(string));
            outputTable.Columns.Add("OutputPreviewField04", typeof(string));
            outputTable.Columns.Add("OutputPreviewField05", typeof(string));
            outputTable.Columns.Add("OutputPreviewField06", typeof(string));
            outputTable.Columns.Add("OutputPreviewField07", typeof(string));
            outputTable.Columns.Add("OutputPreviewField08", typeof(string));
            outputTable.Columns.Add("OutputPreviewField09", typeof(string));
            outputTable.Columns.Add("OutputPreviewField10", typeof(string));
            outputTable.Columns.Add("OutputPreviewField11", typeof(string));
            outputTable.Columns.Add("OutputPreviewField12", typeof(string));
            outputTable.Columns.Add("OutputPreviewField13", typeof(string));
            outputTable.Columns.Add("OutputPreviewField14", typeof(string));
            outputTable.Columns.Add("OutputPreviewField15", typeof(string));
            outputTable.Columns.Add("OutputPreviewField16", typeof(string));
            outputTable.Columns.Add("OutputPreviewField17", typeof(string));
            outputTable.Columns.Add("OutputPreviewField18", typeof(string));
            outputTable.Columns.Add("OutputPreviewField19", typeof(string));
            outputTable.Columns.Add("OutputPreviewField20", typeof(string));



            /********************************************************************/
            /* LOAD USER INPUT FROM THE REQUEST OBJECT AND PARAMETERS IF SET
            /********************************************************************/

            // THE PREVIEW SHOWS AT MOST 5 RECORDS AND DOESN'T PAGE, SO THE REQUEST'S BOOKMARK IS LEFT AS IT IS

            LoadRecordsRequestData userRequest = new LoadRecordsRequestData(
                contextRequest: this.Context.Request as LoadCollectionRequestData,
                filterOverride: queryFilter,
                recordCapOverride: "5"
            );

            // THE PREVIEW IS FOR ONE RULE. WITHOUT A RULE NUMBER THERE'S NOTHING TO SHOW.

            if (string.IsNullOrWhiteSpace(ruleNum))
            {
                return outputTable;
            }



            /********************************************************************/
            /* LOAD THE REPLICATION RULE, INCLUDING AN INACTIVE ONE, SO A RULE CAN BE CHECKED BEFORE IT'S ENABLED
            /********************************************************************/

            Dictionary<string, ReplicationRule> replicationRules = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum, includeInactive: true);

            // THE QUERY IS ALREADY FILTERED TO THIS RULE NUMBER, SO THERE'S AT MOST ONE RESULT

            ReplicationRule replicationRule = replicationRules.Values.FirstOrDefault();

            if (replicationRule == null)
            {
                return outputTable;
            }



            /********************************************************************/
            /* LOAD MATCHING RECORDS AND REMAP THEM WITH THE RULE'S FIELD MAP
            /********************************************************************/

            LoadRecordsResponseData replicationRecordsResponse = sytelineAPI.LoadRecords(
                IDOName: IDOName,
                properties: replicationRule.SourceIDOProperties,
                filter: userRequest.Filter,
                orderBy: "",
                recordCap: userRequest.RecordCap
            );

            List<Dictionary<string, object>> remappedReplicationRecords = this.RemapRecords(
                fields: replicationRule.MapFieldsList,
                recordsResponseData: replicationRecordsResponse
            );



            /********************************************************************/
            /* FILL IN THE DATA TABLE
            /********************************************************************/

            foreach (Dictionary<string, object> remappedRecord in remappedReplicationRecords)
            {

                int counter = 1;
                DataRow outputRow = outputTable.NewRow();

                // LOAD THE FIRST 20 PROPERTIES INTO THE PREVIEW TABLE (AND IGNORE THE REST IF THERE ARE MORE THAN 20)

                foreach (object item in remappedRecord.Values)
                {

                    outputRow["OutputPreviewField" + counter.ToString("00", CultureInfo.InvariantCulture)] = item != null ? (object)Convert.ToString(item, CultureInfo.InvariantCulture) : DBNull.Value;
                    counter++;

                    if (counter > 20)
                    {
                        break;
                    }

                }

                outputTable.Rows.Add(outputRow);

            }

            return outputTable;

        }



        private List<Dictionary<string, object>> RemapRecords(List<ReplicationField> fields, LoadRecordsResponseData recordsResponseData, Dictionary<Dictionary<string, object>, string> transformWarnings = null)
        {

            Dictionary<string, object> remappedRecord;
            List<Dictionary<string, object>> remappedRecords = new List<Dictionary<string, object>>();

            foreach (IDOItem record in recordsResponseData.Items)
            {

                remappedRecord = new Dictionary<string, object>();

                foreach (ReplicationField field in fields)
                {

                    object value;

                    if (field.SourceType == "IDOProperty")
                    {

                        IDOPropertyValue propertyValue = record.PropertyValues[recordsResponseData.PropertyKeys[field.SourceValue]];
                        value = propertyValue.IsNull ? null : propertyValue.GetValue<object>();

                    }
                    else
                    {

                        value = field.SourceValue;

                    }

                    // APPLY THE FIELD'S TRANSFORMATION (SourceTransformation), IF IT HAS ONE. NULLS STAY NULL. A VALUE THE
                    // TRANSFORMATION CAN'T CONVERT IS SENT UNCHANGED, AND THE WARNING IS KEPT FOR THE RECORD'S LOG ROW.

                    string transformWarning;
                    remappedRecord[field.OutputFieldName] = field.Transformation.Convert(value, out transformWarning);

                    if (transformWarning != "" && transformWarnings != null)
                    {
                        string fieldWarning = "Field '" + field.OutputFieldName + "': " + transformWarning + ".";
                        string existingWarnings;
                        transformWarnings[remappedRecord] = transformWarnings.TryGetValue(remappedRecord, out existingWarnings) ? existingWarnings + " " + fieldWarning : fieldWarning;
                    }

                }

                remappedRecords.Add(remappedRecord);

            }

            return remappedRecords;

        }



        private Dictionary<string, ReplicationRule> LoadReplicationRulesAndMaps(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, string ruleNum = null, bool includeInactive = false)
        {

            Dictionary<string, ReplicationRule> replicationRules = new Dictionary<string, ReplicationRule>();

            // LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO

            LoadRecordsResponseData replicationRuleRecordsResponse = sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_IDOReplicationRules",
                properties: new List<string>(){
                    { "RuleNum" },
                    { "TargetType" },
                    { "CredentialName" },
                    { "CredentialValue01" },
                    { "CredentialValue02" },
                    { "CredentialValue03" },
                    { "CredentialValue04" },
                    { "CredentialValue05" },
                    { "CredentialValue06" },
                    { "CredentialValue07" },
                    { "CredentialValue08" },
                    { "CredentialValue09" },
                    { "CredentialValue10" },
                    { "Option01" },
                    { "Option02" },
                    { "Option03" },
                    { "Option04" },
                    { "Option05" },
                    { "Option06" },
                    { "Option07" },
                    { "Option08" },
                    { "Option09" },
                    { "Option10" }
                },
                filter: "( IDOName = " + sytelineAPI.QuoteFilterValue(IDOName) + " )" + (includeInactive ? "" : " AND ( IsActive = 1 )") + (ruleNum != null ? " AND ( RuleNum = " + sytelineAPI.QuoteFilterValue(ruleNum) + " )" : ""),
                orderBy: "RuleNum ASC"
            );

            replicationRules = replicationRuleRecordsResponse.Items.ToDictionary(
                replicationRuleRecord => replicationRuleRecord.PropertyValues[replicationRuleRecordsResponse.PropertyKeys["RuleNum"]].Value,
                replicationRuleRecord => new ReplicationRule(replicationRuleRecord, replicationRuleRecordsResponse.PropertyKeys)
            );

            if (replicationRules.Keys.Count > 0)
            {

                LoadRecordsResponseData replicationMapFieldRecordsResponse = sytelineAPI.LoadRecords(
                    IDOName: "ue_AIR_IDOReplicationMapFields",
                    properties: new List<string>(){
                        { "RuleNum" },
                        { "FieldSeq" },
                        { "Name" },
                        { "SourceType" },
                        { "SourceValue" },
                        { "SourceTransformation" }
                    },
                    filter: "( " + sytelineAPI.BuildInFilter("RuleNum", replicationRules.Keys) + " )",
                    orderBy: "RuleNum ASC, FieldSeq ASC"
                );

                replicationMapFieldRecordsResponse.Items.ForEach(replicationMapFieldRecord =>
                {

                    string replicationMapFieldRecordRuleNum = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["RuleNum"]].Value;
                    string replicationMapFieldRecordFieldSeq = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["FieldSeq"]].Value;
                    string replicationMapFieldRecordName = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["Name"]].Value;
                    string replicationMapFieldRecordSourceType = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["SourceType"]].Value;
                    string replicationMapFieldRecordSourceValue = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["SourceValue"]].Value;
                    string replicationMapFieldRecordSourceTransformation = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["SourceTransformation"]].Value;

                    if ((replicationMapFieldRecordSourceValue != null && replicationMapFieldRecordSourceValue != "") || (replicationMapFieldRecordSourceType == "Literal"))
                    {

                        replicationRules[replicationMapFieldRecordRuleNum].MapFields[replicationMapFieldRecordFieldSeq] = new ReplicationField(
                            outputFieldName: replicationMapFieldRecordName,
                            sourceType: replicationMapFieldRecordSourceType,
                            sourceValue: replicationMapFieldRecordSourceValue,
                            transformationName: replicationMapFieldRecordSourceTransformation
                        );

                    }

                });

            }

            return replicationRules;

        }

    }

}