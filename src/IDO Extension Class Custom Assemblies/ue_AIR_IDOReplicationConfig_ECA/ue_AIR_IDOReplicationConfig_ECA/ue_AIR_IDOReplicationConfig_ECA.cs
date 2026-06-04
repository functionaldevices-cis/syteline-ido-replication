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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ue_AIR_IDOReplicationConfig_ECA.Helpers;
using ue_AIR_IDOReplicationConfig_ECA.Models;
using ue_AIR_IDOReplicationConfig_ECA.Models.AzureEventHubAPI;
using ue_AIR_IDOReplicationConfig_ECA.Models.SalesforceRestAPI;
using ue_AIR_IDOReplicationConfig_ECA.Models.SytelineAPI;
using static System.Collections.Specialized.BitVector32;

namespace ue_AIR_IDOReplicationConfig_ECA
{

    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /*
    /* Name:     ue_AIR_IDOReplicationConfig_ECA
    /* Purpose:  Assembly class to hold all methods related to AES-Driven IDO-Layer Replication.
    /* Date:     2025-04-21
    /* Author:   Andy Mercer
    /*
    /* Copyright 2025, Functional Devices, Inc
    /*
    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /**********************************************************************************************************/

    [IDOExtensionClass("ue_AIR_IDOReplicationConfig_ECA")]
    public class ue_AIR_IDOReplicationConfig_ECA : ExtensionClassBase
    {

        private string Debug1 { get; set; } = string.Empty;

        private string Debug2 { get; set; } = string.Empty;

        private string Debug3 { get; set; } = string.Empty;


        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_UpdateAESTriggers
        /* Date:     2026-16-14
        /* Authors:  Andy Mercer
        /* Purpose:  This method will create or delete AES triggers, as specified by the user.
        /*
        /* Free to use under MIT License, Copyright (c) 2025 FDI Information Systems. See full license at
        /* https://github.com/functionaldevices-cis/syteline-custom-load-method-examples?tab=MIT-1-ov-file#readme
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_UpdateAESTriggers(string idoName, string action, ref string results)
        {

            // SETUP VARS

            int result = 1;
            this.Debug1 = idoName;
            this.Debug2 = action;
            this.Debug3 = "";

            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            Utilities utils = new Utilities(
                commands: this.Context.Commands
            );

            // CREATE UPDATE ITEMS

            switch (action)
            {

                case "SetupTriggers":
                    
                    // CREATE ALL TRIGGERS

                    result = this.SetupTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    break;

                case "RemoveTriggers":

                    // REMOVE ALL TRIGGERS

                    result = this.RemoveTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    break;

                case "FixTriggers":

                    // REMOVE ALL TRIGGERS

                    result = this.RemoveTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    // RE-CREATE ALL TRIGGERS

                    result = this.SetupTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    break;

                case "ActivateAllTriggers":
                case "DeactivateAllTriggers":

                    // TOGGLE ACTIVE STATUS FOR ALL TRIGGERS

                    result = this.ToggleTriggers(sytelineAPI, idoName, (action == "ActivateAllTriggers" ? 1 : 0));

                    break;

            }

            results = $"1: '{this.Debug1}', 2: '{this.Debug2}', 3: '{this.Debug3}'";
            return result;

        }

        private int SetupTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {

            int defaultSequence = -499;
            int result = 1;

            IDOUpdateItems insertedEventHandlers = new IDOUpdateItems();
            IDOUpdateItems insertedEventActions = new IDOUpdateItems();
            List<IDOUpdateItem> eventHandlers = new List<IDOUpdateItem>();
            List<IDOUpdateItem> eventActions = new List<IDOUpdateItem>();
            List<string> eventNames = new List<string> {
                "CustomAIR", // THIS IS THERE BECAUSE THERE IS A BUG WHEN INSERTING. THE FIRST RECORD DOESN'T GET THE SEQUENCE NUMBER ASSIGNED PROPERLY, SO THIS ENSURES THE SEQUENCING WORKS PROPERLY FOR ALL THE REAL TRIGGERS BECAUSE THIS ONE GETS THE SEQUENCE NUMBER ASSIGNED, THEN WE DELETE THIS ONE AT THE END
                "IdoPostItemInsert",
                "IdoPostItemUpdate",
                "IdoOnItemDelete"
            };

            foreach (string eventName in eventNames)
            {

                eventHandlers.Add(
                    sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"AccessAs", "ue_"},
                        {"EventName", eventName},
                        {"Description", "ue_AIR_ReplicationTrigger"},
                        {"IDOCollections", idoName},
                        {"Active", 0},
                        {"Sequence", defaultSequence},
                        {"NewSequence", defaultSequence},
                        {"RowPointer", ""}
                    })
                );

                defaultSequence += 2;

            }

            insertedEventHandlers = sytelineAPI.CreateRecords(
                IDOName: "EventHandlers",
                records: eventHandlers
            );

            if (insertedEventHandlers != null)
            {

                this.Debug3 = insertedEventHandlers[0].ItemID;

                sytelineAPI.DeleteRecord( // THIS NEEDS TO BE DELETED BECAUSE IT IS A TEMP RECORD. ONLY CREATED TO ENSURE SEQUENCING WORKS PROPERLY FOR THE OTHER RECORDS BECAUSE OF A BUG IN THE SEQUENCING WHEN CREATING MULTIPLE RECORDS AT ONCE
                    IDOName: "EventHandlers",
                    record: sytelineAPI.BuildDeleteItem(
                        insertedEventHandlers[0].ItemID
                    )
                );

                foreach (IDOUpdateItem eventHandler in insertedEventHandlers.Skip(1))
                {
                    eventActions.Add(sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"EventHandlerRowPointer", eventHandler.Properties["RowPointer"].GetValue<string>()},
                        {"Description", "ue_AIR_ReplicationTrigger"},
                        {"Sequence", "10"},
                        {"ActionType", "21"},
                        {"Parameters", "IDO(\"ue_AIR_IDOReplicationRules\") METHOD(\"ReplicateRecordToExternalSystems\") PARMS(-1, SUBSTITUTE(\"RowPointer = '{0}'\", P(\"RowPointer\")), \"" + idoName + "\")"},
                    }));
                }

                insertedEventActions = sytelineAPI.CreateRecords(
                    IDOName: "EventActions",
                    records: eventActions
                );

                if (insertedEventActions != null)
                {
                    result = 0; // SUCCESS
                }

            }

            return result;

        }

        private int RemoveTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {
            int result = 0;

            LoadRecordsResponseData eventHandlerResponse = sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_EventHandlers",
                filter: sytelineAPI.BuildFilterString(new List<string>(){
                    $"Description = 'ue_AIR_ReplicationTrigger'",
                    $"IDOCollections = '{idoName}'"
                }),
                orderBy: "",
                properties: new List<string>() { "RowPointer", "EventName", "Active", "ue_IsSetup" }
            );

            if (eventHandlerResponse.Items.Count > 0)
            {

                LoadRecordsResponseData eventActionResponse = sytelineAPI.LoadRecords(
                    IDOName: "EventActions",
                    filter: $"EventHandlerRowPointer IN ( {string.Join(", ", eventHandlerResponse.Items.Select(eventHandler => $"'{eventHandler.PropertyValues[eventHandlerResponse.PropertyKeys["RowPointer"]].GetValue<string>()}'"))} )",
                    orderBy: "",
                    properties: new List<string>() { "RowPointer" }
                );

                sytelineAPI.DeleteRecords(
                    IDOName: "EventActions",
                    records: eventActionResponse.Items.Select(eventAction => sytelineAPI.BuildDeleteItem(
                        eventAction.ItemID
                    )).ToList()
                );

                sytelineAPI.DeleteRecords(
                    IDOName: "ue_AIR_EventHandlers",
                    records: eventHandlerResponse.Items.Select(eventHandler => sytelineAPI.BuildDeleteItem(
                        eventHandler.ItemID
                    )).ToList()
                );

            }

            return result;

        }

        private int ToggleTriggers(SytelineInternalAPI sytelineAPI, string idoName, int activeStatus)
        {
            int result = 1;

            LoadRecordsResponseData eventHandlerResponse = sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_EventHandlers",
                filter: sytelineAPI.BuildFilterString(new List<string>(){
                    $"Description = 'ue_AIR_ReplicationTrigger'",
                    $"IDOCollections = '{idoName}'"
                }),
                orderBy: "",
                properties: new List<string>() { "RowPointer", "EventName", "Active", "ue_IsSetup" }
            );

            if (eventHandlerResponse.Items.Count > 0)
            {
                sytelineAPI.UpdateRecords(
                    IDOName: "ue_AIR_EventHandlers",
                    records: eventHandlerResponse.Items.Select(r => sytelineAPI.BuildUpdateItem(
                        r.ItemID,
                        new List<IDOUpdateProperty> {
                            new IDOUpdateProperty("Active", activeStatus, true)
                        }
                    )).ToList()
                );
            }
            return result;

        }

    }

}