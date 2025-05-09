using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertResponse : SalesforceAPIQueryResponse
    {

        public List<Dictionary<string, string>> recordResults { get; set; }

        public List<Dictionary<string, string>> recordResultsSuccesses => this.recordResults.Where(result => result["success"] == "True").ToList();

        public List<Dictionary<string, string>> recordResultsFailures => this.recordResults.Where(result => result["success"] == "False").ToList();


        public SalesforceAPIUpsertResponse(List<SalesforceAPIUpsertResponseRecordResult> successResponse = null, SalesforceAPIQueryResponseError errorResponse = null, string errorMessage = null)
        {
            this.success = successResponse != null ? true : false;
            this.errorCode = errorResponse?.errorCode ?? (errorMessage ?? "");
            this.message = errorResponse?.message ?? "";
            if (successResponse != null)
            {
                this.recordResults = successResponse.Select(recordResult => new Dictionary<string, string>() { { "id", recordResult.id }, { "success", recordResult.success.ToString() }, { "operation", recordResult.operation }, { "errorMessage", recordResult.errorMessage } }).ToList();
            }
            else
            {
                this.recordResults = new List<Dictionary<string, string>>();
            }
        }

    }

}
