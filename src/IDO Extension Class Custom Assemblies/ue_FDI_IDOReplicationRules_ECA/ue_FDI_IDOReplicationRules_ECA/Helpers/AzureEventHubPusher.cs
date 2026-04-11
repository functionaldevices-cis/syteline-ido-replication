using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ue_AIR_IDOReplicationRules_ECA.Models.AzureEventHubAPI;

namespace ue_AIR_IDOReplicationRules_ECA.Helpers
{

    public class AzureEventHubPusher
    {

        public AzureEventHubPusher()
        {

        }

        public static async Task<bool> ExportToAzureEventHub(AzureEventHubSASCredential eventHubCredential, List<Dictionary<string, object>> records)
        {

            if (records.Count > 0)
            {

                HttpClient httpClient = new HttpClient();

                for (int i = 0; i < records.Count; i += 500)
                {

                    List<string> recordBatch = records.Skip(i).Take(500).Select(record => JsonConvert.SerializeObject(record)).ToList();
                    Task<bool> task = SendBatch(httpClient, eventHubCredential.MessagesUri, eventHubCredential.Token, recordBatch);
                    task.Wait();

                }

            }

            return true;

        }

        private static async Task<bool> SendBatch(HttpClient httpClient, string postUri, string sasToken, List<string> rows)
        {

            //string payload = "[{\"Body\": \"{\\\"TermsCode\\\": \\\"T01\\\", \\\"Description\\\": \\\"Prepaid Test\\\"}\"},  {\"Body\": \"{\\\"TermsCode\\\": \\\"T02\\\", \\\"Description\\\": \\\"NET 30 Test\\\"}\"}]";
            string payload = string.Join(
                ",",
                JsonConvert.SerializeObject(
                    rows.Select(row => new Dictionary<string, string>() { { "Body", row } })
                )
            );

            HttpRequestMessage request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(postUri),
                Headers =
                {
                    { "Authorization", sasToken }
                },
                Content = new StringContent(payload, Encoding.UTF8)
            };
            request.Content.Headers.Remove("Content-Type");
            request.Content.Headers.Add("Content-Type", "application/vnd.microsoft.servicebus.json");

            HttpResponseMessage httpResponse = await httpClient.SendAsync(request);

            return httpResponse.IsSuccessStatusCode;

        }

    }

}