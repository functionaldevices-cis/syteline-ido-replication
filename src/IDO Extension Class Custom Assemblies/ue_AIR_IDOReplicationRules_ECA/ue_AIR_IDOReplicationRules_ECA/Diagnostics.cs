using Mongoose.IDO;
using Mongoose.IDO.Protocol;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using ue_AIR_IDOReplicationRules_ECA.Helpers;
using ue_AIR_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_AIR_IDOReplicationRules_ECA
{

    /**********************************************************************************************************/
    /**********************************************************************************************************/
    /*
    /* Diagnostics: methods for testing how Syteline fires IDO events and runs saves. They aren't part of
    /* replication. The Config assembly's SetupDiagnosticTriggers action creates event handlers (description
    /* ue_AIR_DiagnosticTrigger) that call ue_LogTriggerEvent, and ue_DiagUpdateCollection saves test records
    /* through an extension class, the way other extension classes do. See the wiki page Trigger Diagnostics.
    /*
    /* Everything here lives in this file, so it can be removed in one piece once the tests are done.
    /*
    /**********************************************************************************************************/
    /**********************************************************************************************************/

    public partial class ue_AIR_IDOReplicationRules_ECA
    {

        // THE SOURCE OF A DIAGNOSTIC ROW IN THE REPLICATION LOG: "Diag: " AND THE EVENT THAT FIRED. THE VALUES MUST MATCH THE Source INLINE
        // LIST ON ue_AIR_IDOReplicationLogs. AN EVENT NAME THAT ISN'T ONE OF THESE IS LOGGED AS SourceDiagOther, WITH THE NAME IN THE PAYLOAD.

        private const string SourceDiagPrefix = "Diag: ";

        private const string SourceDiagOther = "Diag: Other";

        private static readonly HashSet<string> DiagnosticEventNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IdoOnUpdateCollection",
            "IdoPostUpdateCollection",
            "IdoOnItemInsert",
            "IdoPostItemInsert",
            "IdoOnItemUpdate",
            "IdoPostItemUpdate",
            "IdoOnItemDelete",
            "IdoPostItemDelete"
        };

        // KEPT FOR THE LIFE OF THE PROCESS, SO CONSECUTIVE ROWS SHOW WHETHER STATIC STATE SURVIVES BETWEEN CALLS (THE SEQUENCE KEEPS COUNTING)
        // AND HOW LONG PASSED SINCE THE PREVIOUS EVENT IN THE SAME PROCESS (THE COST OF ONE RECORD IN A SAVE)

        private static long diagnosticSequence = 0;

        private static long diagnosticPreviousTimestamp = 0;

        private static readonly string diagnosticProcessStartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_LogTriggerEvent
        /* Purpose:  Called by a diagnostic event handler. Writes one row to the replication log that records what the
        /*           event handler passed (the raw P() values) and what the runtime looked like when it ran: the
        /*           session, the server, the thread, the database transaction and precise times. Writes nothing
        /*           else and never throws, so it can't fail the save that fired the event. Always returns 0.
        /*
        /*           The row is written inside the save's transaction (when there is one), so a save that rolls
        /*           back also removes its diagnostic rows. That is one of the things the rows test.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_LogTriggerEvent(string eventName = "", string IDOName = "", string rowPointer = "", string keyValues = "", string itemID = "", string mode = "")
        {

            try
            {

                long timestamp = Stopwatch.GetTimestamp();
                long sequence = Interlocked.Increment(ref diagnosticSequence);
                long previousTimestamp = Interlocked.Exchange(ref diagnosticPreviousTimestamp, timestamp);

                eventName = (eventName ?? "").Trim();
                IDOName = (IDOName ?? "").Trim();

                string source = DiagnosticEventNames.Contains(eventName) ? SourceDiagPrefix + eventName : SourceDiagOther;
                bool rowPointerSent = !string.IsNullOrWhiteSpace(rowPointer);
                bool keysSent = HasAnyKeyValue(keyValues);

                Dictionary<string, object> details = new Dictionary<string, object>()
                {
                    { "event", eventName },
                    { "mode", (mode ?? "").Trim() },
                    { "ido", IDOName },
                    { "rowPointer", rowPointer ?? "" },
                    { "keyValues", keyValues ?? "" },
                    { "itemId", itemID ?? "" },
                    { "utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) },
                    { "sequence", sequence },
                    { "msSincePreviousEvent", previousTimestamp > 0 ? (object)Math.Round((timestamp - previousTimestamp) * 1000.0 / Stopwatch.Frequency, 3) : null },
                    { "machine", SafeRead(() => Environment.MachineName) },
                    { "processId", SafeRead(() => Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)) },
                    { "processStartedUtc", diagnosticProcessStartedUtc },
                    { "appDomain", SafeRead(() => AppDomain.CurrentDomain.Id.ToString(CultureInfo.InvariantCulture)) },
                    { "threadId", Thread.CurrentThread.ManagedThreadId },
                    { "sessionId", SafeRead(() => Convert.ToString(IDORuntime.Context.SessionID, CultureInfo.InvariantCulture)) },
                    { "initiatorType", SafeRead(() => Convert.ToString(IDORuntime.Context.InitiatorType, CultureInfo.InvariantCulture)) },
                    { "initiator", SafeRead(() => Convert.ToString(IDORuntime.Context.Initiator, CultureInfo.InvariantCulture)) },
                    { "transactionId", SafeRead(() => System.Transactions.Transaction.Current == null ? "(none)" : System.Transactions.Transaction.Current.TransactionInformation.LocalIdentifier) },
                    { "transactionIsolation", SafeRead(() => System.Transactions.Transaction.Current == null ? "(none)" : System.Transactions.Transaction.Current.IsolationLevel.ToString()) }
                };

                string summary = eventName + (mode != null && mode.Trim() != "" ? " (" + mode.Trim() + ")" : "")
                    + ": RowPointer " + (rowPointerSent ? "sent" : "blank")
                    + ", keys " + (keysSent ? "sent" : "blank")
                    + ", ItemID " + (string.IsNullOrWhiteSpace(itemID) ? "blank" : "sent") + ".";

                string payload = JsonConvert.SerializeObject(details);

                if (payload.Length > MaxPayloadLength)
                {
                    payload = payload.Substring(0, MaxPayloadLength);
                }

                string recordID = (keyValues ?? "").Trim();

                if (recordID.Length > MaxRecordIDLength)
                {
                    recordID = recordID.Substring(0, MaxRecordIDLength);
                }

                Guid rowPointerGuid;
                bool hasRowPointer = Guid.TryParse((rowPointer ?? "").Trim(), out rowPointerGuid);

                SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                    IDOCommands: this.Context.Commands
                );

                sytelineAPI.CreateRecord("ue_AIR_IDOReplicationLogs", sytelineAPI.BuildInsertItem(new Dictionary<string, object>()
                {
                    { "IDOName", IDOName != "" ? IDOName : "(unknown)" },
                    { "CredentialName", NoCredentialName },
                    { "Source", source },
                    { "Operation", DiagnosticOperation(eventName) },
                    { "RecordID", recordID != "" ? recordID : null },
                    { "RecordRowPointer", hasRowPointer ? rowPointerGuid.ToString() : null },
                    { "Payload", payload },
                    { "Status", ReplicationRecordOutcomeStatusSkipped },
                    { "ErrorMessage", summary },
                    { "RetriesRemaining", 0 },
                    { "AttemptCount", 1 }
                }));

            }
            catch (Exception)
            {
                // A DIAGNOSTIC MUST NEVER FAIL THE SAVE THAT FIRED IT. A MISSING ROW IS ITSELF A RESULT: THE TEST GUIDE SAYS TO COUNT ROWS.
            }

            return 0;

        }

        // DIAGNOSTIC ROWS ARE Skipped, SO THE RETRY TASK AND THE RESEND FORM NEVER ACT ON THEM, AND THE RETRY TASK'S PURGE REMOVES THEM LIKE
        // ANY OTHER Skipped ROW

        private const string ReplicationRecordOutcomeStatusSkipped = "Skipped";

        // THE Operation COLUMN IS REQUIRED AND TAKES Insert, Update, Delete OR Upsert. A COLLECTION EVENT HAS NO OPERATION OF ITS OWN, SO
        // IT IS LOGGED AS Upsert, WHICH MEANS NOTHING ON A DIAGNOSTIC ROW.

        private static string DiagnosticOperation(string eventName)
        {

            if (eventName.EndsWith("Insert", StringComparison.Ordinal))
            {
                return OperationInsert;
            }

            if (eventName.EndsWith("Update", StringComparison.Ordinal))
            {
                return OperationUpdate;
            }

            if (eventName.EndsWith("Delete", StringComparison.Ordinal))
            {
                return OperationDelete;
            }

            return OperationUpsert;

        }

        // THE HANDLER PASSES THE KEYS AS "Name=Value|Name=Value". THEY COUNT AS SENT WHEN ANY VALUE ISN'T BLANK.

        private static bool HasAnyKeyValue(string keyValues)
        {

            return (keyValues ?? "")
                .Split('|')
                .Select(pair => pair.IndexOf('=') >= 0 ? pair.Substring(pair.IndexOf('=') + 1) : "")
                .Any(value => value.Trim() != "");

        }

        private static string SafeRead(Func<string> read)
        {

            try
            {
                return read() ?? "";
            }
            catch (Exception ex)
            {
                return "(unavailable: " + ex.GetType().Name + ")";
            }

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_DiagUpdateCollection
        /* Purpose:  Saves test records through this.Context.Commands.UpdateCollection, the way an extension class
        /*           does, so the diagnostic triggers show how events fire for that kind of caller. All the records
        /*           go in one UpdateCollection request.
        /*
        /*           IDOName          the IDO to save to, for example SLEndtypes
        /*           action           Insert, Update or Delete
        /*           keyProperty      the IDO's key property, for example EndUserType
        /*           keyPrefix        Insert creates keyPrefix001, keyPrefix002 and so on. Update and Delete act on the
        /*                            records whose key starts with keyPrefix. Use a prefix no real record has.
        /*           count            how many records to insert, or the most to update or delete (0 = all that match)
        /*           extraProperties  other properties to set, as Name=Value;Name=Value, sent as text. An insert sets
        /*                            the IDO's required properties this way.
        /*           identify         how Update and Delete name each record: ItemId (the ItemID only, no properties),
        /*                            Keys (the key property only) or KeysAndRowPointer
        /*           failLast         1 makes the last record fail (a duplicate key on insert, a key that doesn't exist
        /*                            on update and delete), so the whole request should roll back
        /*           results          what happened, with how long the UpdateCollection call took
        /*
        /*           Returns 0 when the save went through and 1 when it didn't. Never throws.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_DiagUpdateCollection(string IDOName, string action, string keyProperty, string keyPrefix, int count, string extraProperties, string identify, int failLast, ref string results)
        {

            Stopwatch stopwatch = new Stopwatch();

            try
            {

                IDOName = (IDOName ?? "").Trim();
                keyProperty = (keyProperty ?? "").Trim();
                keyPrefix = (keyPrefix ?? "").Trim();
                identify = (identify ?? "").Trim();
                string normalizedAction = NormalizeOperation(action);

                if (IDOName == "" || keyProperty == "" || keyPrefix == "")
                {
                    results = "IDOName, keyProperty and keyPrefix are all required.";
                    return 1;
                }

                if (normalizedAction == OperationUpsert)
                {
                    results = "action must be Insert, Update or Delete.";
                    return 1;
                }

                if (count < 0)
                {
                    results = "count can't be negative.";
                    return 1;
                }

                Dictionary<string, string> extras = ParseNameValuePairs(extraProperties);

                SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                    IDOCommands: this.Context.Commands
                );

                UpdateCollectionRequestData request = new UpdateCollectionRequestData(IDOName)
                {
                    RefreshAfterUpdate = true
                };

                if (normalizedAction == OperationInsert)
                {

                    if (count == 0)
                    {
                        results = "Insert needs a count above 0.";
                        return 1;
                    }

                    // THE NUMBER AFTER THE PREFIX HAS 3 DIGITS, OR MORE WHEN count NEEDS THEM. KEEP THE PREFIX SHORT ENOUGH FOR THE KEY'S
                    // LENGTH (SLEndtypes' EndUserType HOLDS 6 CHARACTERS, SO A 2-CHARACTER PREFIX LEAVES ROOM FOR 9,999 RECORDS).

                    string numberFormat = new string('0', Math.Max(3, count.ToString(CultureInfo.InvariantCulture).Length));

                    for (int i = 1; i <= count; i++)
                    {

                        // THE LAST RECORD REPEATS THE FIRST KEY WHEN failLast IS SET, SO THE INSERT FAILS ON A DUPLICATE KEY

                        string key = keyPrefix + (failLast == 1 && i == count && count > 1 ? 1 : i).ToString(numberFormat, CultureInfo.InvariantCulture);

                        Dictionary<string, object> properties = new Dictionary<string, object>()
                        {
                            { keyProperty, key }
                        };

                        foreach (KeyValuePair<string, string> extra in extras)
                        {
                            properties[extra.Key] = extra.Value;
                        }

                        request.Items.Add(sytelineAPI.BuildInsertItem(properties));

                    }

                }
                else
                {

                    if (identify != "ItemId" && identify != "Keys" && identify != "KeysAndRowPointer")
                    {
                        results = "identify must be ItemId, Keys or KeysAndRowPointer.";
                        return 1;
                    }

                    LoadRecordsResponseData existing = sytelineAPI.LoadRecords(
                        IDOName: IDOName,
                        filter: keyProperty + " LIKE " + sytelineAPI.QuoteFilterValue(keyPrefix + "%"),
                        orderBy: keyProperty,
                        properties: new List<string>() { keyProperty, "RowPointer" },
                        recordCap: count == 0 ? SytelineInternalAPI.NoRecordCap : count
                    );

                    if (existing.Items.Count == 0)
                    {
                        results = "No " + IDOName + " record has a " + keyProperty + " that starts with '" + keyPrefix + "'.";
                        return 1;
                    }

                    for (int i = 0; i < existing.Items.Count; i++)
                    {

                        IDOItem item = existing.Items[i];
                        bool failThisOne = failLast == 1 && i == existing.Items.Count - 1;
                        string existingKey = item.PropertyValues[existing.PropertyKeys[keyProperty]].Value ?? "";

                        // A KEY THAT CAN'T EXIST, THE SAME LENGTH AS A REAL ONE SO IT FITS THE COLUMN: THE PREFIX, THEN TILDES

                        string key = failThisOne
                            ? keyPrefix + new string('~', Math.Max(1, existingKey.Length - keyPrefix.Length))
                            : existingKey;
                        string recordRowPointer = item.PropertyValues[existing.PropertyKeys["RowPointer"]].Value;

                        UpdateAction updateAction = normalizedAction == OperationDelete ? UpdateAction.Delete : UpdateAction.Update;
                        IDOUpdateItem updateItem;

                        if (identify == "ItemId" && !failThisOne)
                        {
                            updateItem = new IDOUpdateItem(updateAction, item.ItemID)
                            {
                                ItemID = item.ItemID
                            };
                        }
                        else
                        {

                            // NAMED BY ITS KEY, WITH NO ItemID, AS A REST CALLER THAT SENDS AN EMPTY ItemId DOES

                            updateItem = new IDOUpdateItem(updateAction)
                            {
                                UseOptimisticLocking = false
                            };

                            updateItem.Properties.Add(keyProperty, key, false);

                            if (identify == "KeysAndRowPointer" && !failThisOne)
                            {
                                updateItem.Properties.Add("RowPointer", recordRowPointer, false);
                            }

                        }

                        if (updateAction == UpdateAction.Update)
                        {
                            foreach (KeyValuePair<string, string> extra in extras)
                            {
                                updateItem.Properties.Add(extra.Key, extra.Value, true);
                            }
                        }

                        request.Items.Add(updateItem);

                    }

                }

                stopwatch.Start();
                UpdateCollectionResponseData response = this.Context.Commands.UpdateCollection(request);
                stopwatch.Stop();

                results = normalizedAction + " of " + request.Items.Count + " " + IDOName + " record(s) went through in "
                    + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms"
                    + (response != null && response.Items != null ? " (" + response.Items.Count + " returned)." : ".");
                return 0;

            }
            catch (Exception ex)
            {

                stopwatch.Stop();
                results = "The save failed after " + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms: " + this.DescribeException(ex);
                return 1;

            }

        }

        // READS Name=Value;Name=Value. A PAIR WITHOUT A NAME IS LEFT OUT.

        private static Dictionary<string, string> ParseNameValuePairs(string text)
        {

            Dictionary<string, string> pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string pair in (text ?? "").Split(';'))
            {

                int equalsIndex = pair.IndexOf('=');

                if (equalsIndex > 0)
                {

                    string name = pair.Substring(0, equalsIndex).Trim();

                    if (name != "")
                    {
                        pairs[name] = pair.Substring(equalsIndex + 1);
                    }

                }

            }

            return pairs;

        }

    }

}
