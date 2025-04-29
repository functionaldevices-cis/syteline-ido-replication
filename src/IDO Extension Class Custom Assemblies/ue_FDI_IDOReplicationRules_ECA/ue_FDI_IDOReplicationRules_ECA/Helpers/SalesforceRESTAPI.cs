using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using ue_FDI_IDOReplicationRules_ECA.Models;
using ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI;
using static Mongoose.Core.Common.QuickKeywordParser;


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
                    CountCompleted: i + 200
                ));

                onProgressCallback?.Invoke(new SalesforceAPIQueryStatus(
                    CountTotal: records.Count,
                    CountCompleted: i + recordBatch.Count()
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
                        content: JsonSerializer.Serialize<SalesforceAPIUpsertPackage>(recordsPackage)
                    )
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage httpResponse = await this.HttpClient.SendAsync(
                    request
                );

                string responseContent = await httpResponse.Content.ReadAsStringAsync();

                return new SalesforceAPIUpsertResponse(
                    success: httpResponse.IsSuccessStatusCode,
                    message: responseContent
                );

            }
            else
            {

                return new SalesforceAPIUpsertResponse(
                    success: false,
                    message: accessToken.Message
                );

            }

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
                        successResponse: JsonSerializer.Deserialize<SalesforceAPILoadResponseSuccess>(responseContent) ?? throw new Exception("Unable to parse response.")
                    );
                }
                else
                {
                    return new SalesforceAPILoadResponse(
                        errorResponse: JsonSerializer.Deserialize<SalesforceAPILoadResponseError>(responseContent) ?? throw new Exception("Unable to parse response.")
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