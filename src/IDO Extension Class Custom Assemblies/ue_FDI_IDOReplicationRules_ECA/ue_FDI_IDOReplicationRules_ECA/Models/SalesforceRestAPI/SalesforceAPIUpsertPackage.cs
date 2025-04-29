using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertPackage
    {

        [JsonPropertyName("allOrNone")]
        public bool AllOrNone
        {
            get; set;
        }

        [JsonPropertyName("records")]
        public List<Dictionary<string, object>> Records
        {
            get; set;
        }


        public SalesforceAPIUpsertPackage(List<Dictionary<string, object>> Records, bool AllOrNone = false)
        {
            this.AllOrNone = AllOrNone;
            this.Records = Records;
        }

    }

}