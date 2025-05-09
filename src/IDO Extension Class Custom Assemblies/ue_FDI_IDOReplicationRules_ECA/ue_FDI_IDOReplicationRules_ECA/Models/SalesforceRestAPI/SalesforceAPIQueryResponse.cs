using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPIQueryResponse
    {

        public bool success
        {
            get; set;
        }

        public string errorCode
        {
            get; set;
        }

        public string message
        {
            get; set;
        }

        public SalesforceAPIQueryResponse(SalesforceAPIQueryResponseError? errorResponse = null, string? errorMessage = null)
        {
            this.success = errorResponse == null && errorMessage == null ? true : false;
            this.errorCode = errorResponse?.errorCode ?? (errorMessage ?? "");
            this.message = errorResponse?.message ?? "";
        }

    }

}