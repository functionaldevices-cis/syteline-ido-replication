using System;
using System.Collections.Generic;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertPackage
    {
        public bool allOrNone
        {
            get; set;
        }

        public List<Dictionary<string, object>> records
        {
            get; set;
        }


        public SalesforceAPIUpsertPackage(List<Dictionary<string, object>> records, bool allOrNone = false)
        {
            this.allOrNone = allOrNone;
            this.records = records;
        }

    }

}