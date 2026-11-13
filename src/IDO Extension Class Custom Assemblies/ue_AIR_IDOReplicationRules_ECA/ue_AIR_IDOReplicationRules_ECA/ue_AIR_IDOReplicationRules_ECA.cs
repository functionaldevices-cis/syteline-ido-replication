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

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_ReplicateRecordToExternalSystems(int BGTaskNum = -1, string queryFilter = "", string IDOName = "", int recordCap = 1, string ruleNum = null, string operation = "")
        {

            // TRIGGERS PASS A BLANK RULE NUMBER TO REACH THE OPERATION PARAMETER. BLANK MEANS EVERY ACTIVE RULE FOR THE IDO.

            if (string.IsNullOrWhiteSpace(ruleNum))
            {
                ruleNum = null;
            }

            // THE OPERATION THAT CHANGED THE RECORD: Insert, Update OR Delete. BLANK (BACKGROUND TASKS, OR TRIGGERS SET
            // UP BEFORE THIS PARAMETER EXISTED) MEANS UNKNOWN, AND TARGETS TREAT THE RECORD AS AN INSERT OR UPDATE.

            operation = NormalizeOperation(operation);

            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands,
                BGTaskNum: BGTaskNum
            );

            List<ReplicationRuleResult> ruleResults = new List<ReplicationRuleResult>();

            // THIS METHOD RUNS INSIDE AES TRIGGERS ON THE SOURCE IDO, SO IT MUST NEVER THROW. AN UNHANDLED EXCEPTION
            // WOULD FAIL THE EVENT ACTION AND ROLL BACK THE USER'S SAVE. EVERY ERROR IS CAUGHT, RECORDED AND LOGGED,
            // AND THE METHOD ALWAYS RETURNS 0.

            try
            {

                // LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO

                Dictionary<string, ReplicationRule> replicationRules = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum);

                if (replicationRules.Keys.Count > 0)
                {

                    // LOAD THE REPLICATION RECORD

                    LoadRecordsResponseData replicationRecordsResponse = sytelineAPI.LoadRecords(
                        IDOName: IDOName,
                        properties: replicationRules.Values.Select(rule => rule.SourceIDOProperties).SelectMany(innerList => innerList).Distinct().ToList(),
                        filter: queryFilter,
                        orderBy: "",
                        recordCap: recordCap
                    );

                    utils.WriteLogMessage("Count records found: " + replicationRecordsResponse.Items.Count.ToString() + " records. (Record cap was " + recordCap.ToString() + ").");

                    // STEP THROUGH EACH RULE
                    // REMAP THE RECORD(S) BASED ON THE MAP
                    // SEND

                    if (replicationRecordsResponse.Items.Count > 0)
                    {

                        foreach (KeyValuePair<string, ReplicationRule> replicationRuleKeyValPair in replicationRules)
                        {

                            // EACH RULE IS ISOLATED, SO ONE FAILING TARGET DOESN'T STOP THE OTHERS

                            ruleResults.Add(this.ReplicateRule(
                                replicationRule: replicationRuleKeyValPair.Value,
                                replicationRecordsResponse: replicationRecordsResponse,
                                operation: operation,
                                utils: utils
                            ));

                        }

                    }

                }

            }
            catch (Exception ex)
            {

                ruleResults.Add(new ReplicationRuleResult(
                    ruleNum: ruleNum ?? "",
                    targetType: "",
                    recordCount: 0,
                    success: false,
                    errorMessage: "Replication setup failed for IDO '" + IDOName + "': " + this.DescribeException(ex),
                    operation: operation
                ));

            }

            // LOG THE OUTCOME OF EACH RULE

            foreach (ReplicationRuleResult ruleResult in ruleResults)
            {
                utils.WriteLogMessage(ruleResult.ToString());
            }

            return 0;

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ReplicateRule
        /* Purpose:  Remaps the loaded records for a single rule and sends them to the rule's target. Never
        /*           throws. Any exception or rejected push is returned as a failed ReplicationRuleResult.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        private ReplicationRuleResult ReplicateRule(ReplicationRule replicationRule, LoadRecordsResponseData replicationRecordsResponse, string operation, Utilities utils)
        {

            ReplicationRuleResult ruleResult = this.SendRule(replicationRule, replicationRecordsResponse, operation, utils);
            ruleResult.Operation = operation;
            return ruleResult;

        }



        private ReplicationRuleResult SendRule(ReplicationRule replicationRule, LoadRecordsResponseData replicationRecordsResponse, string operation, Utilities utils)
        {

            int recordCount = 0;

            try
            {

                // LOOP THROUGH RECORDS AND GET REMAPPED RECORDS

                // @todo, need to extract this from the Rules loop, then I can loop through the records once, rather than rule count times. Should give major performance increase if you have lots of rules for a single IDO.

                List<Dictionary<string, object>> remappedReplicationRecords = this.RemapRecords(
                    fields: replicationRule.MapFieldsList,
                    recordsResponseData: replicationRecordsResponse
                );

                recordCount = remappedReplicationRecords.Count;

                // SEND RECORDS

                switch (replicationRule.TargetType)
                {

                    case "AzureEventHub":

                        // CREDENTIAL VALUES: 01 = ConnectionString, 02 = EventSource, 03 = MaxBatchKBOverride (OPTIONAL)
                        // RULE OPTIONS:      01 = EventSchemaUri,   02 = UpsertFieldName (USED AS THE RECORD ID IN FAILURE MESSAGES),
                        //                    03 = OperationFieldName (OPTIONAL: BODY FIELD THAT RECEIVES THE OPERATION)
                        // THESE POSITIONS MUST MATCH THE CredentialKeys AND OptionKeys LISTS IN THE CREDENTIALS AND RULES FORM SCRIPTS.

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
                                return new ReplicationRuleResult(
                                    ruleNum: replicationRule.RuleNum,
                                    targetType: replicationRule.TargetType,
                                    recordCount: recordCount,
                                    success: false,
                                    errorMessage: "The rule has an Event Schema URI, but its credential has no Event Source. Set Event Source on the credential (for example, your company's website URL)."
                                );
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

                        return new ReplicationRuleResult(
                            ruleNum: replicationRule.RuleNum,
                            targetType: replicationRule.TargetType,
                            recordCount: recordCount,
                            success: eventHubPushResult.Success,
                            errorMessage: eventHubPushResult.ErrorSummary
                        );

                    case "SalesforceRESTAPI":

                        // RULE OPTIONS: 01 = DestinationName (sObject), 02 = ExternalIDFieldName, 03 = DeleteBehavior (Skip OR Delete)
                        // THESE POSITIONS MUST MATCH THE OptionKeys LIST IN THE RULES FORM SCRIPT.

                        bool isDelete = operation == "Delete";
                        bool deleteInSalesforce = string.Equals((replicationRule.Option03 ?? "").Trim(), "Delete", StringComparison.OrdinalIgnoreCase);

                        // DELETES ARE SKIPPED UNLESS THE RULE OPTS IN, SO A SYTELINE DELETE NEVER REMOVES SALESFORCE DATA BY DEFAULT

                        if (isDelete && !deleteInSalesforce)
                        {
                            return new ReplicationRuleResult(
                                ruleNum: replicationRule.RuleNum,
                                targetType: replicationRule.TargetType,
                                recordCount: recordCount,
                                success: true,
                                errorMessage: "Delete skipped, because the rule's DeleteBehavior isn't Delete."
                            );
                        }

                        SalesforceRestAPI salesforceRestAPI = new SalesforceRestAPI(
                            credential: new SalesforceAPICredential(
                                ClientId: replicationRule.CredentialValue01,
                                ClientSecret: replicationRule.CredentialValue02,
                                Username: replicationRule.CredentialValue03,
                                Password: replicationRule.CredentialValue04,
                                SecurityToken: replicationRule.CredentialValue05
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

                        List<string> salesforceErrors = new List<string>();

                        foreach (SalesforceAPIUpsertResponse response in salesforceUpsertResults.responses)
                        {

                            // A FAILED BATCH (AUTH, BAD OBJECT, HTTP ERROR) HAS NO RECORD RESULTS, ONLY AN ERROR CODE AND MESSAGE

                            if (!response.success)
                            {
                                salesforceErrors.Add("Batch failed: " + (response.errorCode + " " + response.message).Trim());
                            }

                            // A SUCCESSFUL BATCH CAN STILL CONTAIN INDIVIDUAL RECORD FAILURES

                            foreach (Dictionary<string, string> recordFailure in response.recordResultsFailures)
                            {

                                // A RECORD THAT'S ALREADY GONE FROM SALESFORCE COUNTS AS DELETED, SO A RETRY OR A SECOND DELETE ISN'T A FAILURE

                                string failureCode = recordFailure.ContainsKey("errorCode") ? recordFailure["errorCode"] : "";

                                if (isDelete && (failureCode == "NOT_FOUND" || failureCode == "ENTITY_IS_DELETED"))
                                {
                                    continue;
                                }

                                salesforceErrors.Add("Record '" + (recordFailure.ContainsKey("id") ? recordFailure["id"] : "") + "' failed: " + (recordFailure.ContainsKey("errorMessage") ? recordFailure["errorMessage"] : ""));
                            }

                        }

                        return new ReplicationRuleResult(
                            ruleNum: replicationRule.RuleNum,
                            targetType: replicationRule.TargetType,
                            recordCount: recordCount,
                            success: salesforceErrors.Count == 0,
                            errorMessage: string.Join(" | ", salesforceErrors)
                        );

                    default:

                        return new ReplicationRuleResult(
                            ruleNum: replicationRule.RuleNum,
                            targetType: replicationRule.TargetType,
                            recordCount: recordCount,
                            success: false,
                            errorMessage: "Unknown target type '" + replicationRule.TargetType + "'."
                        );

                }

            }
            catch (Exception ex)
            {

                return new ReplicationRuleResult(
                    ruleNum: replicationRule.RuleNum,
                    targetType: replicationRule.TargetType,
                    recordCount: recordCount,
                    success: false,
                    errorMessage: this.DescribeException(ex)
                );

            }

        }



        private static string NormalizeOperation(string operation)
        {

            switch ((operation ?? "").Trim().ToLowerInvariant())
            {
                case "insert":
                    return "Insert";
                case "update":
                    return "Update";
                case "delete":
                    return "Delete";
                default:
                    return "";
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



        private List<Dictionary<string, object>> RemapRecords(List<ReplicationField> fields, LoadRecordsResponseData recordsResponseData)
        {

            Dictionary<string, object> remappedRecord;
            List<Dictionary<string, object>> remappedRecords = new List<Dictionary<string, object>>();

            foreach (IDOItem record in recordsResponseData.Items)
            {

                remappedRecord = new Dictionary<string, object>();

                foreach (ReplicationField field in fields)
                {

                    if (field.SourceType == "IDOProperty")
                    {

                        if (!record.PropertyValues[recordsResponseData.PropertyKeys[field.SourceValue]].IsNull)
                        {
                            remappedRecord[field.OutputFieldName] = record.PropertyValues[recordsResponseData.PropertyKeys[field.SourceValue]].GetValue<object>();
                        }
                        else
                        {
                            remappedRecord[field.OutputFieldName] = null;
                        }

                    }
                    else
                    {

                        remappedRecord[field.OutputFieldName] = field.SourceValue;

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