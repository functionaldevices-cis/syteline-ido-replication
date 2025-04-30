using Infor.DocumentManagement.ICP;
using Mongoose.Core.Extensions;
using Mongoose.IDO;
using Mongoose.IDO.DataAccess;
using Mongoose.IDO.Metadata;
using Mongoose.IDO.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ue_FDI_IDOReplicationRules_ECA.Helpers;
using ue_FDI_IDOReplicationRules_ECA.Models;
using ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI;
using ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI;
using ue_FDI_IDOReplicationRules_ECA.Models.AzureEventHubAPI;

namespace ue_FDI_IDOReplicationRules_ECA
{

    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /*
    /* Name:     ue_FDI_IDOReplicationRules_ECA
    /* Purpose:  Assembly class to hold all methods related to AES-Driven IDO-Layer Replication.
    /* Date:     2025-04-21
    /* Author:   Andy Mercer
    /*
    /* Copyright 2025, Functional Devices, Inc
    /*
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/

    [IDOExtensionClass("ue_FDI_IDOReplicationRules_ECA")]
    public class ue_FDI_IDOReplicationRules_ECA : ExtensionClassBase
    {

        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     PushRecordToExternalSystem
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
        public int ReplicateRecordToExternalSystems(int BGTaskNum = -1, string rowPointer = "", string IDOName = "", string targetType = null, int debugLevel = 0)
        {
 
            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands,
                BGTaskNum: BGTaskNum,
                DebugLevel: debugLevel
            );


            //idoHelper.WriteLogMessage("( IDOName = '" + IDOName + "') AND ( IsActive = 1 )" + (targetType != null ? "AND ( TargetType = '" + targetType + "' )" : ""));

            // LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO

            GetRecordsResponseData replicationRuleRecordsResponse = sytelineAPI.GetRecords(new SytelineQuery(
                IDOName: "ue_FDI_IDOReplicationRules",
                selectProperties: new List<string>(){
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
                filter: "( IDOName = '" + IDOName + "') AND ( IsActive = 1 )" + (targetType != null ? "AND ( TargetType = '" + targetType + "' )" : ""),
                orderBy: "RuleNum ASC"
            ));

            Dictionary<string, ReplicationRule> replicationRules = replicationRuleRecordsResponse.Items.ToDictionary(
                replicationRuleRecord => replicationRuleRecord.PropertyValues[replicationRuleRecordsResponse.PropertyKeys["RuleNum"]].Value,
                replicationRuleRecord => new ReplicationRule(replicationRuleRecord, replicationRuleRecordsResponse.PropertyKeys)
            );

            GetRecordsResponseData replicationMapFieldSourceRecordsResponse = sytelineAPI.GetRecords(new SytelineQuery(
                IDOName: "ue_FDI_IDOReplicationMapFieldSources",
                selectProperties: new List<string>(){
                    { "RuleNum" },
                    { "FieldSeq" },
                    { "FieldName" },
                    { "Type" },
                    { "Value" }
                },
                filter: "( RuleNum IN (" + string.Join(",", replicationRules.Keys.Select(ruleNum => "'" + ruleNum + "'")) + ") )",
                orderBy: "RuleNum ASC, FieldSeq ASC, SourceSeq ASC"
            ));

            List<string> idoProperties = new List<string>();

            replicationMapFieldSourceRecordsResponse.Items.ForEach(replicationRuleRecord => {

                string ruleNum = replicationRuleRecord.PropertyValues[replicationMapFieldSourceRecordsResponse.PropertyKeys["RuleNum"]].Value;
                string fieldSeq = replicationRuleRecord.PropertyValues[replicationMapFieldSourceRecordsResponse.PropertyKeys["FieldSeq"]].Value;
                string fieldName = replicationRuleRecord.PropertyValues[replicationMapFieldSourceRecordsResponse.PropertyKeys["FieldName"]].Value;
                string type = replicationRuleRecord.PropertyValues[replicationMapFieldSourceRecordsResponse.PropertyKeys["Type"]].Value;
                string value = replicationRuleRecord.PropertyValues[replicationMapFieldSourceRecordsResponse.PropertyKeys["Value"]].Value;

                if (!replicationRules[ruleNum].MapFields.ContainsKey(fieldSeq))
                {
                    replicationRules[ruleNum].MapFields[fieldSeq] = new MapField(fieldName);
                }

                if (value != null && value != "")
                {

                    replicationRules[ruleNum].MapFields[fieldSeq].AddSource(source: new MapFieldSource(type: type, value: value));

                    if (type == "IDOProperty" && !idoProperties.Contains(value))
                    {
                        idoProperties.Add(value);
                    }

                }

            });

            // LOAD THE REPLICATION RECORD

            GetRecordsResponseData replicationRecordsResponse = sytelineAPI.GetRecords(new SytelineQuery(
                IDOName: IDOName,
                selectProperties: idoProperties,
                filter: "( RowPointer = '" + rowPointer + "')"
            ));

            // STEP THROUGH EACH RULE
            // REMAP THE RECORD(S) BASED ON THE MAP
            // SEND

            if (replicationRecordsResponse.Items.Count > 0)
            {

                // SETUP RECORD STRUCTURE RECORD

                List<Dictionary<string, object>> remappedReplicationRecords;
                string currentRuleNum;
                List<MapField> mapFields;
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
                        mapFields: mapFields,
                        recordsResponseData: replicationRecordsResponse
                    );

                    // SEND RECORDS

                    switch (replicationRule.TargetType)
                    {

                        case "AzureEventHub":

                            // SEND THE RECORD

                            Task<bool> eventHubPush = AzureEventHubPusher.ExportToAzureEventHub(
                                eventHubCredential: new AzureEventHubCredential(
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

                            sytelineAPI.WriteLogMessage("objectName: '" + replicationRule.Option01 + "'");
                            sytelineAPI.WriteLogMessage("externalIDFieldName: '" + replicationRule.Option02 + "'");
                            sytelineAPI.WriteLogMessage("Syteline_Invoice_Number__c: '" + remappedReplicationRecords[0]["Syteline_Invoice_Number__c"] + "'");

                            SalesforceAPIUpsertResults salesforceUpsert = salesforceRestAPI.UpsertRecords(
                                objectName: replicationRule.Option01,
                                externalIDFieldName: replicationRule.Option02,
                                records: remappedReplicationRecords
                            ).Result;

                            sytelineAPI.WriteLogMessage(salesforceUpsert.responses[0].message);

                            break;

                    }

                }

            }

            return 0;

        }

        private List<Dictionary<string, object>> RemapRecords(List<MapField> mapFields, GetRecordsResponseData recordsResponseData)
        {

            List<object> values;
            object value;
            Dictionary<string, object> remappedRecord;
            List<Dictionary<string, object>> remappedRecords = new List<Dictionary<string, object>>();

            foreach (IDOItem record in recordsResponseData.Items)
            {

                remappedRecord = new Dictionary<string, object>();

                foreach (MapField fieldMap in mapFields)
                {

                    values = new List<object>();

                    foreach (MapFieldSource source in fieldMap.ParsedSources)
                    {

                        if (source.Type == "IDOProperty")
                        {

                            if (!record.PropertyValues[recordsResponseData.PropertyKeys[source.Value]].IsNull)
                            {
                                value = record.PropertyValues[recordsResponseData.PropertyKeys[source.Value]].GetValue<object>();
                            }
                            else
                            {
                                value = null;
                            }

                            values.Add(value);

                        }
                        else
                        {

                            values.Add(source.Value);

                        }

                    }

                    if (fieldMap.OutputFieldName.Contains('.'))
                    {

                        string[] fieldNameParts = fieldMap.OutputFieldName.Split('.');
                        remappedRecord[fieldNameParts[0]] = new Dictionary<string, string>() { { fieldNameParts[1], string.Concat(values) } };

                    }
                    else
                    {

                        remappedRecord[fieldMap.OutputFieldName] = string.Concat(values);

                    }

                }

                remappedRecords.Add(remappedRecord);

            }

            return remappedRecords;

        }

    }

}