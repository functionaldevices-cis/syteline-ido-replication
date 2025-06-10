using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertResponseRecordResult
    {
        public string id
        {
            get; set;
        }

        public bool success
        {
            get; set;
        }

        public List<SalesforceAPIUpsertResponseRecordResultError> errors
        {
            get; set;
        }

        public bool created
        {
            get; set;
        }
        public string operation => this.created ? "Insert" : "Update";

        public string errorMessage => this.errors.Count > 0 ? ((this.errors[0].fields.Count > 0 ? this.errors[0].fields[0] + ": " : "") + this.errors[0].message) : "";

    }

}
