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
        /* Name:     ue_UpdateTriggers
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
        public int ue_UpdateAESTriggers(string idoName, string actions)
        {

            // SETUP VARS

            List<IDOUpdateItem> eventHandlerRecords = new List<IDOUpdateItem>();
            List<IDOUpdateItem> eventActionRecords = new List<IDOUpdateItem>();

            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands
            );

            // PARSE INPUT

            List<string> actionsSplit = actions.Replace(" ", "").Split(',').ToList();

            // CREATE UPDATE ITEMS

            if (actionsSplit.Contains("SetupPostInsert"))
            {
                IDOUpdateItem postInsertEventHandler = new IDOUpdateItem(UpdateAction.Insert);
                postInsertEventHandler.Properties.Add("EventName", "IdoPostItemInsert", true);
                postInsertEventHandler.Properties.Add("Description", "ue_AIR_" + idoName, true);
                postInsertEventHandler.Properties.Add("IDOCollections", idoName, true);
                postInsertEventHandler.Properties.Add("Active", 1, true);
                postInsertEventHandler.Properties.Add("Overridable", 1, true);
                postInsertEventHandler.Properties.Add("Synchronous", 1, true);
                eventHandlerRecords.Add(postInsertEventHandler);
            }

            if (actionsSplit.Contains("SetupPostUpdate"))
            {
                IDOUpdateItem postInsertEventHandler = new IDOUpdateItem(UpdateAction.Insert);
                postInsertEventHandler.Properties.Add("EventName", "IdoPostItemUpdate", true);
                postInsertEventHandler.Properties.Add("Description", "ue_AIR_" + idoName, true);
                postInsertEventHandler.Properties.Add("IDOCollections", idoName, true);
                postInsertEventHandler.Properties.Add("Active", 1, true);
                postInsertEventHandler.Properties.Add("Overridable", 1, true);
                postInsertEventHandler.Properties.Add("Synchronous", 1, true);
                eventHandlerRecords.Add(postInsertEventHandler);
            }

            if (actionsSplit.Contains("SetupPostDelete"))
            {
                IDOUpdateItem postInsertEventHandler = new IDOUpdateItem(UpdateAction.Insert);
                postInsertEventHandler.Properties.Add("EventName", "IdoPostItemDelete", true);
                postInsertEventHandler.Properties.Add("Description", "ue_AIR_" + idoName, true);
                postInsertEventHandler.Properties.Add("IDOCollections", idoName, true);
                postInsertEventHandler.Properties.Add("Active", 1, true);
                postInsertEventHandler.Properties.Add("Overridable", 1, true);
                postInsertEventHandler.Properties.Add("Synchronous", 1, true);
                eventHandlerRecords.Add(postInsertEventHandler);
            }

            if (actionsSplit.Contains("RemovePostInsert"))
            {

            }

            if (actionsSplit.Contains("RemovePostUpdate"))
            {

            }

            if (actionsSplit.Contains("RemovePostDelete"))
            {

            }

            if (eventHandlerRecords.Count > 0)
            {
                sytelineAPI.CreateRecords("EventHandlers", eventHandlerRecords);
            }

            //"EventActions"

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

            //utils.WriteLogMessage("( IDOName = '" + IDOName + "') AND ( IsActive = 1 )" + (targetType != null ? "AND ( TargetType = '" + targetType + "' )" : ""));

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

                    // SETUP RECORD STRUCTURE RECORD

                    List<Dictionary<string, object>> remappedReplicationRecords;
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

                        // SEND RECORDS

                        switch (replicationRule.TargetType)
                        {

                            case "AzureEventHub":

                                // SEND THE RECORD

                                Task<bool> eventHubPush = AzureEventHubPusher.ExportToAzureEventHub(
                                    eventHubCredential: new AzureEventHubSASCredential(
                                        ConnectionString: replicationRule.CredentialValue01
                                    ),
                                    records: remappedReplicationRecords
                                );
                                eventHubPush.Wait();

                                break;

                            case "SalesforceRESTAPI":

                                // SEND THE RECORD

                                SalesforceRestAPI salesforceRestAPI = new SalesforceRestAPI(
                                    credential: new SalesforceAPICredential(
                                        ClientId: replicationRule.CredentialValue01,
                                        ClientSecret: replicationRule.CredentialValue02,
                                        Username: replicationRule.CredentialValue03,
                                        Password: replicationRule.CredentialValue04,
                                        SecurityToken: replicationRule.CredentialValue05
                                    )
                                );

                                utils.WriteLogMessage("Pushing " + replicationRecordsResponse.Items.Count + " records to Salesforce.");

                                Task<SalesforceAPIUpsertResults> salesforceUpsert = salesforceRestAPI.UpsertRecords(
                                    objectName: replicationRule.Option01,
                                    externalIDFieldName: replicationRule.Option02,
                                    records: remappedReplicationRecords,
                                    onProgressCallback: (queryStatus) => { utils.WriteLogMessage(queryStatus.ErrorMessage); }
                                );

                                salesforceUpsert.Wait();

                                break;

                        }

                    }

                }

            }

            return 0;

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

                List<string> idoProperties = new List<string>();

                replicationMapFieldRecordsResponse.Items.ForEach(replicationMapFieldRecord =>
                {

                    string replicationMapFieldRecordRuleNum = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["RuleNum"]].Value;
                    string replicationMapFieldRecordFieldSeq = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["FieldSeq"]].Value;
                    string replicationMapFieldRecordFieldName = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["FieldName"]].Value;
                    string replicationMapFieldRecordType = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["Type"]].Value;
                    string replicationMapFieldRecordValue = replicationMapFieldRecord.PropertyValues[replicationMapFieldRecordsResponse.PropertyKeys["Value"]].Value;

                    if ((replicationMapFieldRecordValue != null && replicationMapFieldRecordValue != "") || (replicationMapFieldRecordType == "Literal"))
                    {

                        replicationRules[replicationMapFieldRecordRuleNum].MapFields[replicationMapFieldRecordFieldSeq] = new ReplicationField(
                            outputFieldName: replicationMapFieldRecordFieldName,
                            sourceType: replicationMapFieldRecordType,
                            sourceValue: replicationMapFieldRecordValue
                        );

                        if (replicationMapFieldRecordType == "IDOProperty" && !idoProperties.Contains(replicationMapFieldRecordValue))
                        {
                            idoProperties.Add(replicationMapFieldRecordValue);
                        }

                    }

                });

            }

            return replicationRules;

        }

    }

}