using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceAPI
{

    public class SalesforceAPILoadResponseErrorMessage
    {
        public string errorCode
        {
            get; set;
        } = "";

        public string message
        {
            get; set;
        } = "";

        public SalesforceAPILoadResponseErrorMessage(string errorCode, string message)
        {

            this.errorCode = errorCode;
            this.message = message;

        }

    }

}
