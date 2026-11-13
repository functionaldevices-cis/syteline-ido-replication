using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using ue_AIR_IDOReplicationConfig_ECA.Models;
using ue_AIR_IDOReplicationConfig_ECA.Models.SalesforceRestAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace ue_AIR_IDOReplicationConfig_ECA.Helpers
{

    public class SalesforceRestAPI
    {
        private int StreamingRecordCap
        {
            get; set;
        }

        private int ApiVersion
        {
            get; set;
        } = 61;

        private HttpClient HttpClient
        {
            get; set;
        }

        public SalesforceAPICredential CurrentSalesforceCredential
        {
            get; set;
        }

        private SalesforceAPIAccessTokenDetails AccessTokenDetails
        {
            get; set;
        } = new SalesforceAPIAccessTokenDetails();

        public SalesforceRestAPI(SalesforceAPICredential credential)
        {

            this.CurrentSalesforceCredential = credential;

            // INIT SETTINGS

            this.StreamingRecordCap = 20000;

            // INIT HTTP CLIENT

            this.HttpClient = new HttpClient();
            this.GetAccessToken();

        }

        public SalesforceAPIAccessTokenDetails GetAccessToken(SalesforceAPICredential credential = null)
        {

            if (credential == null)
            {
                credential = this.CurrentSalesforceCredential;
            }

            if (credential != null)
            {

                // CHECK TO SEE IF WE NEED A NEW TOKEN: THERE ISN'T ONE YET, OR THE CURRENT ONE EXPIRES WITHIN THE NEXT 10 MINUTES

                if ((this.AccessTokenDetails.Token == "") || (this.AccessTokenDetails.Expiration == null) || (this.AccessTokenDetails.Expiration.Value <= DateTime.Now.AddMinutes(10)))
                {

                    // TRY TO GET THE TOKEN

                    try
                    {
                        // LOAD THE REQUEST

                        HttpResponseMessage httpResponse;

                        switch (credential.AuthFlow)
                        {

                            case "UsernamePassword":

                                httpResponse = this.HttpClient.PostAsync(
                                    requestUri: new Uri(credential.TokenRequestEndpoint),
                                    content: new FormUrlEncodedContent(new Dictionary<string, string>()
                                    {
                                        { "grant_type", "password" },
                                        { "client_id", credential.ClientId },
                                        { "client_secret", credential.ClientSecret },
                                        { "username", credential.Username }, // Username IS TESTED FOR NULLABILITY WHEN LOADED
                                        { "password", credential.Password + credential.SecurityToken }  // Password AND SecurityToken ARE TESTED FOR NULLABILITY WHEN LOADED
                                    })
                                ).Result;

                                break;

                            case "ClientCredentials":

                                httpResponse = this.HttpClient.PostAsync(
                                    requestUri: new Uri(credential.TokenRequestEndpoint),
                                    content: new FormUrlEncodedContent(new Dictionary<string, string>()
                                    {
                                        { "grant_type", "client_credentials" },
                                        { "client_id", credential.ClientId },
                                        { "client_secret", credential.ClientSecret }
                                    })
                                ).Result;

                                break;

                            default:

                                throw new Exception("Unknown Salesforce auth flow '" + credential.AuthFlow + "'. Expected 'UsernamePassword' or 'ClientCredentials'.");

                        }

                        Dictionary<string, object> parsedResponseContent = JsonConvert.DeserializeObject<Dictionary<string, object>>(httpResponse.Content.ReadAsStringAsync().Result);


                        this.AccessTokenDetails.Valid = httpResponse.IsSuccessStatusCode;

                        if (this.AccessTokenDetails.Valid)
                        {
                            this.AccessTokenDetails.Message = "Successfully connected and authenticated.";
                            this.AccessTokenDetails.Token = (parsedResponseContent["access_token"] ?? "").ToString() ?? "";
                            this.AccessTokenDetails.InstanceURL = (parsedResponseContent["instance_url"] ?? "").ToString() ?? "";
                            this.AccessTokenDetails.Expiration = DateTime.Now.AddSeconds((2 * 3600));
                        }
                        else
                        {
                            this.AccessTokenDetails.Message = $"Unable to load access token. Server Error: '{(parsedResponseContent["error_description"] ?? "").ToString() ?? ""}'";
                            this.AccessTokenDetails.Token = "";
                            this.AccessTokenDetails.InstanceURL = "";
                            this.AccessTokenDetails.Expiration = null;
                        }


                    }
                    catch (Exception ex)
                    {
                        this.AccessTokenDetails.Token = "";
                        this.AccessTokenDetails.InstanceURL = "";
                        this.AccessTokenDetails.Expiration = null;
                        this.AccessTokenDetails.Valid = false;
                        this.AccessTokenDetails.Message = ex.Message;

                    }

                }

            }

            return this.AccessTokenDetails;

        }

        public async Task<SalesforceAPIUpsertResults> UpsertRecords(string objectName, string externalIDFieldName, List<Dictionary<string, object>> records, Action<SalesforceAPIQueryStatus> onStartCallback = null, Action<SalesforceAPIQueryStatus> onProgressCallback = null, Action<SalesforceAPIQueryStatus> onCompleteCallback = null)
        {

            // INIT VARS
            SalesforceAPIUpsertResults results = new SalesforceAPIUpsertResults();
            SalesforceAPIUpsertResponse response;

            // RUN START CALLBACK

            onStartCallback?.Invoke(new SalesforceAPIQueryStatus(
                CountTotal: records.Count,
                CountCompleted: 0
            ));

            // SET UP REUSABLE DATA

            Dictionary<string, object> attributesField = new Dictionary<string, object>() { { "attributes", new Dictionary<string, string>() { { "type", objectName } } } };

            // LOAD THE REQUEST

            for (int i = 0; i < records.Count; i += 200)
            {

                var recordBatch = records.Skip(i).Take(200);

                // UPSERT RECORD BATCH

                response = await this.UpsertRecordBatch(
                    objectName: objectName,
                    externalIDFieldName: externalIDFieldName,
                    recordsPackage: new SalesforceAPIUpsertPackage(recordBatch.Select(record => attributesField.Concat(record).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value)).ToList())
                );

                results.AddResponse(response);


                // RUN PROGRESS CALLBACK

                onProgressCallback?.Invoke(new SalesforceAPIQueryStatus(
                    CountTotal: records.Count,
                    CountCompleted: i + recordBatch.Count(),
                    CountBatch: recordBatch.Count(),
                    Success: response.success,
                    ErrorCode: response.errorCode,
                    ErrorMessage: response.message,
                    RecordDetails: response.recordResults
                ));

            }

            // RUN COMPLETE CALLBACK

            onCompleteCallback?.Invoke(new SalesforceAPIQueryStatus(
                CountTotal: records.Count,
                CountCompleted: records.Count
            ));

            return results;

        }

        public async Task<SalesforceAPIUpsertResponse> UpsertRecordBatch(string objectName, string externalIDFieldName, SalesforceAPIUpsertPackage recordsPackage)
        {

            SalesforceAPIUpsertResponse response;

            SalesforceAPIAccessTokenDetails accessToken = this.GetAccessToken();
            if (accessToken.Valid)
            {

                string urlDomain = accessToken.InstanceURL;
                string urlPath = $"/services/data/v{this.ApiVersion:0.0}/composite/sobjects/{objectName}/{externalIDFieldName}";

                // SEND THE REQUEST

                HttpRequestMessage request = new HttpRequestMessage()
                {
                    Method = new HttpMethod("PATCH"),
                    RequestUri = new Uri($"{urlDomain}{urlPath}"),
                    Headers =
                    {
                        { "Authorization", "Bearer " + accessToken.Token }
                    },
                    Content = new StringContent(
                        content:  JsonConvert.SerializeObject(recordsPackage)
                    )
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage httpResponse = await this.HttpClient.SendAsync(
                    request
                );

                string responseContent = await httpResponse.Content.ReadAsStringAsync();

                if (httpResponse.IsSuccessStatusCode)
                {
                    response = new SalesforceAPIUpsertResponse(
                        successResponse: JsonConvert.DeserializeObject<List<SalesforceAPIUpsertResponseRecordResult>>(responseContent) ?? throw new Exception("Unable to parse response.")
                    );

                    // RESULTS COME BACK IN THE ORDER SENT. A RECORD MAY NOT MAP THE EXTERNAL ID FIELD, OR MAY HAVE IT EMPTY.

                    for (int counter = 0; counter < response.recordResults.Count(); counter++)
                    {
                        object externalIDValue = null;

                        if (counter < recordsPackage.records.Count && recordsPackage.records[counter].ContainsKey(externalIDFieldName))
                        {
                            externalIDValue = recordsPackage.records[counter][externalIDFieldName];
                        }

                        response.recordResults[counter]["externalID"] = Convert.ToString(externalIDValue, CultureInfo.InvariantCulture) ?? "";
                    }
                }
                else
                {
                    response = new SalesforceAPIUpsertResponse(
                        errorResponse: JsonConvert.DeserializeObject<SalesforceAPIQueryResponseError>(responseContent) ?? throw new Exception("Unable to parse response.")
                    );
                }

            }
            else
            {

                response = new SalesforceAPIUpsertResponse(
                    errorMessage: accessToken.Message
                );

            }

            return response;

        }

        /**********************************************************************************************************/
        /*
        /* DELETING BY EXTERNAL ID. Ported from sfbridge's DeleteRecords (Bridjer.APIs.Salesforce), including its
        /* fixes: batches of 25 (the Composite API's subrequest limit), generated reference IDs, URL-escaped values,
        /* records without a value reported as failures, and subresponse bodies of any shape (an external ID that
        /* matches several records answers HTTP 300 with a list of record URLs).
        /*
        /**********************************************************************************************************/

        public async Task<SalesforceAPIUpsertResults> DeleteRecords(string objectName, string externalIDFieldName, List<Dictionary<string, object>> records)
        {

            SalesforceAPIUpsertResults results = new SalesforceAPIUpsertResults();

            for (int i = 0; i < records.Count; i += 25)
            {
                results.AddResponse(await this.DeleteRecordBatch(
                    objectName: objectName,
                    externalIDFieldName: externalIDFieldName,
                    records: records.Skip(i).Take(25).ToList()
                ));
            }

            return results;

        }

        public async Task<SalesforceAPIUpsertResponse> DeleteRecordBatch(string objectName, string externalIDFieldName, List<Dictionary<string, object>> records)
        {

            // SPLIT THE BATCH INTO RECORDS WITH AN EXTERNAL ID TO DELETE BY AND RECORDS WITHOUT ONE

            List<string> keyValues = new List<string>();
            List<Dictionary<string, string>> recordResults = new List<Dictionary<string, string>>();

            foreach (Dictionary<string, object> record in records)
            {

                string keyValue = SalesforceRestAPI.GetFieldValue(record, externalIDFieldName);

                if (keyValue != "")
                {
                    keyValues.Add(keyValue);
                }
                else
                {
                    recordResults.Add(SalesforceRestAPI.BuildDeleteResult("", false, "MISSING_KEY", "The record has no " + externalIDFieldName + " value, so it can't be deleted."));
                }

            }

            if (keyValues.Count == 0)
            {
                return SalesforceRestAPI.BuildDeleteResponse(recordResults);
            }

            SalesforceAPIAccessTokenDetails accessToken = this.GetAccessToken();

            if (!accessToken.Valid)
            {
                SalesforceAPIUpsertResponse tokenFailure = new SalesforceAPIUpsertResponse(errorMessage: accessToken.Message);
                tokenFailure.recordResults = recordResults;
                return tokenFailure;
            }

            // ONE DELETE SUBREQUEST PER RECORD. REFERENCE IDS ARE GENERATED (r0, r1, ...) BECAUSE SALESFORCE ONLY ALLOWS LETTERS,
            // DIGITS AND UNDERSCORES THERE, STARTING WITH A LETTER, WHICH EXTERNAL IDS OFTEN AREN'T.

            string apiPath = "/services/data/v" + this.ApiVersion.ToString("0.0", CultureInfo.InvariantCulture);

            Dictionary<string, object> compositeRequest = new Dictionary<string, object>()
            {
                { "allOrNone", false },
                { "compositeRequest", keyValues.Select((keyValue, index) => new Dictionary<string, string>() {
                    { "method", "DELETE" },
                    { "url", apiPath + "/sobjects/" + Uri.EscapeDataString(objectName) + "/" + Uri.EscapeDataString(externalIDFieldName) + "/" + Uri.EscapeDataString(keyValue) },
                    { "referenceId", "r" + index.ToString(CultureInfo.InvariantCulture) }
                }).ToList() }
            };

            HttpRequestMessage request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(accessToken.InstanceURL + apiPath + "/composite"),
                Headers =
                {
                    { "Authorization", "Bearer " + accessToken.Token }
                },
                Content = new StringContent(JsonConvert.SerializeObject(compositeRequest))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage httpResponse = await this.HttpClient.SendAsync(request);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            if (!httpResponse.IsSuccessStatusCode)
            {

                SalesforceAPIUpsertResponse batchFailure;

                try
                {
                    batchFailure = new SalesforceAPIUpsertResponse(
                        errorResponse: JsonConvert.DeserializeObject<SalesforceAPIQueryResponseError>(responseContent)
                    );
                }
                catch (JsonException)
                {
                    batchFailure = new SalesforceAPIUpsertResponse(
                        errorMessage: "HTTP " + (int)httpResponse.StatusCode + ": " + SalesforceRestAPI.Trim(responseContent)
                    );
                }

                batchFailure.recordResults = recordResults;
                return batchFailure;

            }

            // MAP EACH SUBRESPONSE BACK TO ITS EXTERNAL ID THROUGH THE GENERATED REFERENCE ID

            JToken compositeResponse = JObject.Parse(responseContent)["compositeResponse"];

            foreach (JToken subresponse in (compositeResponse as JArray) ?? new JArray())
            {

                string referenceId = (string)subresponse["referenceId"] ?? "";
                int httpStatusCode = (int?)subresponse["httpStatusCode"] ?? 0;
                int index;

                if (!(referenceId.StartsWith("r") && int.TryParse(referenceId.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)))
                {
                    index = -1;
                }

                string externalID = index >= 0 && index < keyValues.Count ? keyValues[index] : referenceId;

                if (httpStatusCode == 204)
                {
                    recordResults.Add(SalesforceRestAPI.BuildDeleteResult(externalID, true, "", ""));
                }
                else
                {
                    KeyValuePair<string, string> error = SalesforceRestAPI.DescribeSubresponseError(subresponse["body"], httpStatusCode);
                    recordResults.Add(SalesforceRestAPI.BuildDeleteResult(externalID, false, error.Key, error.Value));
                }

            }

            return SalesforceRestAPI.BuildDeleteResponse(recordResults);

        }

        // TURNS A FAILED COMPOSITE SUBRESPONSE BODY INTO AN ERROR CODE AND MESSAGE, WHATEVER ITS SHAPE

        private static KeyValuePair<string, string> DescribeSubresponseError(JToken body, int httpStatusCode)
        {

            string statusCode = httpStatusCode.ToString(CultureInfo.InvariantCulture);
            JArray bodyArray = body as JArray;

            if (bodyArray != null && bodyArray.Count > 0)
            {

                // MOST FAILURES: [{ "errorCode": "...", "message": "..." }]

                if (bodyArray[0].Type == JTokenType.Object)
                {
                    return new KeyValuePair<string, string>(
                        (string)bodyArray[0]["errorCode"] ?? statusCode,
                        (string)bodyArray[0]["message"] ?? ""
                    );
                }

                // HTTP 300: THE EXTERNAL ID ISN'T UNIQUE, AND THE BODY LISTS THE URLS OF THE MATCHING RECORDS

                if (bodyArray[0].Type == JTokenType.String)
                {
                    List<string> urls = bodyArray.Where(item => item.Type == JTokenType.String).Select(item => (string)item).ToList();
                    return new KeyValuePair<string, string>(statusCode, "The external ID matches " + urls.Count + " records, so none were deleted: " + string.Join(", ", urls));
                }

            }

            // ANY OTHER SHAPE: REPORT THE STATUS CODE AND THE RAW BODY, TRIMMED

            return new KeyValuePair<string, string>(statusCode, body != null && body.Type != JTokenType.Null ? SalesforceRestAPI.Trim(body.ToString(Formatting.None)) : "");

        }

        // READS A FIELD FROM A RECORD AS A TRIMMED STRING, MATCHING THE NAME WITHOUT REGARD TO CASE, AS SALESFORCE DOES

        private static string GetFieldValue(Dictionary<string, object> record, string fieldName)
        {

            foreach (KeyValuePair<string, object> field in record)
            {
                if (string.Equals(field.Key, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    return (Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "").Trim();
                }
            }

            return "";

        }

        private static Dictionary<string, string> BuildDeleteResult(string externalID, bool success, string errorCode, string errorMessage)
        {
            return new Dictionary<string, string>()
            {
                { "id", externalID },
                { "externalID", externalID },
                { "success", success.ToString() },
                { "operation", "Delete" },
                { "errorCode", errorCode },
                { "errorMessage", errorMessage }
            };
        }

        private static SalesforceAPIUpsertResponse BuildDeleteResponse(List<Dictionary<string, string>> recordResults)
        {
            SalesforceAPIUpsertResponse response = new SalesforceAPIUpsertResponse(successResponse: new List<SalesforceAPIUpsertResponseRecordResult>());
            response.recordResults = recordResults;
            return response;
        }

        private static string Trim(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 500 ? text.Substring(0, 500) + "..." : text;
        }




        public async Task<SalesforceAPILoadResults> LoadRecords(string objectName, List<string> fieldNames, string filter = null, List<SalesforceAPILoadRequestOrderByField> orderBy = null, int? recordCap = null, Action<SalesforceAPIQueryStatus> onStartCallback = null, Action<SalesforceAPIQueryStatus> onProgressCallback = null, Action<SalesforceAPIQueryStatus> onCompleteCallback = null, bool reportCompletionPercentage = false)
        {

            // INIT VARS
            SalesforceAPILoadResults results = new SalesforceAPILoadResults();
            SalesforceAPILoadResponse response;

            int requestCap = recordCap != null ? Math.Min((int)recordCap, this.StreamingRecordCap) : this.StreamingRecordCap;
            bool haveToPaginate = recordCap == null || recordCap >= this.StreamingRecordCap;
            bool totalCapNotMet = true;
            bool moreRowsExist = true;
            List<Dictionary<string, object>> data = new List<Dictionary<string, object>>();

            // IF WE ARE LOADING VIA SOAP, WE NEED TO ENSURE THAT THE ROWPOINTER IS PART OF THE REQUEST, BECAUSE
            // THAT IS WHAT WE USE FOR PAGINATION, SINCE SOAP DOESN'T HAVE PAGINATION OUT OF BOX

            onStartCallback?.Invoke(new SalesforceAPIQueryStatus(
                CountCompleted: 0
            ));

            do
            {

                // SEND REQUEST AND RETRIEVE RECORDS

                response = await this.LoadRecordBatch(
                    objectName: objectName,
                    fieldNames: fieldNames,
                    filter: filter,
                    orderBy: orderBy,
                    recordCap: recordCap
                );

                results.AddResponse(response);


                // SEND REQUEST AND RETRIEVE RECORDS

                onProgressCallback?.Invoke(new SalesforceAPIQueryStatus(
                    CountTotal: results.totalSize,
                    CountCompleted: results.recordCount
                ));

                if (recordCap != null)
                {
                    if (recordCap <= results.recordCount)
                    {
                        totalCapNotMet = false;
                    }
                }

                moreRowsExist = !results.done;

            } while (moreRowsExist && totalCapNotMet);

            onCompleteCallback?.Invoke(new SalesforceAPIQueryStatus(
                CountTotal: results.totalSize,
                CountCompleted: results.recordCount
            ));

            return results;

        }

        public async Task<SalesforceAPILoadResponse> LoadRecordBatch(string objectName, List<string> fieldNames, string filter = null, List<SalesforceAPILoadRequestOrderByField> orderBy = null, int? recordCap = null, string urlPathOverride = null)
        {

            // BUILD ORDER BY STRING

            string orderByString = string.Join(", ", (orderBy ?? new List<SalesforceAPILoadRequestOrderByField>()).Select(field => field.OrderBy));

            // BUILD QUERY

            string query = $"SELECT {string.Join(",", fieldNames)} FROM {objectName} {(filter != null ? $"WHERE {filter}" : "")} {(orderBy != null ? $"ORDER BY {orderByString}" : "")}";

            // BUILD REUEST URL

            SalesforceAPIAccessTokenDetails accessToken = this.GetAccessToken();
            if (accessToken.Valid)
            {

                string urlDomain = this.GetAccessToken().InstanceURL;
                string urlPath = urlPathOverride ?? $"/services/data/v{this.ApiVersion:0.0}/query?q={HttpUtility.UrlEncode(query)}";

                // LOAD THE REQUEST

                HttpResponseMessage httpResponse = await this.HttpClient.SendAsync(new HttpRequestMessage()
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"{urlDomain}{urlPath}"),
                    Headers =
                    {
                        { "Authorization", "Bearer " + this.GetAccessToken().Token }
                    }
                });

                string responseContent = await httpResponse.Content.ReadAsStringAsync();

                // PARSE THE REQUEST

                if (httpResponse.IsSuccessStatusCode)
                {
                    return new SalesforceAPILoadResponse(
                        successResponse: JsonConvert.DeserializeObject<SalesforceAPILoadResponseSuccess>(responseContent)
                    );
                }
                else
                {
                    return new SalesforceAPILoadResponse(
                        errorResponse: JsonConvert.DeserializeObject<SalesforceAPIQueryResponseError>(responseContent)
                    );
                }

            }
            else
            {

                return new SalesforceAPILoadResponse(
                    errorMessage: accessToken.Message
                );
            }

        }

    }


}