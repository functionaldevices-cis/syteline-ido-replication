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

        private const string TriggerDescription = "ue_AIR_ReplicationTrigger";

        // THE EVENTS THAT GET A REPLICATION TRIGGER

        private static readonly string[] TriggerEventNames = { "IdoPostItemInsert", "IdoPostItemUpdate", "IdoOnItemDelete" };

        // THE MESSAGE RETURNED TO THE FORM IN THE results PARAMETER

        private string Message { get; set; } = string.Empty;


        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_UpdateAESTriggers
        /* Date:     2026-16-14
        /* Authors:  Andy Mercer
        /* Purpose:  This method will create or delete AES triggers, as specified by the user.
        /*
        /*           RETURNS 0 WHEN THE ACTION WORKED AND 1 WHEN IT DIDN'T. results GETS A MESSAGE THAT SAYS WHAT
        /*           WAS DONE.
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
            this.Message = "";

            // SETUP UTILITIES

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            // CREATE UPDATE ITEMS

            switch (action)
            {

                case "SetupTriggers":

                    // CREATE ALL TRIGGERS. THEY ARE CREATED INACTIVE, SO NOTHING REPLICATES UNTIL THE USER ACTIVATES THEM.

                    result = this.SetupTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    this.Message = result == 0
                        ? $"The triggers for {idoName} were created. They are inactive until you click Activate All."
                        : $"The triggers for {idoName} couldn't be created.";

                    break;

                case "RemoveTriggers":

                    // REMOVE ALL TRIGGERS

                    result = this.RemoveTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    this.Message = $"The triggers for {idoName} were removed.";

                    break;

                case "FixTriggers":

                    // REMOVE AND RE-CREATE ALL TRIGGERS, KEEPING EACH ONE ACTIVE OR INACTIVE AS IT WAS

                    result = this.FixTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    break;

                case "ActivateAllTriggers":
                case "DeactivateAllTriggers":

                    // TOGGLE ACTIVE STATUS FOR ALL TRIGGERS

                    result = this.ToggleTriggers(sytelineAPI, idoName, (action == "ActivateAllTriggers" ? 1 : 0));

                    break;

                default:

                    this.Message = $"'{action}' isn't a trigger action.";

                    break;

            }

            results = this.Message;
            return result;

        }

        // LOADS THE IDO'S REPLICATION TRIGGERS (ITS EVENT HANDLERS)

        private LoadRecordsResponseData LoadTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {

            return sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_EventHandlers",
                filter: sytelineAPI.BuildFilterString(new List<string>(){
                    $"Description = '{TriggerDescription}'",
                    $"IDOCollections = {sytelineAPI.QuoteFilterValue(idoName)}"
                }),
                orderBy: "",
                properties: new List<string>() { "RowPointer", "EventName", "Active", "ue_IsSetup" }
            );

        }

        // THE EVENT NAMES OF THE TRIGGERS THAT ARE ACTIVE

        private static HashSet<string> ActiveEventNames(SytelineInternalAPI sytelineAPI, LoadRecordsResponseData triggers)
        {

            HashSet<string> eventNames = new HashSet<string>();

            foreach (IDOItem trigger in triggers.Items)
            {

                string active = sytelineAPI.ParseIDOPropertyValue<string>(trigger.PropertyValues[triggers.PropertyKeys["Active"]]);
                string eventName = sytelineAPI.ParseIDOPropertyValue<string>(trigger.PropertyValues[triggers.PropertyKeys["EventName"]]);

                if (IsTrue(active) && TriggerEventNames.Contains(eventName))
                {
                    eventNames.Add(eventName);
                }

            }

            return eventNames;

        }

        private static bool IsTrue(string value)
        {

            value = (value ?? "").Trim();

            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);

        }

        // FIX TRIGGERS REMOVES THE IDO'S TRIGGERS AND CREATES THEM AGAIN. SetupTriggers CREATES THEM INACTIVE, SO THE
        // ONES THAT WERE ACTIVE BEFORE ARE ACTIVATED AGAIN AFTERWARD. WITHOUT THAT, FIXING THE TRIGGERS OF AN IDO THAT WAS
        // REPLICATING WOULD SILENTLY TURN ITS REPLICATION OFF.
        //
        // THE METHOD ISN'T TRANSACTIONAL, SO A FAILURE PARTWAY LEAVES WHAT WAS ALREADY DONE. A FAILURE PARTWAY IS THROWN, SO
        // THE FORM SHOWS IT AS AN ERROR, AND ITS MESSAGE SAYS WHAT STATE THE TRIGGERS ARE IN AND WHAT TO CLICK NEXT.

        private int FixTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {

            // REMEMBER WHICH TRIGGERS WERE ACTIVE

            HashSet<string> activeEventNames = ActiveEventNames(sytelineAPI, this.LoadTriggers(sytelineAPI, idoName));
            string nextStep = "Click Setup Triggers (or Fix Triggers, if the status shows Broken)" + (activeEventNames.Count > 0 ? ", then Activate All." : ".");

            // REMOVE ALL TRIGGERS

            this.RemoveTriggers(
                sytelineAPI: sytelineAPI,
                idoName: idoName
            );

            // RE-CREATE ALL TRIGGERS

            int result = 1;

            try
            {

                result = this.SetupTriggers(
                    sytelineAPI: sytelineAPI,
                    idoName: idoName
                );

            }
            catch (Exception ex)
            {
                throw new Exception($"The triggers for {idoName} were removed, but creating them again failed: {ex.Message} {nextStep}");
            }

            if (result != 0)
            {
                throw new Exception($"The triggers for {idoName} were removed, but they couldn't be created again. {nextStep}");
            }

            if (activeEventNames.Count == 0)
            {
                this.Message = $"The triggers for {idoName} were re-created. They were inactive before, so they are inactive now.";
                return 0;
            }

            // ACTIVATE THE ONES THAT WERE ACTIVE BEFORE

            int activated;

            try
            {
                result = this.SetTriggersActive(sytelineAPI, idoName, 1, activeEventNames, out activated);
            }
            catch (Exception ex)
            {
                throw new Exception($"The triggers for {idoName} were re-created, but activating them failed: {ex.Message} Click Activate All.");
            }

            if (result != 0 || activated != activeEventNames.Count)
            {
                throw new Exception($"The triggers for {idoName} were re-created, but not all of the ones that were active before could be activated. Click Activate All.");
            }

            this.Message = $"The triggers for {idoName} were re-created. The {activated} that {(activated == 1 ? "was" : "were")} active before {(activated == 1 ? "is" : "are")} active again.";

            return 0;

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
                "CustomAIR" // THIS IS THERE BECAUSE THERE IS A BUG WHEN INSERTING. THE FIRST RECORD DOESN'T GET THE SEQUENCE NUMBER ASSIGNED PROPERLY, SO THIS ENSURES THE SEQUENCING WORKS PROPERLY FOR ALL THE REAL TRIGGERS BECAUSE THIS ONE GETS THE SEQUENCE NUMBER ASSIGNED, THEN WE DELETE THIS ONE AT THE END
            };
            eventNames.AddRange(TriggerEventNames);

            foreach (string eventName in eventNames)
            {

                eventHandlers.Add(
                    sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"AccessAs", "ue_"},
                        {"EventName", eventName},
                        {"Description", TriggerDescription},
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

                sytelineAPI.DeleteRecord( // THIS NEEDS TO BE DELETED BECAUSE IT IS A TEMP RECORD. ONLY CREATED TO ENSURE SEQUENCING WORKS PROPERLY FOR THE OTHER RECORDS BECAUSE OF A BUG IN THE SEQUENCING WHEN CREATING MULTIPLE RECORDS AT ONCE
                    IDOName: "EventHandlers",
                    record: sytelineAPI.BuildDeleteItem(
                        insertedEventHandlers[0].ItemID
                    )
                );

                foreach (IDOUpdateItem eventHandler in insertedEventHandlers.Skip(1))
                {

                    // EACH TRIGGER PASSES ITS OWN OPERATION. THE RECORD CAP (1) AND A BLANK RULE NUMBER (EVERY RULE) ARE
                    // PASSED ONLY TO REACH THE OPERATION PARAMETER.
                    // TWO DERIVED PROPERTIES COMPARE THESE STRINGS EXACTLY, SO KEEP BOTH IN SYNC WITH ANY CHANGE HERE (AND
                    // WITH OperationForEvent):
                    //   - ue_AIR_IDOReplicationConfigs.ue_EventHandlersAreSetup (ONE COMPARISON PER EVENT)
                    //   - ue_AIR_EventHandlers.ue_IsSetup (ONE COMPARISON, PICKING THE OPERATION FROM EventName)

                    string operation = OperationForEvent(eventHandler.Properties["EventName"].GetValue<string>());

                    eventActions.Add(sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"EventHandlerRowPointer", eventHandler.Properties["RowPointer"].GetValue<string>()},
                        {"Description", TriggerDescription},
                        {"Sequence", "10"},
                        {"ActionType", "21"},
                        {"Parameters", "IDO(\"ue_AIR_IDOReplicationRules\") METHOD(\"ue_ReplicateRecordToExternalSystems\") PARMS(-1, SUBSTITUTE(\"RowPointer = '{0}'\", P(\"RowPointer\")), \"" + idoName + "\", 1, \"\", \"" + operation + "\")"},
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

        // KEEP IN SYNC WITH THE CASE EventName IN THE ue_AIR_EventHandlers IDO'S ue_IsSetup DERIVED PROPERTY

        private static string OperationForEvent(string eventName)
        {

            switch (eventName)
            {
                case "IdoPostItemInsert":
                    return "Insert";
                case "IdoPostItemUpdate":
                    return "Update";
                case "IdoOnItemDelete":
                    return "Delete";
                default:
                    return "";
            }

        }



        private int RemoveTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {
            int result = 0;

            LoadRecordsResponseData eventHandlerResponse = this.LoadTriggers(sytelineAPI, idoName);

            if (eventHandlerResponse.Items.Count > 0)
            {

                LoadRecordsResponseData eventActionResponse = sytelineAPI.LoadRecords(
                    IDOName: "EventActions",
                    filter: sytelineAPI.BuildInFilter("EventHandlerRowPointer", eventHandlerResponse.Items.Select(eventHandler => eventHandler.PropertyValues[eventHandlerResponse.PropertyKeys["RowPointer"]].GetValue<string>())),
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

        // ACTIVATE ALL AND DEACTIVATE ALL

        private int ToggleTriggers(SytelineInternalAPI sytelineAPI, string idoName, int activeStatus)
        {

            string verb = activeStatus == 1 ? "activated" : "deactivated";

            int result = this.SetTriggersActive(sytelineAPI, idoName, activeStatus, null, out int changed);

            if (result != 0)
            {
                this.Message = $"The triggers for {idoName} couldn't be {verb}.";
            }
            else if (changed == 0)
            {
                this.Message = $"{idoName} has no replication triggers, so nothing was {verb}.";
            }
            else
            {
                this.Message = $"{changed} {(changed == 1 ? "trigger" : "triggers")} for {idoName} {(changed == 1 ? "was" : "were")} {verb}.";
            }

            return result;

        }

        // SETS THE ACTIVE FLAG ON THE IDO'S TRIGGERS: ALL OF THEM, OR ONLY THE ONES FOR THE GIVEN EVENTS. RETURNS 0 WHEN
        // IT WORKED, WHICH INCLUDES THE CASE WHERE THERE WAS NOTHING TO CHANGE, AND 1 WHEN THE UPDATE DIDN'T GO THROUGH.

        private int SetTriggersActive(SytelineInternalAPI sytelineAPI, string idoName, int activeStatus, HashSet<string> eventNames, out int changed)
        {

            changed = 0;

            LoadRecordsResponseData triggers = this.LoadTriggers(sytelineAPI, idoName);

            List<IDOUpdateItem> updates = triggers.Items
                .Where(trigger => eventNames == null || eventNames.Contains(sytelineAPI.ParseIDOPropertyValue<string>(trigger.PropertyValues[triggers.PropertyKeys["EventName"]]) ?? ""))
                .Select(trigger => sytelineAPI.BuildUpdateItem(
                    trigger.ItemID,
                    new List<IDOUpdateProperty> {
                        new IDOUpdateProperty("Active", activeStatus, true)
                    }
                )).ToList();

            if (updates.Count == 0)
            {
                return 0;
            }

            // UpdateRecords RETURNS NULL WHEN THE RESPONSE DOESN'T HOLD EVERY RECORD THAT WAS SENT

            if (sytelineAPI.UpdateRecords(IDOName: "ue_AIR_EventHandlers", records: updates) == null)
            {
                return 1;
            }

            changed = updates.Count;

            return 0;

        }

    }

}