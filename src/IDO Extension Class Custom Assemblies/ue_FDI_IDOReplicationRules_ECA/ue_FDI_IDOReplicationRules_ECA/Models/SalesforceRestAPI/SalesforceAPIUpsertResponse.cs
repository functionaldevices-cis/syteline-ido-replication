using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertResponse
    {

        public bool success
        {
            get; set;
        }

        public string message
        {
            get; set;
        }

        public SalesforceAPIUpsertResponse(bool success, string message)
        {
            this.success = success;
            this.message = message;
        }
    }

}
