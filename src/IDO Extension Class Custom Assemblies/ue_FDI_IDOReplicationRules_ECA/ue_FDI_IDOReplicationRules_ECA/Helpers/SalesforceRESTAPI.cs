using System.Threading.Tasks;
using System.Collections.Generic;
using ue_FDI_IDOReplicationRules_ECA.Models;


namespace ue_FDI_IDOReplicationRules_ECA.Helpers
{

    public class SalesforceRESTAPI
    {

        public SalesforceRESTAPI()
        {

        }

        public static async Task<bool> Upsert(SalesforceCredential salesforceCredential, List<Dictionary<string, object>> records)
        {

            if (records.Count > 0)
            {

                await Task.Run(() => { });

            }

            return true;

        }
    }

}