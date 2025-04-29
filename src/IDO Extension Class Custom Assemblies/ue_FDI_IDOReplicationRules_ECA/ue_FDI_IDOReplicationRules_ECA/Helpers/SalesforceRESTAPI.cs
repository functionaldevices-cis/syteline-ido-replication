using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using ue_FDI_IDOReplicationRules_ECA.Models;
using ue_FDI_IDOReplicationRules_ECA.Models.SalesforceAPI;


namespace ue_FDI_IDOReplicationRules_ECA.Helpers
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

        public SalesforceCredential CurrentSalesforceCredential
        {
            get; set;
        }

        private SalesforceAPIAccessTokenDetails AccessTokenDetails
        {
            get; set;
        } = new SalesforceAPIAccessTokenDetails();

        public SalesforceRestAPI(SalesforceCredential credential)
        {

            this.CurrentSalesforceCredential = credential;

            // INIT SETTINGS

            this.StreamingRecordCap = 20000;

            // INIT HTTP CLIENT

            this.HttpClient = new HttpClient();
            this.GetAccessToken();

        }

        public SalesforceAPIAccessTokenDetails GetAccessToken(SalesforceCredential credential = null)
        {

            if (credential == null)
            {
                credential = this.CurrentSalesforceCredential;
            }

            if (credential != null)
            {

                // CHECK TO SEE IF WE NEED A NEW TOKEN

                if ((this.AccessTokenDetails.Token == "") || ((this.AccessTokenDetails.Expiration != null && this.AccessTokenDetails.Expiration >= DateTime.Now.AddMinutes(10))))
                {

                    // TRY TO GET THE TOKEN

                    try
                    {
                        // LOAD THE REQUEST

                        HttpResponseMessage httpResponse = this.HttpClient.PostAsync(
                            requestUri: new Uri(credential.TokenRequestEndpointUrl),
                            content: new FormUrlEncodedContent(new Dictionary<string, string>()
                            {
                            { "grant_type", "password" },
                            { "client_id", credential.ClientId },
                            { "client_secret", credential.ClientSecret },
                            { "username", credential.Username },
                            { "password", credential.Password + credential.SecurityToken }
                            })
                        ).Result;

                        Dictionary<string, object> parsedResponseContent = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(httpResponse.Content.ReadAsStringAsync().Result) ?? throw new Exception("Unable to parse response.");


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

        public void UpsertRecords(string objectName, List<Dictionary<string, object>> records, Action<SalesforceAPIQueryResult> onStartCallback = null, Action<SalesforceAPIQueryResult> onProgressCallback = null, Action<SalesforceAPIQueryResult> onCompleteCallback = null)
        {

            // RUN START CALLBACK

            onStartCallback?.Invoke(new SalesforceAPIQueryResult(
                CountTotal: records.Count,
                CountCompleted: 0
            ));

            // SET UP REUSABLE DATA

            Dictionary<string, object> attributesField = new Dictionary<string, object>() { { "attributes", new Dictionary<string, string>() { { "type", objectName } } } };

            // LOAD THE REQUEST

            for (int i = 0; i < records.Count; i += 200)
            {

                // UPSERT RECORD BATCH

                UpsertRecordBatch(
                    objectName: objectName,
                    recordsPackage: new SalesforceAPIUpsertPackage(records.Skip(i).Take(200).Select(record => attributesField.Concat(record).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value)).ToList())
                );

                // RUN PROGRESS CALLBACK

                onProgressCallback?.Invoke(new SalesforceAPIQueryResult(
                    CountTotal: records.Count,
                    CountCompleted: i + 200
                ));

            }

            // RUN COMPLETE CALLBACK

            onCompleteCallback?.Invoke(new SalesforceAPIQueryResult(
                CountTotal: records.Count,
                CountCompleted: records.Count
            ));

        }

        public void UpsertRecordBatch(string objectName, SalesforceAPIUpsertPackage recordsPackage)
        {

            SalesforceAPIAccessTokenDetails accessToken = this.GetAccessToken();
            if (accessToken.Valid)
            {

                string urlDomain = accessToken.InstanceURL;
                string urlPath = $"/services/data/v{this.ApiVersion:0.0}/composite/sobjects/{objectName}/Syteline_Invoice_Number__c";

                // SEND THE REQUEST

                HttpRequestMessage request = new HttpRequestMessage()
                {
                    Method = HttpMethod.Patch,
                    RequestUri = new Uri($"{urlDomain}{urlPath}"),
                    Headers =
                    {
                        { "Authorization", "Bearer " + accessToken.Token }
                    },
                    Content = new StringContent(
                        content: JsonSerializer.Serialize<SalesforceAPIUpsertPackage>(recordsPackage)
                    )
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage httpResponse = this.HttpClient.SendAsync(
                    request
                ).Result;

                string responseText = httpResponse.Content.ReadAsStringAsync().Result;

            }

        }

        public SalesforceAPILoadResultsResponse LoadRecords(string objectName, List<string> fieldNames, string filter = null, List<SalesforceAPILoadRequestOrderByField> orderBy = null, int? recordCap = null, Action<SalesforceAPIQueryResult> onStartCallback = null, Action<SalesforceAPIQueryResult> onProgressCallback = null, Action<SalesforceAPIQueryResult> onCompleteCallback = null, bool reportCompletionPercentage = false)
        {

            // INIT VARS
            SalesforceAPILoadResultsResponse results = new SalesforceAPILoadResultsResponse();
            SalesforceAPILoadResultsResponse response;

            int requestCap = recordCap != null ? Math.Min((int)recordCap, this.StreamingRecordCap) : this.StreamingRecordCap;
            bool haveToPaginate = recordCap == null || recordCap >= this.StreamingRecordCap;
            bool totalCapNotMet = true;
            bool moreRowsExist = true;
            List<Dictionary<string, object>> data = new List<Dictionary<string, object>>();

            // IF WE ARE LOADING VIA SOAP, WE NEED TO ENSURE THAT THE ROWPOINTER IS PART OF THE REQUEST, BECAUSE
            // THAT IS WHAT WE USE FOR PAGINATION, SINCE SOAP DOESN'T HAVE PAGINATION OUT OF BOX

            onStartCallback?.Invoke(new SalesforceAPIQueryResult(
                CountCompleted: 0
            ));

            do
            {

                // SEND REQUEST AND RETRIEVE RECORDS

                response = this.LoadRecordBatch(
                    objectName: objectName,
                    fieldNames: fieldNames,
                    filter: filter,
                    orderBy: orderBy,
                    recordCap: recordCap
                );

                results.success = response.success;
                results.errorCode = response.errorCode;
                results.message = response.message;
                results.totalSize = response.totalSize;
                results.done = response.done;
                results.nextRecordsUrl = response.nextRecordsUrl;
                results.records.AddRange(response.records);


                // SEND REQUEST AND RETRIEVE RECORDS

                onProgressCallback?.Invoke(new SalesforceAPIQueryResult(
                    CountTotal: results.totalSize,
                    CountCompleted: results.records.Count
                ));

                if (recordCap != null)
                {
                    if (recordCap <= results.records.Count)
                    {
                        totalCapNotMet = false;
                    }
                }

                moreRowsExist = !results.done;

            } while (moreRowsExist && totalCapNotMet);

            onCompleteCallback?.Invoke(new SalesforceAPIQueryResult(
                CountTotal: results.totalSize,
                CountCompleted: results.records.Count
            ));

            return results;

        }

        public SalesforceAPILoadResultsResponse LoadRecordBatch(string objectName, List<string> fieldNames, string filter = null, List<SalesforceAPILoadRequestOrderByField> orderBy = null, int? recordCap = null, string urlPathOverride = null)
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

                HttpResponseMessage httpResponse = this.HttpClient.SendAsync(new HttpRequestMessage()
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"{urlDomain}{urlPath}"),
                    Headers =
                    {
                        { "Authorization", "Bearer " + this.GetAccessToken().Token }
                    }
                }).Result;

                // PARSE THE REQUEST

                if (httpResponse.IsSuccessStatusCode)
                {
                    return new SalesforceAPILoadResultsResponse(
                        successResponse: JsonSerializer.Deserialize<SalesforceAPILoadResponseSuccess>(httpResponse.Content.ReadAsStringAsync().Result) ?? throw new Exception("Unable to parse response.")
                    );
                }
                else
                {
                    return new SalesforceAPILoadResultsResponse(
                        errorResponse: JsonSerializer.Deserialize<SalesforceAPILoadResponseError>(httpResponse.Content.ReadAsStringAsync().Result) ?? throw new Exception("Unable to parse response.")
                    );
                }

            }
            else
            {

                return new SalesforceAPILoadResultsResponse(
                    errorMessage: accessToken.Message
                );
            }

        }

    }


}