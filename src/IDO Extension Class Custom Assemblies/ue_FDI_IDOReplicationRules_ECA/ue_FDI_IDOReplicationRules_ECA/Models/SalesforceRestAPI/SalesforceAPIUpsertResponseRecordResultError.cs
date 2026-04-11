using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPIUpsertResponseRecordResultError
    {

        public string statusCode
        {
            get; set;
        }

        public string message
        {
            get; set;
        }

        public List<string> fields
        {
            get; set;
        }

    }

}