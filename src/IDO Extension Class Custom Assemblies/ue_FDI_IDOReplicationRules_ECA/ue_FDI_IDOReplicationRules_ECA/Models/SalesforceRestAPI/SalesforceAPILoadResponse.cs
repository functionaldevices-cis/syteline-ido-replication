using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceAPI
{
    public class SalesforceAPILoadResultsResponse
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

        public SalesforceAPILoadResultsResponse(SalesforceAPILoadResponseSuccess successResponse = null, SalesforceAPILoadResponseError errorResponse = null, string errorMessage = null)
        {

            this.success = successResponse != null ? true : false;
            this.errorCode = errorResponse?.errorCode ?? (errorMessage ?? "");
            this.message = errorResponse?.message ?? "";
            this.totalSize = successResponse?.totalSize ?? 0;
            this.done = successResponse?.done ?? true;
            this.nextRecordsUrl = successResponse?.nextRecordsUrl;
            this.records = successResponse != null ? successResponse.records.Select(recordRaw =>
            {

                SalesforceAPILoadResponseRecordAttributes attributes = new SalesforceAPILoadResponseRecordAttributes();

                if (recordRaw.ContainsKey("attributes"))
                {
                    JsonElement attributesNestedObject = (JsonElement)recordRaw["attributes"];
                    string type = attributesNestedObject.GetProperty("type").GetString() ?? "";
                    string url = attributesNestedObject.GetProperty("url").GetString() ?? "";
                    attributes = new SalesforceAPILoadResponseRecordAttributes(type, url);
                    recordRaw.Remove("attributes");
                }

                return new SalesforceAPILoadResponseRecord(attributes, recordRaw);

            }).ToList() : new List<SalesforceAPILoadResponseRecord>();

        }

    }

}