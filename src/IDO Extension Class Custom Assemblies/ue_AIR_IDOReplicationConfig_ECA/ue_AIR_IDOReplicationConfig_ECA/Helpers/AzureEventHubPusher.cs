using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ue_AIR_IDOReplicationConfig_ECA.Models.AzureEventHubAPI;

namespace ue_AIR_IDOReplicationConfig_ECA.Helpers
{

    /***********************************************************************************************************/
    /*
    /* Sends records to an Azure Event Hub through its REST batch endpoint. Ported from SyteBridge's
    /* AzureEventHubPusher, rewritten for .NET Framework 4.8 / C# 7.3 and made fully synchronous, because it runs
    /* inside IDO extension class methods.
    /*
    /* When an Event Schema URI is given, each event carries CloudEvents user properties (type, source,
    /* dataschema, ...), which Fabric Event Schema Eventstreams require. Without one, plain events are sent, which
    /* standard and inferred multi-schema Eventstreams accept.
    /*
    /***********************************************************************************************************/

    public static class AzureEventHubPusher
    {

        // THE BATCH SIZE LIMIT DEPENDS ON THE EVENT HUBS TIER: 256 KB FOR BASIC, 1 MB FOR STANDARD AND ABOVE. THE
        // DEFAULT IS SAFE FOR EVERY TIER (ABOUT 15% UNDER 256 KB). CALLERS CAN RAISE IT FOR HIGHER TIERS, UP TO THE
        // MAXIMUM, WHICH STAYS ABOUT 15% UNDER 1 MB.

        public const int DefaultMaxBatchBytes = 220000;

        public const int MaximumMaxBatchBytes = 850000;

        // ONE SHARED CLIENT FOR THE WHOLE APP DOMAIN. CREATING A CLIENT PER CALL EXHAUSTS SOCKETS UNDER LOAD.

        private static readonly HttpClient SharedHttpClient = new HttpClient();

        // CREDENTIALS ARE CACHED BY CONNECTION STRING SO THEIR SAS TOKENS ARE REUSED UNTIL THEY'RE CLOSE TO EXPIRING

        private static readonly ConcurrentDictionary<string, AzureEventHubSASCredential> CachedCredentials = new ConcurrentDictionary<string, AzureEventHubSASCredential>();



        public static AzureEventHubSASCredential GetCredential(string connectionString)
        {
            return AzureEventHubPusher.CachedCredentials.GetOrAdd(connectionString ?? "", key => new AzureEventHubSASCredential(key));
        }



        // operation IS Insert, Update, Delete OR BLANK (UNKNOWN). WHEN SET, EVERY EVENT CARRIES IT AS THE "operation" USER
        // PROPERTY, WHICH FABRIC EVENTSTREAMS KEEP IN EVENT METADATA WHEN THE SOURCE USES EXTENDED FEATURES.
        // operationFieldName IS OPTIONAL. WHEN SET, THE OPERATION IS ALSO WRITTEN INTO EVERY EVENT BODY UNDER THAT NAME (BLANK
        // WHEN UNKNOWN), SO DESTINATIONS THAT ONLY READ THE BODY SEE IT. AN EVENT SCHEMA MUST THEN INCLUDE THAT FIELD.

        public static AzureEventHubPushResult Push(AzureEventHubSASCredential credential, List<Dictionary<string, object>> records, string idFieldName = null, FabricEventSchemaUri eventSchemaUri = null, string eventSource = null, int maxBatchBytes = AzureEventHubPusher.DefaultMaxBatchBytes, string operation = null, string operationFieldName = null)
        {

            operation = (operation ?? "").Trim();
            operationFieldName = (operationFieldName ?? "").Trim();

            AzureEventHubPushResult result = new AzureEventHubPushResult(records.Count);

            if (maxBatchBytes <= 0)
            {
                maxBatchBytes = AzureEventHubPusher.DefaultMaxBatchBytes;
            }

            maxBatchBytes = Math.Min(maxBatchBytes, AzureEventHubPusher.MaximumMaxBatchBytes);

            if (eventSchemaUri != null && string.IsNullOrWhiteSpace(eventSource))
            {
                throw new ArgumentException("An event source is required when an Event Schema URI is set.");
            }

            string pushTimestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            List<Dictionary<string, object>> recordBatch = new List<Dictionary<string, object>>();
            List<string> serializedEventBatch = new List<string>();
            int serializedEventBatchBytes = Encoding.UTF8.GetByteCount("[]");

            foreach (Dictionary<string, object> record in records)
            {

                // THE BODY IS THE RECORD, PLUS THE OPERATION FIELD WHEN THE RULE NAMES ONE. THE RECORD ITSELF ISN'T CHANGED.

                Dictionary<string, object> body = record;

                if (operationFieldName != "")
                {
                    body = new Dictionary<string, object>(record);
                    body[operationFieldName] = operation;
                }

                Dictionary<string, object> eventEnvelope = new Dictionary<string, object>()
                {
                    { "Body", JsonConvert.SerializeObject(body) }
                };

                Dictionary<string, string> userProperties = new Dictionary<string, string>();

                if (eventSchemaUri != null)
                {
                    userProperties["cloudEvents:specversion"] = "1.0";
                    userProperties["cloudEvents:type"] = eventSchemaUri.SchemaName;
                    userProperties["cloudEvents:source"] = eventSource.Trim();
                    userProperties["cloudEvents:id"] = Guid.NewGuid().ToString();
                    userProperties["cloudEvents:time"] = pushTimestamp;
                    userProperties["cloudEvents:datacontenttype"] = "application/json";
                    userProperties["cloudEvents:dataschema"] = eventSchemaUri.Url;
                }

                if (operation != "")
                {
                    userProperties["operation"] = operation;
                }

                if (userProperties.Count > 0)
                {
                    eventEnvelope["UserProperties"] = userProperties;
                }

                string serializedEvent = JsonConvert.SerializeObject(eventEnvelope);
                int serializedEventBytes = Encoding.UTF8.GetByteCount(serializedEvent) + 1; // + 1 FOR THE COMMA SEPARATOR

                // SEND THE CURRENT BATCH FIRST IF THIS EVENT WOULD PUSH IT OVER THE LIMIT. A SINGLE EVENT LARGER THAN
                // THE LIMIT IS STILL SENT ON ITS OWN. IF IT'S ALSO OVER THE TIER'S REAL LIMIT, THE EVENT HUB'S REJECTION
                // IS RECORDED AS A FAILURE.

                if (serializedEventBatch.Count > 0 && serializedEventBatchBytes + serializedEventBytes > maxBatchBytes)
                {

                    AzureEventHubPusher.SendBatch(credential, recordBatch, serializedEventBatch, idFieldName, result);

                    recordBatch = new List<Dictionary<string, object>>();
                    serializedEventBatch = new List<string>();
                    serializedEventBatchBytes = Encoding.UTF8.GetByteCount("[]");

                }

                recordBatch.Add(record);
                serializedEventBatch.Add(serializedEvent);
                serializedEventBatchBytes += serializedEventBytes;

            }

            if (serializedEventBatch.Count > 0)
            {
                AzureEventHubPusher.SendBatch(credential, recordBatch, serializedEventBatch, idFieldName, result);
            }

            return result;

        }



        private static void SendBatch(AzureEventHubSASCredential credential, List<Dictionary<string, object>> recordBatch, List<string> serializedEventBatch, string idFieldName, AzureEventHubPushResult result)
        {

            string failureMessage = null;

            try
            {

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, credential.MessagesUri + "?timeout=60&api-version=2014-01"))
                {

                    request.Headers.TryAddWithoutValidation("Authorization", credential.Token);
                    request.Content = new StringContent("[" + string.Join(",", serializedEventBatch) + "]", Encoding.UTF8);
                    request.Content.Headers.Remove("Content-Type");
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/vnd.microsoft.servicebus.json");

                    using (HttpResponseMessage response = AzureEventHubPusher.SharedHttpClient.SendAsync(request).GetAwaiter().GetResult())
                    {

                        // THE BATCH ENDPOINT SIGNALS ACCEPTANCE WITH 201 CREATED SPECIFICALLY. ANYTHING ELSE IS A REJECTION.

                        if (response.StatusCode != HttpStatusCode.Created)
                        {

                            string responseBody = (response.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "").Trim();

                            if (responseBody.Length > 500)
                            {
                                responseBody = responseBody.Substring(0, 500) + "...";
                            }

                            failureMessage = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + (responseBody != "" ? ": " + responseBody : "");

                        }

                    }

                }

            }
            catch (Exception ex)
            {

                Exception baseException = ex.GetBaseException();
                failureMessage = baseException.GetType().Name + ": " + baseException.Message;

            }

            result.AddBatch(
                recordBatch.Select(record => AzureEventHubPusher.GetRecordID(record, idFieldName)).ToList(),
                failureMessage
            );

        }



        private static string GetRecordID(Dictionary<string, object> record, string idFieldName)
        {

            // USE THE ID FIELD IF THE RULE NAMES ONE AND THE RECORD HAS IT, OTHERWISE FALL BACK TO THE FIRST FIELD

            object idValue = null;

            if (!string.IsNullOrEmpty(idFieldName) && record.ContainsKey(idFieldName))
            {
                idValue = record[idFieldName];
            }
            else if (record.Count > 0)
            {
                idValue = record.First().Value;
            }

            return idValue != null ? (Convert.ToString(idValue, CultureInfo.InvariantCulture) ?? "") : "";

        }

    }

}
