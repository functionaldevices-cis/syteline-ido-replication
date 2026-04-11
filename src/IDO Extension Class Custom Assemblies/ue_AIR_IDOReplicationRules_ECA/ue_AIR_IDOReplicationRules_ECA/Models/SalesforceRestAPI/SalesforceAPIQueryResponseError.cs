using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPIQueryResponseError : List<Dictionary<string, string>>
    {

        public string errorCode => this[0]["errorCode"];

        public string message => this[0]["message"];

    }

}