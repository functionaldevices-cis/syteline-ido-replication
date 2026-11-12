using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models
{

    public class ReplicationRuleResult
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string RuleNum { get; set; }

        public string TargetType { get; set; }

        public int RecordCount { get; set; }

        public bool Success { get; set; }

        public string ErrorMessage { get; set; }



        /***********************************************************************************************************/
        /*********************************************** COue_AIR_IDOReplicationRules_ECATRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public ReplicationRuleResult(string ruleNum, string targetType, int recordCount, bool success, string errorMessage = "")
        {
            this.RuleNum = ruleNum;
            this.TargetType = targetType;
            this.RecordCount = recordCount;
            this.Success = success;
            this.ErrorMessage = errorMessage ?? "";
        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        public override string ToString()
        {
            return "Rule '" + this.RuleNum + "' (" + this.TargetType + "): " + (this.Success ? "succeeded" : "FAILED") + " for " + this.RecordCount + " record(s)." + (this.ErrorMessage != "" ? " " + this.ErrorMessage : "");
        }

    }

}