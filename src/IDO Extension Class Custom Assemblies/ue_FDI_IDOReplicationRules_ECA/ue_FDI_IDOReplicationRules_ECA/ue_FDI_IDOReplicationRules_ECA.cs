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

            IDOHelper idoHelper = new IDOHelper(
                IDOCommands: this.Context.Commands,
                BGTaskNum: BGTaskNum,
                DebugLevel: debugLevel
            );


            //idoHelper.WriteLogMessage("( IDOName = '" + IDOName + "') AND ( IsActive = 1 )" + (targetType != null ? "AND ( TargetType = '" + targetType + "' )" : ""));

            // LOAD REPLICATION RULE RECORDS FOR SPECIFIED IDO

            GetRecordsResponseData replicationRuleRecordsResponse = idoHelper.GetRecords(new QueryDef(
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

            GetRecordsResponseData replicationMapFieldSourceRecordsResponse = idoHelper.GetRecords(new QueryDef(
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

            GetRecordsResponseData replicationRecordsResponse = idoHelper.GetRecords(new QueryDef(
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
                Task<bool> currentRuleTask;
                List<MapField> mapFields;
                ReplicationRule replicationRule;

                foreach (KeyValuePair<string, ReplicationRule> replicationRuleKeyValPair in replicationRules)
                {

                    // SETUP RULE STRUCTURE

                    currentRuleNum = replicationRuleKeyValPair.Key;
                    replicationRule = replicationRuleKeyValPair.Value;
                    mapFields = replicationRule.MapFieldsList;

                    // LOOP THROUGH RECORDS AND GET REMAPPED RECORDS

                    remappedReplicationRecords = this.RemapRecords(
                        mapFields: mapFields,
                        recordsResponseData: replicationRecordsResponse
                    );

                    // SEND RECORDS

                    switch (replicationRule.TargetType)
                    {

                        case "AzureEventHub":

                            // SEND THE RECORD

                            currentRuleTask = AzureEventHubPusher.ExportToAzureEventHub(
                                eventHubCredential: new AzureEventHubCredential(
                                    ConnectionString: replicationRule.CredentialValue01
                                ),
                                records: remappedReplicationRecords
                            );
                            currentRuleTask.Wait();

                            break;

                        case "SalesforceRestAPI":

                            // SEND THE RECORD

                            currentRuleTask = SalesforceRESTAPI.Upsert(
                                salesforceCredential: new SalesforceCredential(
                                    ClientId: replicationRule.CredentialValue01,
                                    ClientSecret: replicationRule.CredentialValue02,
                                    Username: replicationRule.CredentialValue03,
                                    Password: replicationRule.CredentialValue04,
                                    SecurityToken: replicationRule.CredentialValue05
                                ),
                                records: remappedReplicationRecords
                            );
                            currentRuleTask.Wait();

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

                    remappedRecord[fieldMap.OutputFieldName] = string.Concat(values);

                }

                remappedRecords.Add(remappedRecord);

            }

            return remappedRecords;

        }

    }

}