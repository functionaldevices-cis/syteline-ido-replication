using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPILoadResponseSuccess
    {

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

        public List<Dictionary<string, object>> records
        {
            get; set;
        }

        public SalesforceAPILoadResponseSuccess(int totalSize, bool done, string nextRecordsUrl, List<Dictionary<string, object>> records)
        {
            this.totalSize = totalSize;
            this.done = done;
            this.nextRecordsUrl = nextRecordsUrl;
            this.records = records;

        }

    }

}