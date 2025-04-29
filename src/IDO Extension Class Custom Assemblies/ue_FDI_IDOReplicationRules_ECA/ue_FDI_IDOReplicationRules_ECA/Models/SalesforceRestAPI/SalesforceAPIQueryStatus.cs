using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPIQueryStatus
    {
        public int CountTotal
        {
            get; set;
        } = 0;

        public int CountCompleted
        {
            get; set;
        } = 0;

        public SalesforceAPIQueryStatus(int CountTotal = 0, int CountCompleted = 0)
        {
            this.CountTotal = CountTotal;
            this.CountCompleted = CountCompleted;
        }

    }

}