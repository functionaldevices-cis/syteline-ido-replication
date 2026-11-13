using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationConfig_ECA.Models
{

    /***********************************************************************************************************/
    /*
    /* What happened to one record for one rule: the record's ID-field value, the remapped record as JSON
    /* (what was sent, so a retry can resend it without reloading the record from SyteLine), and the status.
    /*
    /***********************************************************************************************************/

    public class ReplicationRecordOutcome
    {

        public const string StatusSucceeded = "Succeeded";

        public const string StatusFailed = "Failed";

        public const string StatusSkipped = "Skipped";

        public string RecordID { get; set; }

        public string Payload { get; set; }

        public string Status { get; set; }

        public string ErrorMessage { get; set; }

        public ReplicationRecordOutcome(string recordID, string payload, string status, string errorMessage = "")
        {
            this.RecordID = recordID ?? "";
            this.Payload = payload;
            this.Status = status;
            this.ErrorMessage = errorMessage ?? "";
        }

    }

}