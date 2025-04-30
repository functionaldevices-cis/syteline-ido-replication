using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ue_FDI_IDOReplicationRules_ECA.Models.AzureEventHubAPI;


namespace ue_FDI_IDOReplicationRules_ECA.Helpers
{

    public class AzureEventHubPusher
    {

        public AzureEventHubPusher()
        {

        }

        public static async Task<bool> ExportToAzureEventHub(AzureEventHubCredential eventHubCredential, List<Dictionary<string, object>> records)
        {

            if (records.Count > 0)
            {

                EventHubProducerClient producerClient = new EventHubProducerClient(
                    connectionString: eventHubCredential.ConnectionString,
                    eventHubName: eventHubCredential.EventHubName
                );

                for (int i = 0; i < records.Count; i += 500)
                {
                    List<string> batch = records.Skip(i).Take(500).Select(record => JsonConvert.SerializeObject(record)).ToList();
                    Task<bool> task = AzureEventHubPusher.SendBatch(producerClient, batch);
                    task.Wait();

                }

                await producerClient.DisposeAsync();

            }

            return true;

        }

        private static async Task<bool> SendBatch(EventHubProducerClient producerClient, List<string> rows)
        {

            EventDataBatch eventBatch = await producerClient.CreateBatchAsync();

            rows.ForEach(row =>
            {
                Console.WriteLine(row);
                eventBatch.TryAdd(new EventData(Encoding.UTF8.GetBytes(row)));

            });

            if (eventBatch.Count > 0)
            {

                await producerClient.SendAsync(eventBatch);
                Console.WriteLine($"Sent {eventBatch.Count}");

            }

            return true;


        }

    }

}