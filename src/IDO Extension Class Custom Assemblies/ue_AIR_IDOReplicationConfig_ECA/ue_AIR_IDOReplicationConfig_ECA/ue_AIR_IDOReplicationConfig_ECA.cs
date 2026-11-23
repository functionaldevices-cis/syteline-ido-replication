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
                        : $"The triggers for {idoName} couldn't be created completely. If the status shows Broken, click Fix Triggers.";

                    break;

                case "RemoveTriggers":

                    // REMOVE ALL TRIGGERS

                    result = this.RemoveTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName,
                        removed: out int removed
                    );

                    this.Message = removed > 0
                        ? $"The triggers for {idoName} were removed."
                        : $"{idoName} has no replication triggers, so nothing was removed.";

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

                case "SetupDiagnosticTriggers":

                    // FOR TESTING ONLY: CREATE ACTIVE EVENT HANDLERS THAT LOG EVERY UPDATE EVENT ON THE IDO (SEE SetupDiagnosticTriggers)

                    result = this.SetupDiagnosticTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName
                    );

                    break;

                case "RemoveDiagnosticTriggers":

                    result = this.RemoveTriggers(
                        sytelineAPI: sytelineAPI,
                        idoName: idoName,
                        removed: out int removedDiagnostic,
                        description: DiagnosticTriggerDescription
                    );

                    this.Message = removedDiagnostic > 0
                        ? $"The {removedDiagnostic} diagnostic triggers for {idoName} were removed."
                        : $"{idoName} has no diagnostic triggers, so nothing was removed.";

                    break;

                default:

                    this.Message = $"'{action}' isn't a trigger action.";

                    break;

            }

            results = this.Message;
            return result;

        }

        // LOADS THE IDO'S REPLICATION TRIGGERS (ITS EVENT HANDLERS), OR ITS DIAGNOSTIC TRIGGERS WHEN GIVEN THEIR DESCRIPTION

        private LoadRecordsResponseData LoadTriggers(SytelineInternalAPI sytelineAPI, string idoName, string description = TriggerDescription)
        {

            return sytelineAPI.LoadRecords(
                IDOName: "ue_AIR_EventHandlers",
                filter: sytelineAPI.BuildFilterString(new List<string>(){
                    $"Description = '{description}'",
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
            string nextStep = "Refresh the form, then click Setup Triggers (or Fix Triggers, if the status shows Broken)" + (activeEventNames.Count > 0 ? ", then Activate All." : ".");

            // REMOVE ALL TRIGGERS. WHEN THIS FAILS, THE TRIGGERS THAT ARE LEFT STILL HOLD THEIR ACTIVE FLAGS, SO FIX TRIGGERS
            // CAN BE CLICKED AGAIN.

            try
            {

                this.RemoveTriggers(
                    sytelineAPI: sytelineAPI,
                    idoName: idoName,
                    removed: out _
                );

            }
            catch (Exception ex)
            {
                throw new Exception($"Removing the triggers for {idoName} failed, so they weren't re-created: {ex.Message} Refresh the form, then click Fix Triggers again.");
            }

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
                this.Message = $"The triggers for {idoName} were re-created. None was active, so they are inactive. Click Activate All if this IDO should replicate.";
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



        /**********************************************************************************************************/
        /*
        /* DIAGNOSTIC TRIGGERS, FOR TESTING HOW SYTELINE FIRES EVENTS. THEY AREN'T PART OF REPLICATION.
        /*
        /* SetupDiagnosticTriggers CREATES ONE SYNCHRONOUS EVENT HANDLER FOR EACH UPDATE EVENT (THE TWO COLLECTION EVENTS AND THE SIX
        /* ITEM EVENTS), PLUS AN ASYNCHRONOUS ONE FOR IdoPostUpdateCollection. EACH CALLS ue_LogTriggerEvent ON THE RULES IDO, WHICH
        /* WRITES A "Diag: <event>" ROW TO THE REPLICATION LOG WITH WHAT THE HANDLER PASSED: THE EVENT, THE IDO, P("RowPointer"),
        /* THE IDO'S KEY PROPERTIES AND P("_ItemId").
        /*
        /* UNLIKE THE REPLICATION TRIGGERS, THEY ARE CREATED ACTIVE, SO EVERY SAVE TO THE IDO WRITES ROWS UNTIL THEY ARE REMOVED. THEY
        /* HAVE THEIR OWN DESCRIPTION, SO THE CONFIGS FORM'S TRIGGER STATUS, FIX TRIGGERS AND ACTIVATE ALL DON'T SEE THEM. USE THEM ON
        /* A TEST SYSTEM AND REMOVE THEM WITH RemoveDiagnosticTriggers WHEN THE TESTS ARE DONE.
        /*
        /**********************************************************************************************************/

        private const string DiagnosticTriggerDescription = "ue_AIR_DiagnosticTrigger";

        private static readonly string[] DiagnosticEventNames = {
            "IdoOnUpdateCollection",
            "IdoPostUpdateCollection",
            "IdoOnItemInsert",
            "IdoPostItemInsert",
            "IdoOnItemUpdate",
            "IdoPostItemUpdate",
            "IdoOnItemDelete",
            "IdoPostItemDelete"
        };

        private int SetupDiagnosticTriggers(SytelineInternalAPI sytelineAPI, string idoName)
        {

            // THE HANDLERS ARE ACTIVE AT ONCE AND RUN ON EVERY SAVE TO THE IDO, AND EXECUTE IS GRANTED ON THE CONFIGS IDO AS A WHOLE, NOT PER
            // METHOD, SO ONLY A SUPER USER MAY CREATE THEM. THIS PROJECT'S OWN IDOs ARE REFUSED: A DIAGNOSTIC TRIGGER ON THE LOG WOULD FIRE ON
            // ITS OWN LOG ROW, AND AGAIN ON THAT ONE, WITHOUT END.

            if (!IsSuperUser())
            {
                this.Message = "Only a super user can set up diagnostic triggers.";
                return 1;
            }

            string trimmedIdoName = (idoName ?? "").Trim();

            if (trimmedIdoName == ""
                || trimmedIdoName.StartsWith("ue_AIR_", StringComparison.OrdinalIgnoreCase)
                || trimmedIdoName.Equals("EventHandlers", StringComparison.OrdinalIgnoreCase)
                || trimmedIdoName.Equals("EventActions", StringComparison.OrdinalIgnoreCase))
            {
                this.Message = $"Diagnostic triggers can't be set up on '{idoName}'. Pick the IDO you want to test, not one of this project's IDOs or the event system's.";
                return 1;
            }

            try
            {

                // START CLEAN, SO RUNNING SETUP TWICE DOESN'T DOUBLE THE ROWS

                this.RemoveTriggers(sytelineAPI, idoName, out int removedBefore, DiagnosticTriggerDescription);

                List<string> keyNames;
                string keyValuesExpression = this.BuildKeyValuesExpression(idoName, out keyNames);

                // EACH HANDLER: ITS EVENT AND WHETHER IT IS SYNCHRONOUS. THE FIRST IS A TEMPORARY HANDLER, AS IN SetupTriggers, BECAUSE THE
                // FIRST HANDLER INSERTED IN A BATCH DOESN'T GET ITS SEQUENCE NUMBER ASSIGNED PROPERLY. IT IS DELETED AFTERWARD.

                List<KeyValuePair<string, bool>> handlers = new List<KeyValuePair<string, bool>>
                {
                    new KeyValuePair<string, bool>("CustomAIR", true)
                };
                handlers.AddRange(DiagnosticEventNames.Select(eventName => new KeyValuePair<string, bool>(eventName, true)));
                handlers.Add(new KeyValuePair<string, bool>("IdoPostUpdateCollection", false));

                int sequence = -399;
                List<IDOUpdateItem> eventHandlers = new List<IDOUpdateItem>();

                for (int i = 0; i < handlers.Count; i++)
                {

                    eventHandlers.Add(sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"AccessAs", "ue_"},
                        {"EventName", handlers[i].Key},
                        {"Description", DiagnosticTriggerDescription},
                        {"IDOCollections", idoName},
                        {"Active", i == 0 ? 0 : 1},
                        {"Synchronous", handlers[i].Value ? 1 : 0},
                        {"Transactional", 0},
                        {"IgnoreFailure", 1}, // IF THE HANDLER ITSELF FAILS, FOR EXAMPLE EVALUATING ITS PARMS, THE SAVE STILL GOES THROUGH
                        {"Sequence", sequence},
                        {"NewSequence", sequence},
                        {"RowPointer", ""}
                    }));

                    sequence += 2;

                }

                IDOUpdateItems insertedEventHandlers = sytelineAPI.CreateRecords(
                    IDOName: "ue_AIR_EventHandlers",
                    records: eventHandlers
                );

                if (insertedEventHandlers == null)
                {
                    this.Message = $"The diagnostic triggers for {idoName} couldn't be created.";
                    return 1;
                }

                sytelineAPI.DeleteRecord(
                    IDOName: "ue_AIR_EventHandlers",
                    record: sytelineAPI.BuildDeleteItem(insertedEventHandlers[0].ItemID)
                );

                // THE INSERTED HANDLERS COME BACK IN THE ORDER THEY WERE SENT, SO HANDLER i + 1 IS handlers[i + 1]

                List<IDOUpdateItem> eventActions = new List<IDOUpdateItem>();

                for (int i = 1; i < insertedEventHandlers.Count; i++)
                {

                    string eventName = handlers[i].Key;
                    string mode = handlers[i].Value ? "Sync" : "Async";

                    eventActions.Add(sytelineAPI.BuildInsertItem(new Dictionary<string, object>(){
                        {"EventHandlerRowPointer", insertedEventHandlers[i].Properties["RowPointer"].GetValue<string>()},
                        {"Description", DiagnosticTriggerDescription},
                        {"Sequence", "10"},
                        {"ActionType", "21"},
                        {"Parameters", "IDO(\"ue_AIR_IDOReplicationRules\") METHOD(\"ue_LogTriggerEvent\") PARMS(\"" + eventName + "\", \"" + idoName + "\", P(\"RowPointer\"), " + keyValuesExpression + ", P(\"_ItemId\"), \"" + mode + "\")"},
                    }));

                }

                if (sytelineAPI.CreateRecords(IDOName: "EventActions", records: eventActions) == null)
                {
                    this.Message = $"The diagnostic event handlers for {idoName} were created, but their actions couldn't be. Run RemoveDiagnosticTriggers, then try again.";
                    return 1;
                }

                this.Message = $"{eventActions.Count} diagnostic triggers for {idoName} were created and are active. Every save to {idoName} now writes Diag rows to the replication log."
                    + (keyNames.Count > 0 ? $" Key properties passed: {string.Join(", ", keyNames)}." : $" {idoName} has no key properties, so only RowPointer and _ItemId are passed.")
                    + " Run RemoveDiagnosticTriggers when the tests are done.";

                return 0;

            }
            catch (Exception ex)
            {
                this.Message = $"The diagnostic triggers for {idoName} couldn't be set up: {ex.Message}";
                return 1;
            }

        }

        // WHETHER THE SIGNED-IN USER IS A SUPER USER. FALSE WHEN THE RUNTIME CAN'T SAY.

        private static bool IsSuperUser()
        {

            try
            {
                return IDORuntime.Context != null && IDORuntime.Context.SuperUser;
            }
            catch (Exception)
            {
                return false;
            }

        }

        // THE EVENT EXPRESSION THAT PASSES THE IDO'S KEY PROPERTIES AS "Name=Value|Name=Value", IN KEY ORDER, OR AN EMPTY STRING WHEN THE
        // IDO HAS NO KEYS. P() IN AN ITEM EVENT ONLY SEES THE PROPERTIES THE CALLER SENT FOR THAT RECORD, WHICH IS WHAT THE DIAGNOSTIC TESTS.

        private string BuildKeyValuesExpression(string idoName, out List<string> keyNames)
        {

            keyNames = this.Context.Commands.GetPropertyInfo(idoName).Properties
                .Cast<Mongoose.IDO.Protocol.PropertyInfo>()
                .Where(propertyInfo => propertyInfo.Key > 0)
                .OrderBy(propertyInfo => propertyInfo.Key)
                .Select(propertyInfo => propertyInfo.Name)
                .ToList();

            if (keyNames.Count == 0)
            {
                return "\"\"";
            }

            string template = string.Join("|", keyNames.Select((name, index) => name + "={" + index + "}"));

            return "SUBSTITUTE(\"" + template + "\", " + string.Join(", ", keyNames.Select(name => "P(\"" + name + "\")")) + ")";

        }

        private int RemoveTriggers(SytelineInternalAPI sytelineAPI, string idoName, out int removed, string description = TriggerDescription)
        {
            int result = 0;

            LoadRecordsResponseData eventHandlerResponse = this.LoadTriggers(sytelineAPI, idoName, description);

            removed = eventHandlerResponse.Items.Count;

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