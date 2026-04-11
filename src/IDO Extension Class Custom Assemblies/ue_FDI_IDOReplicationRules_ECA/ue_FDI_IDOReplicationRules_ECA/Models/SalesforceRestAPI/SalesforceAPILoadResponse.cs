using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPILoadResponse
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

        public int totalSize
        {
            get; set;
        }

        public bool done
        {
            get; set;
        }

        public string nextRecordsUrl
        {
            get; set;
        }

        public List<SalesforceAPILoadResponseRecord> records
        {
            get; set;
        }

        public SalesforceAPILoadResponse(SalesforceAPILoadResponseSuccess successResponse = null, SalesforceAPIQueryResponseError errorResponse = null, string errorMessage = null)
        {

            this.success = successResponse != null ? true : false;
            this.errorCode = errorResponse?.errorCode ?? (errorMessage ?? "");
            this.message = errorResponse?.message ?? "";
            this.totalSize = successResponse?.totalSize ?? 0;
            this.done = successResponse?.done ?? true;
            this.nextRecordsUrl = successResponse?.nextRecordsUrl;
            this.records = successResponse != null ? successResponse.records.Select(recordRaw =>
            {

                Dictionary<string, string> attributes = new Dictionary<string, string>();

                if (recordRaw.ContainsKey("attributes"))
                {
                    JToken attributesNestedObject = (JToken)recordRaw["attributes"];
                    attributes = new Dictionary<string, string>() {
                        { "type", attributesNestedObject.SelectToken("type").ToString() ?? "" },
                        { "url", attributesNestedObject.SelectToken("url").ToString() ?? "" }
                    };
                    recordRaw.Remove("attributes");
                }

                return new SalesforceAPILoadResponseRecord(attributes, recordRaw);

            }).ToList() : new List<SalesforceAPILoadResponseRecord>();

        }

    }

}