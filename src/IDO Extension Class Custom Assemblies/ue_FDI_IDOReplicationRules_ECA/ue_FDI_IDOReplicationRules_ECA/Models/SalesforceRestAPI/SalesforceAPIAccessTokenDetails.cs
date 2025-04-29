using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceAPI
{

    public class SalesforceAPIAccessTokenDetails
    {

        public string Token
        {
            get; set;
        }
        public string InstanceURL
        {
            get; set;
        }

        public DateTime? Expiration
        {
            get; set;
        }

        public bool Valid
        {
            get; set;
        }

        public string Message
        {
            get; set;
        }

        public SalesforceAPIAccessTokenDetails(string token = "", string instanceURL = "", DateTime? expiration = null, bool valid = false, string message = "")
        {
            this.Token = token;
            this.InstanceURL = instanceURL;
            this.Expiration = expiration;
            this.Valid = valid;
            this.Message = message;
        }

    }

}