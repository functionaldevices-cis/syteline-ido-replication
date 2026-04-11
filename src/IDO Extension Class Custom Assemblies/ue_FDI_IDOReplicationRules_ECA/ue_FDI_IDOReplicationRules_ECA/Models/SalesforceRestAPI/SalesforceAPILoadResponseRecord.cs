using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPILoadResponseRecord
    {

        public Dictionary<string, string> Attributes
        {
            get; set;
        }

        public Dictionary<string, object> Fields
        {
            get; set;
        }

        public SalesforceAPILoadResponseRecord(Dictionary<string, string> Attributes, Dictionary<string, object> Fields)
        {
            this.Attributes = Attributes;
            this.Fields = Fields;
        }


    }

}
