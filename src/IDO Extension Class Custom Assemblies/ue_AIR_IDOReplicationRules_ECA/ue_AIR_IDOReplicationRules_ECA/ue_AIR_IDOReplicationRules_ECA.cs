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
        public int ue_ReplicateRecordToExternalSystems(int BGTaskNum = -1, string queryFilter = "", string IDOName = "", int recordCap = 1, string ruleNum = null)
        {

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
                    errorMessage: "Replication setup failed for IDO '" + IDOName + "': " + this.DescribeException(ex)
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

        private ReplicationRuleResult ReplicateRule(ReplicationRule replicationRule, LoadRecordsResponseData replicationRecordsResponse, Utilities utils)
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

                        bool eventHubPushSucceeded = AzureEventHubPusher.ExportToAzureEventHub(
                            eventHubCredential: new AzureEventHubSASCredential(
                                ConnectionString: replicationRule.CredentialValue01
                            ),
                            records: remappedReplicationRecords
                        ).GetAwaiter().GetResult();

                        return new ReplicationRuleResult(
                            ruleNum: replicationRule.RuleNum,
                            targetType: replicationRule.TargetType,
                            recordCount: recordCount,
                            success: eventHubPushSucceeded,
                            errorMessage: eventHubPushSucceeded ? "" : "Azure Event Hub rejected one or more batches."
                        );

                    case "SalesforceRESTAPI":

                        SalesforceRestAPI salesforceRestAPI = new SalesforceRestAPI(
                            credential: new SalesforceAPICredential(
                                ClientId: replicationRule.CredentialValue01,
                                ClientSecret: replicationRule.CredentialValue02,
                                Username: replicationRule.CredentialValue03,
                                Password: replicationRule.CredentialValue04,
                                SecurityToken: replicationRule.CredentialValue05
                            )
                        );

                        utils.WriteLogMessage("Pushing " + recordCount + " records to Salesforce.");

                        SalesforceAPIUpsertResults salesforceUpsertResults = salesforceRestAPI.UpsertRecords(
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
            Dictionary<string, int> itemIndices = new Dictionary<string, int>();

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

            (bool haveBookmark, bool areCappingResults) flags = (false, false);

            LoadRecordsRequestData userRequest = new LoadRecordsRequestData(
                contextRequest: this.Context.Request as LoadCollectionRequestData,
                filterOverride: queryFilter,
                recordCapOverride: "5"
            );

            flags.haveBookmark = userRequest.Bookmark != "<B/>";
            flags.areCappingResults = userRequest.RecordCap != 0;
                
            List<Dictionary<string, object>> remappedReplicationRecords = new List<Dictionary<string, object>>();


            /********************************************************************/
            /* LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO
            /********************************************************************/

            Dictionary<string, ReplicationRule> replicationRules = this.LoadReplicationRulesAndMaps(sytelineAPI, utils, IDOName, ruleNum);




            /********************************************************************/
            /* LOAD MATCHING RECORDS TO BE REPLICATED
            /********************************************************************/


            if (replicationRules.Keys.Count > 0)
            {

                // LOAD THE REPLICATION RECORD

                LoadRecordsResponseData replicationRecordsResponse = sytelineAPI.LoadRecords(
                    IDOName: IDOName,
                    properties: replicationRules.Values.Select(rule => rule.SourceIDOProperties).SelectMany(innerList => innerList).Distinct().ToList(),
                    filter: userRequest.Filter,
                    orderBy: "",
                    recordCap: userRequest.RecordCap
                );

                // STEP THROUGH EACH RULE
                // REMAP THE RECORD(S) BASED ON THE MAP
                // SEND

                if (replicationRecordsResponse.Items.Count > 0)
                {

                    // SETUP RECORD STRUCTURE RECORD

                    string currentRuleNum;
                    List<ReplicationField> mapFields;
                    ReplicationRule replicationRule;

                    foreach (KeyValuePair<string, ReplicationRule> replicationRuleKeyValPair in replicationRules)
                    {

                        // SETUP RULE STRUCTURE

                        currentRuleNum = replicationRuleKeyValPair.Key;
                        replicationRule = replicationRuleKeyValPair.Value;
                        mapFields = replicationRule.MapFieldsList;

                        // LOOP THROUGH RECORDS AND GET REMAPPED RECORDS

                        // @todo, need to extract this from the Rules loop, then I can loop through the records once, rather than rule count times. Should give major performance increase if you have lots of rules for a single IDO.

                        remappedReplicationRecords = this.RemapRecords(
                            fields: mapFields,
                            recordsResponseData: replicationRecordsResponse
                        );

                    }

                }

            }



            /********************************************************************/
            /* LOOP THROUGH THE ITEM PRICE RECORDS AND FILL IN THE DATA TABLE
            /********************************************************************/

            foreach(Dictionary<string, object> remappedRecord in remappedReplicationRecords)
            {

                // LOAD DATA FROM THE ITEM PRICE RECORD AND RUN CUSTOM LOGIC

                int counter = 1;
                DataRow outputRow = outputTable.NewRow();

                // LOAD THE FIRST 20 PROPERTIES INTO THE PREVIEW TABLE (AND IGNORE THE REST IF THERE ARE MORE THAN 20)

                foreach (object item in remappedRecord.Values)
                {

                    outputRow[$"OutputPreviewField{counter:00}"] = item;
                    counter++;
                    if (counter > 20)
                    {
                        break;
                    }
                }

                outputTable.Rows.Add(outputRow);

            }

            if (outputTable.Rows.Count > 0)
            {
                int bookmarkRowIndex = outputTable.Rows.Count > userRequest.RecordCap ? outputTable.Rows.Count - 2 : outputTable.Rows.Count - 1;
                userRequest.Bookmark = outputTable.Rows[bookmarkRowIndex]["Item"].ToString();
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



        private Dictionary<string, ReplicationRule> LoadReplicationRulesAndMaps(SytelineInternalAPI sytelineAPI, Utilities utils, string IDOName, string ruleNum = null)
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
                filter: "( IDOName = '" + IDOName + "') AND ( IsActive = 1 )" + (ruleNum != null ? "AND ( RuleNum = '" + ruleNum + "' )" : ""),
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
                    filter: "( RuleNum IN (" + string.Join(",", replicationRules.Keys.Select(key => "'" + key + "'")) + ") )",
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