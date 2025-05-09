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
        }

        public int CountCompleted
        {
            get; set;
        }

        public int CountBatch
        {
            get; set;
        }

        public bool Success
        {
            get; set;
        }

        public string ErrorCode
        {
            get; set;
        }

        public string ErrorMessage
        {
            get; set;
        }

        public List<Dictionary<string, string>> RecordDetails
        {
            get; set;
        }

        public SalesforceAPIQueryStatus(int CountTotal = 0, int CountCompleted = 0, int CountBatch = 0, bool Success = false, string ErrorCode = "", string ErrorMessage = "", List<Dictionary<string, string>> RecordDetails = null)
        {
            this.CountTotal = CountTotal;
            this.CountCompleted = CountCompleted;
            this.CountBatch = CountBatch;
            this.Success = Success;
            this.ErrorCode = ErrorCode;
            this.ErrorMessage = ErrorMessage;
            this.RecordDetails = RecordDetails ?? new List<Dictionary<string, string>>();
        }

    }

}