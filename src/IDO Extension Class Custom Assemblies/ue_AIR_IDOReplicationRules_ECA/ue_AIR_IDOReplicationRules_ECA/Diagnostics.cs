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

                SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                    IDOCommands: this.Context.Commands
                );

                IDOUpdateItem diagnosticRow = sytelineAPI.BuildInsertItem(new Dictionary<string, object>()
                {
                    { "IDOName", IDOName != "" ? IDOName : "(unknown)" },
                    { "CredentialName", NoCredentialName },
                    { "Source", source },
                    { "Operation", DiagnosticOperation(eventName) },
                    { "RecordID", recordID != "" ? recordID : null },
                    { "Payload", payload },
                    { "Status", ReplicationRecordOutcomeStatusSkipped },
                    { "ErrorMessage", summary },
                    { "RetriesRemaining", 0 },
                    { "AttemptCount", 1 }
                });

                SetRecordRowPointer(diagnosticRow, (rowPointer ?? "").Trim());

                sytelineAPI.CreateRecord("ue_AIR_IDOReplicationLogs", diagnosticRow);

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
        /* Name:     ue_DiagRingDoorbell
        /* Purpose:  Called by the diagnostic doorbell handler (IdoOnUpdateCollection, Ignore Failure off), once per
        /*           save. Tests whether a save can start a background task that runs right after the save commits.
        /*           It writes a "doorbell" row to the replication log, then submits the ue_AIR_DiagDoorbellTask
        /*           background task through BGTaskDefinitions.BGTaskSubmit2, then writes a row with what the
        /*           submission returned, all inside the save's transaction when the handler's writes are in it. The
        /*           task (ue_DiagDoorbellTask) looks for the doorbell row, so its own row shows how long after the
        /*           submission it started, and whether it could see the save's writes by then.
        /*
        /*           Record IDs: "Doorbell=<token>", "DoorbellSubmit=<token>" and the task's "DoorbellTask=<token>",
        /*           where the token is new for each save. Never throws. Always returns 0.
        /*
        /*           Before it submits, it looks in ActiveBGTasks for a doorbell task that is queued and hasn't started
        /*           (status QueuedTaskStatus). If there is one, it doesn't submit another: that task starts later and
        /*           can pick up this save too. This is what the burst test (B3) measures. Its DoorbellSubmit row says
        /*           which task it relied on and lists every doorbell task it saw with its status. A failed look
        /*           never skips the submission.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        private const string DoorbellTaskName = "ue_AIR_DiagDoorbellTask";

        private const string DoorbellRecordIDPrefix = "Doorbell=";

        private const string DoorbellSubmitRecordIDPrefix = "DoorbellSubmit=";

        private const string DoorbellTaskRecordIDPrefix = "DoorbellTask=";

        // THE STATUS A SUBMITTED TASK IS GIVEN (SEE THE SUBMISSION BELOW). THAT ActiveBGTasks STILL SHOWS IT THIS WAY UNTIL THE TASK
        // STARTS ISN'T CONFIRMED: THE DoorbellSubmit ROW LISTS THE STATUSES IT SAW, TO FIND OUT.

        private const string QueuedTaskStatus = "READY";

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_DiagRingDoorbell(string eventName = "", string IDOName = "")
        {

            try
            {

                eventName = (eventName ?? "").Trim();
                IDOName = (IDOName ?? "").Trim();

                string token = Guid.NewGuid().ToString("N");
                DateTime submittedUtc = DateTime.UtcNow;

                // THE TASK'S PARAMETERS, IN THE ORDER OF ue_DiagDoorbellTask. NONE OF THEM CAN HOLD A COMMA: THE IDO NAME, A HEX TOKEN AND A
                // NUMBER OF TICKS.

                string taskParms = "BG~TASKID~," + IDOName + "," + token + "," + submittedUtc.Ticks.ToString(CultureInfo.InvariantCulture);

                // THE DOORBELL ROW GOES FIRST, SO IT EXISTS BEFORE THE TASK DOES. THE TASK LOOKS FOR IT, AND A ROW WRITTEN AFTER THE
                // SUBMISSION COULD BE MISSED BY A TASK THAT STARTED AT ONCE, WHICH WOULD LOOK LIKE A TASK THAT STARTED BEFORE THE COMMIT.

                WriteDiagnosticRow(
                    IDOName: IDOName,
                    recordID: DoorbellRecordIDPrefix + token,
                    payload: JsonConvert.SerializeObject(new Dictionary<string, object>()
                    {
                        { "kind", "DoorbellRang" },
                        { "event", eventName },
                        { "ido", IDOName },
                        { "token", token },
                        { "utc", submittedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) },
                        { "machine", SafeRead(() => Environment.MachineName) },
                        { "appDomain", SafeRead(() => AppDomain.CurrentDomain.Id.ToString(CultureInfo.InvariantCulture)) },
                        { "sessionId", SafeRead(() => Convert.ToString(IDORuntime.Context.SessionID, CultureInfo.InvariantCulture)) }
                    }),
                    summary: $"Doorbell on {eventName}: about to submit the doorbell task."
                );

                submittedUtc = DateTime.UtcNow;
                taskParms = "BG~TASKID~," + IDOName + "," + token + "," + submittedUtc.Ticks.ToString(CultureInfo.InvariantCulture);

                // IS A DOORBELL TASK ALREADY QUEUED? IF SO, DON'T SUBMIT ANOTHER.

                string queuedTaskNumber = "";
                List<string> activeTasks = new List<string>();
                string checkError = "";
                Stopwatch checkTimer = Stopwatch.StartNew();

                try
                {

                    SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                        IDOCommands: this.Context.Commands
                    );

                    LoadRecordsResponseData tasks = sytelineAPI.LoadRecords(
                        IDOName: "ActiveBGTasks",
                        filter: "TaskName = '" + DoorbellTaskName + "'",
                        orderBy: "TaskNumber",
                        properties: new List<string>() { "TaskNumber", "TaskStatusCode" },
                        recordCap: SytelineInternalAPI.NoRecordCap
                    );

                    foreach (var task in tasks.Items)
                    {

                        string number = (task.PropertyValues[tasks.PropertyKeys["TaskNumber"]].Value ?? "").Trim();
                        string status = (task.PropertyValues[tasks.PropertyKeys["TaskStatusCode"]].Value ?? "").Trim();

                        activeTasks.Add(number + ":" + status);

                        if (queuedTaskNumber == "" && status.Equals(QueuedTaskStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            queuedTaskNumber = number;
                        }

                    }

                }
                catch (Exception ex)
                {
                    checkError = ex.Message;
                    queuedTaskNumber = "";
                }

                checkTimer.Stop();

                bool skipped = queuedTaskNumber != "";

                string taskID = "";
                string infobar = "";
                string returnValue = "";
                string submitError = "";
                Stopwatch submitTimer = Stopwatch.StartNew();

                // A FAILED LOOK LEFT skipped FALSE, SO IT NEVER STOPS A SUBMISSION

                if (!skipped)
                {

                    try
                    {

                        // BGTaskSubmit2's PARAMETERS, IN ORDER: TaskName, TaskParms1, TaskParms2, Infobar (out), TaskID (out), TaskStatusCode,
                        // TaskProfileParms, RequestingUser, Initiator, PrintPreview, TaskHistoryRowPointer (out), PreviewInterval (out), then the
                        // schedule (SchedStartDateTime through SchedIsEnabled) and BaseTaskNumber, all left blank for a task that runs once, now

                        // A BLANK VALUE IS ADDED AS Add(null, false), NEVER Add(null): A null LITERAL PICKS THE Add(InvokeParameter) OVERLOAD AND
                        // ADDS A null ELEMENT, NOT A PARAMETER WITH NO VALUE, AND THE RUNTIME THROWS ON IT. THE REQUESTING USER IS THE USER WHOSE
                        // SAVE RANG THE DOORBELL, AND "READY" IS THE STATUS INFOR'S OWN STORED PROCEDURES SUBMIT TASKS WITH.

                        InvokeParameterList parameters = new InvokeParameterList();
                        parameters.Add(DoorbellTaskName, false);
                        parameters.Add(taskParms, false);
                        parameters.Add(null, false);
                        parameters.Add("", true);
                        parameters.Add(null, true);
                        parameters.Add("READY", false);
                        parameters.Add(null, false);
                        parameters.Add(SafeRead(() => IDORuntime.Context.UserName), false);
                        parameters.Add("ue_AIR_DiagDoorbell", false);
                        parameters.Add(0, false);
                        parameters.Add(null, true);
                        parameters.Add(null, true);

                        for (int i = 0; i < 10; i++)
                        {
                            parameters.Add(null, false);
                        }

                        InvokeResponseData response = this.Context.Commands.Invoke(new InvokeRequestData
                        {
                            IDOName = "BGTaskDefinitions",
                            MethodName = "BGTaskSubmit2",
                            Parameters = parameters
                        });

                        returnValue = response.ReturnValue ?? "";
                        infobar = response.Parameters.Count > 3 ? (response.Parameters[3].Value ?? "") : "";
                        taskID = response.Parameters.Count > 4 ? (response.Parameters[4].Value ?? "") : "";

                    }
                    catch (Exception ex)
                    {
                        submitError = ex.Message;
                    }

                }

                submitTimer.Stop();

                Dictionary<string, object> details = new Dictionary<string, object>()
                {
                    { "kind", "DoorbellSubmit" },
                    { "event", eventName },
                    { "ido", IDOName },
                    { "token", token },
                    { "submittedUtc", submittedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) },
                    { "submitMs", Math.Round(submitTimer.Elapsed.TotalMilliseconds, 3) },
                    { "skipped", skipped },
                    { "queuedTaskNumber", queuedTaskNumber },
                    { "activeTasks", activeTasks },
                    { "checkMs", Math.Round(checkTimer.Elapsed.TotalMilliseconds, 3) },
                    { "checkError", checkError },
                    { "returnValue", returnValue },
                    { "taskId", taskID },
                    { "infobar", infobar },
                    { "error", submitError },
                    { "machine", SafeRead(() => Environment.MachineName) },
                    { "appDomain", SafeRead(() => AppDomain.CurrentDomain.Id.ToString(CultureInfo.InvariantCulture)) },
                    { "sessionId", SafeRead(() => Convert.ToString(IDORuntime.Context.SessionID, CultureInfo.InvariantCulture)) }
                };

                string seen = activeTasks.Count > 0 ? string.Join(", ", activeTasks) : "none";

                string summary = skipped
                    ? $"Doorbell on {eventName}: not submitted, because doorbell task {queuedTaskNumber} is queued. Doorbell tasks seen (number:status): {seen}. Look took {Math.Round(checkTimer.Elapsed.TotalMilliseconds)} ms."
                    : submitError != ""
                    ? $"Doorbell on {eventName}: the task couldn't be submitted: {submitError}"
                    : $"Doorbell on {eventName}: task {(taskID != "" ? taskID : "(no number)")} submitted in {Math.Round(submitTimer.Elapsed.TotalMilliseconds)} ms, return value {returnValue}" + (infobar != "" ? $", message: {infobar}" : "") + $". Doorbell tasks seen first (number:status): {seen}" + (checkError != "" ? $"; the look failed: {checkError}" : "") + ".";

                WriteDiagnosticRow(
                    IDOName: IDOName,
                    recordID: DoorbellSubmitRecordIDPrefix + token,
                    payload: JsonConvert.SerializeObject(details),
                    summary: summary
                );

            }
            catch (Exception)
            {
                // A DIAGNOSTIC MUST NEVER FAIL THE SAVE THAT FIRED IT. A MISSING DOORBELL ROW IS ITSELF A RESULT.
            }

            return 0;

        }



        /**********************************************************************************************************/
        /**********************************************************************************************************/
        /*
        /* Name:     ue_DiagDoorbellTask
        /* Purpose:  The background task ue_DiagRingDoorbell submits (task name ue_AIR_DiagDoorbellTask). Sends nothing.
        /*           It looks for the save's doorbell row: right away, then again after 1, 2, 5, 10, 20, 30, 45 and 60
        /*           seconds, until it sees the row. Each look records when it began and how long it took, because a
        /*           load can wait on the save's lock and only answer once the save commits. Not seeing the row at
        /*           first, and then seeing it, means the task started before the save committed. So does a first
        /*           look that took a long time. A first look that answered quickly and saw the row means the save
        /*           had committed, or that the load read data that wasn't committed yet: compare the task's start
        /*           with the end of the save to tell.
        /*
        /*           The waits hold a server thread for up to a minute, and Execute is granted on the IDO as a whole, so
        /*           anyone could call this directly with a made-up BGTaskNum. So it waits only when it runs as a
        /*           doorbell task (its BGTaskNum is an ue_AIR_DiagDoorbellTask in ActiveBGTasks) or for a super user.
        /*           Otherwise it looks once and says so. A direct call with no BGTaskNum needs a super user. Then it writes one row, "DoorbellTask=<token>", with when it started, how long
        /*           after the submission that was (by the application servers' clocks), and what each look found.
        /*           Always returns 0.
        /*
        /**********************************************************************************************************/
        /**********************************************************************************************************/

        [IDOMethod(MethodFlags.None, "Infobar")]
        public int ue_DiagDoorbellTask(int BGTaskNum = -1, string IDOName = "", string token = "", string submittedUtcTicks = "")
        {

            try
            {

                if (BGTaskNum <= 0 && !IsSuperUser())
                {
                    return 0;
                }

                DateTime startedUtc = DateTime.UtcNow;
                Stopwatch timer = Stopwatch.StartNew();

                IDOName = (IDOName ?? "").Trim();
                token = (token ?? "").Trim();

                long ticks;
                double? delayMs = long.TryParse((submittedUtcTicks ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)
                    ? (double?)Math.Round((startedUtc - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds, 3)
                    : null;

                SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                    IDOCommands: this.Context.Commands
                );

                bool runningAsDoorbellTask = IsRunningDoorbellTask(sytelineAPI, BGTaskNum);
                bool mayWait = runningAsDoorbellTask || IsSuperUser();

                // THE TOKEN IS HEX, SO IT CAN'T BREAK THE FILTER, BUT A TOKEN THAT ISN'T ONE IS REFUSED ANYWAY

                bool tokenIsValid = token.Length > 0 && token.All(Uri.IsHexDigit);

                List<Dictionary<string, object>> looks = new List<Dictionary<string, object>>();
                int[] lookAfterSeconds = mayWait ? new[] { 0, 1, 2, 5, 10, 20, 30, 45, 60 } : new[] { 0 };
                bool seen = false;

                foreach (int seconds in lookAfterSeconds)
                {

                    if (!tokenIsValid)
                    {
                        break;
                    }

                    int waitMs = seconds * 1000 - (int)timer.ElapsedMilliseconds;

                    if (waitMs > 0)
                    {
                        Thread.Sleep(waitMs);
                    }

                    string error = "";
                    double lookStartedMs = timer.Elapsed.TotalMilliseconds;

                    try
                    {

                        LoadRecordsResponseData doorbellRows = sytelineAPI.LoadRecords(
                            IDOName: "ue_AIR_IDOReplicationLogs",
                            filter: "RecordID = '" + DoorbellRecordIDPrefix + token + "'",
                            orderBy: "",
                            properties: new List<string>() { "RecordID" },
                            recordCap: 1
                        );

                        seen = doorbellRows.Items.Count > 0;

                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }

                    looks.Add(new Dictionary<string, object>()
                    {
                        { "startedAtMs", Math.Round(lookStartedMs) },
                        { "tookMs", Math.Round(timer.Elapsed.TotalMilliseconds - lookStartedMs) },
                        { "afterMs", Math.Round(timer.Elapsed.TotalMilliseconds) },
                        { "doorbellRowVisible", seen },
                        { "error", error }
                    });

                    if (seen)
                    {
                        break;
                    }

                }

                Dictionary<string, object> details = new Dictionary<string, object>()
                {
                    { "kind", "DoorbellTask" },
                    { "taskNumber", BGTaskNum },
                    { "ido", IDOName },
                    { "token", token },
                    { "submittedUtc", delayMs.HasValue ? new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) : "" },
                    { "startedUtc", startedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) },
                    { "msFromSubmitToStart", delayMs },
                    { "runningAsDoorbellTask", runningAsDoorbellTask },
                    { "waited", mayWait },
                    { "looks", looks },
                    { "machine", SafeRead(() => Environment.MachineName) },
                    { "appDomain", SafeRead(() => AppDomain.CurrentDomain.Id.ToString(CultureInfo.InvariantCulture)) }
                };

                Dictionary<string, object> firstLook = looks.FirstOrDefault();
                Dictionary<string, object> lastLook = looks.LastOrDefault();

                string summary = !tokenIsValid
                    ? "Doorbell task: no valid token was passed, so it couldn't look for the doorbell row."
                    : $"Doorbell task {BGTaskNum} started {(delayMs.HasValue ? Math.Round(delayMs.Value) + " ms" : "an unknown time")} after the doorbell. "
                        + (firstLook != null && (bool)firstLook["doorbellRowVisible"] && Convert.ToDouble(firstLook["afterMs"], CultureInfo.InvariantCulture) <= 2000
                            ? "The doorbell row was visible at once."
                            : firstLook != null && (bool)firstLook["doorbellRowVisible"]
                            ? $"The first look saw the doorbell row, but only answered after about {firstLook["afterMs"]} ms: it most likely waited for the save to commit, so the task started before the save committed."
                            : seen
                                ? $"The doorbell row wasn't visible at first and appeared after about {lastLook["afterMs"]} ms: the task started before the save committed."
                                : mayWait
                                    ? "The doorbell row never became visible in 60 seconds: the save may have rolled back, or is still running."
                                    : "The doorbell row wasn't visible. The task couldn't be confirmed as a running doorbell task in ActiveBGTasks, so it looked only once.");

                WriteDiagnosticRow(
                    IDOName: IDOName != "" ? IDOName : "(unknown)",
                    recordID: DoorbellTaskRecordIDPrefix + (tokenIsValid ? token : "(none)"),
                    payload: JsonConvert.SerializeObject(details),
                    summary: summary
                );

            }
            catch (Exception)
            {
                // A MISSING TASK ROW IS ITSELF A RESULT. THE TASK'S OWN HISTORY STILL SHOWS WHEN IT STARTED.
            }

            return 0;

        }

        // WHETHER BGTaskNum IS A DOORBELL TASK THAT ActiveBGTasks LISTS. FALSE WHEN IT ISN'T, OR WHEN THE LOAD FAILS. A RUNNING TASK WAS
        // LISTED THERE IN EVERY DOORBELL TEST, BUT A FALSE STILL ONLY CUTS THE TASK TO ONE LOOK; IT NEVER STOPS IT.

        private static bool IsRunningDoorbellTask(SytelineInternalAPI sytelineAPI, int BGTaskNum)
        {

            if (BGTaskNum <= 0)
            {
                return false;
            }

            try
            {

                LoadRecordsResponseData tasks = sytelineAPI.LoadRecords(
                    IDOName: "ActiveBGTasks",
                    filter: "TaskNumber = " + BGTaskNum.ToString(CultureInfo.InvariantCulture) + " AND TaskName = '" + DoorbellTaskName + "'",
                    orderBy: "",
                    properties: new List<string>() { "TaskNumber" },
                    recordCap: 1
                );

                return tasks.Items.Count > 0;

            }
            catch (Exception)
            {
                return false;
            }

        }

        // WRITES ONE Skipped "Diag: Other" ROW TO THE REPLICATION LOG, FOR THE DOORBELL TEST

        private void WriteDiagnosticRow(string IDOName, string recordID, string payload, string summary)
        {

            if (payload.Length > MaxPayloadLength)
            {
                payload = payload.Substring(0, MaxPayloadLength);
            }

            if (recordID.Length > MaxRecordIDLength)
            {
                recordID = recordID.Substring(0, MaxRecordIDLength);
            }

            SytelineInternalAPI sytelineAPI = new SytelineInternalAPI(
                IDOCommands: this.Context.Commands
            );

            IDOUpdateItem diagnosticRow = sytelineAPI.BuildInsertItem(new Dictionary<string, object>()
            {
                { "IDOName", IDOName != "" ? IDOName : "(unknown)" },
                { "CredentialName", NoCredentialName },
                { "Source", SourceDiagOther },
                { "Operation", OperationUpsert },
                { "RecordID", recordID },
                { "Payload", payload },
                { "Status", ReplicationRecordOutcomeStatusSkipped },
                { "ErrorMessage", summary },
                { "RetriesRemaining", 0 },
                { "AttemptCount", 1 }
            });

            SetRecordRowPointer(diagnosticRow, "");

            sytelineAPI.CreateRecord("ue_AIR_IDOReplicationLogs", diagnosticRow);

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
        /*                            records whose key starts with keyPrefix. Use a prefix no real record has. At
        /*                            least 2 characters, with no LIKE wildcard. Only a super user can run the
        /*                            method, and it refuses this project's own (ue_AIR_) IDOs.
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

                // THIS METHOD CAN DELETE RECORDS, AND EXECUTE IS GRANTED ON THE RULES IDO AS A WHOLE, NOT PER METHOD, SO ONLY A SUPER USER MAY
                // RUN IT. THE PREFIX GOES INTO A LIKE FILTER, SO IT CAN'T HOLD A WILDCARD THAT WOULD WIDEN THE MATCH, AND IT CAN'T BE SHORT.

                if (!IsSuperUser())
                {
                    results = "Only a super user can run ue_DiagUpdateCollection.";
                    return 1;
                }

                if (IDOName == "" || keyProperty == "" || keyPrefix == "")
                {
                    results = "IDOName, keyProperty and keyPrefix are all required.";
                    return 1;
                }

                if (keyPrefix.Length < 2 || keyPrefix.IndexOfAny(new[] { '%', '_', '[', ']', '\'' }) >= 0)
                {
                    results = "keyPrefix must be at least 2 characters, without %, _, [, ] or a quote. Use a prefix no real record starts with.";
                    return 1;
                }

                if (IDOName.StartsWith("ue_AIR_", StringComparison.OrdinalIgnoreCase))
                {
                    results = "ue_DiagUpdateCollection doesn't save to this project's own IDOs.";
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

                    if (failLast == 1 && count < 2)
                    {
                        results = "failLast on Insert needs a count of at least 2: the last record fails by repeating the first key.";
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
