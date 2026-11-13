using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models.AzureEventHubAPI
{

    public class AzureEventHubPushResult
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public int CountTotal { get; set; }

        public int CountSent { get; set; }

        public int CountFailed { get; set; }

        public int CountBatches { get; set; }

        public List<string> FailedRecordIDs { get; set; }

        public List<string> ErrorMessages { get; set; }

        // THE ERROR FOR EACH FAILED RECORD, KEYED BY ITS POSITION IN THE RECORDS PASSED TO Push. BATCHES ARE SENT IN ORDER, SO
        // A FAILED BATCH COVERS THE POSITIONS FROM THE COUNT ALREADY PROCESSED ONWARD.

        public Dictionary<int, string> FailedRecordErrors { get; set; }

        public bool Success => this.CountFailed == 0;

        public string ErrorSummary
        {
            get
            {

                if (this.Success)
                {
                    return "";
                }

                // KEEP THE SUMMARY SHORT ENOUGH TO LOG. LIST AT MOST 20 FAILED RECORD IDS.

                List<string> failedIDs = this.FailedRecordIDs.Take(20).ToList();

                return this.CountFailed + " of " + this.CountTotal + " record(s) were rejected. "
                    + string.Join(" | ", this.ErrorMessages.Distinct())
                    + (failedIDs.Count > 0 ? " Failed IDs: " + string.Join(", ", failedIDs) + (this.FailedRecordIDs.Count > failedIDs.Count ? ", ..." : "") : "");

            }
        }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public AzureEventHubPushResult(int countTotal)
        {
            this.CountTotal = countTotal;
            this.CountSent = 0;
            this.CountFailed = 0;
            this.CountBatches = 0;
            this.FailedRecordIDs = new List<string>();
            this.ErrorMessages = new List<string>();
            this.FailedRecordErrors = new Dictionary<int, string>();
        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        public void AddBatch(List<string> recordIDs, string failureMessage)
        {

            this.CountBatches++;

            int firstRecordIndex = this.CountSent + this.CountFailed;

            if (string.IsNullOrEmpty(failureMessage))
            {
                this.CountSent += recordIDs.Count;
            }
            else
            {
                for (int i = 0; i < recordIDs.Count; i++)
                {
                    this.FailedRecordErrors[firstRecordIndex + i] = failureMessage;
                }

                this.CountFailed += recordIDs.Count;
                this.FailedRecordIDs.AddRange(recordIDs);
                this.ErrorMessages.Add(failureMessage);
            }

        }

    }

}
