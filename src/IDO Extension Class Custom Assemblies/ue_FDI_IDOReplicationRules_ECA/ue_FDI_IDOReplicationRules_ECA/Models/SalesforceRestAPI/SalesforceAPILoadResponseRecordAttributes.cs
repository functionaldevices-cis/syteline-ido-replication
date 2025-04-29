using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceAPI
{

    public class SalesforceAPILoadResponseRecordAttributes
    {

        public string type
        {
            get; set;
        }

        public string url
        {
            get; set;
        }

        public SalesforceAPILoadResponseRecordAttributes(string type = "", string url = "")
        {
            this.type = type;
            this.url = url;
        }

    }

}