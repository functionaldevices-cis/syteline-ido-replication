using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models
{
    public class ReplicationFieldTransform
    {

        /***********************************************************************************************************/
        /********************************************** PROPERTIES *************************************************/
        /***********************************************************************************************************/

        public string Type { get; set; }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/


        public ReplicationFieldTransform(string Type = "")
        {
            this.Type = ReplicationFieldTransformTypes.Values.Contains(Type) ? Type : "";
        }

        public bool IsNone => this.Type == "";

        // CONVERTS A NON-NULL VALUE. NULL STAYS NULL. A VALUE THE TRANSFORMATION CAN'T CONVERT IS RETURNED UNCHANGED, SO ONE BAD
        // VALUE DOESN'T FAIL EVERY RECORD IN THE RULE, AND warning SAYS WHAT HAPPENED SO THE CALLER CAN LOG IT ("" WHEN FINE).

        public object Convert(object input)
        {
            string warning;
            return this.Convert(input, out warning);
        }

        public object Convert(object input, out string warning)
        {

            warning = "";

            if (input == null || this.IsNone)
            {
                return input;
            }

            try
            {
                return ReplicationFieldTransformTypes.GetLogic(this.Type)(input);
            }
            catch (Exception ex)
            {

                string inputText = (System.Convert.ToString(input, System.Globalization.CultureInfo.InvariantCulture) ?? "").Trim();

                if (inputText.Length > 100)
                {
                    inputText = inputText.Substring(0, 100) + "...";
                }

                warning = "couldn't apply " + this.Type + " to '" + inputText + "' (" + ex.GetBaseException().Message + "), so it was sent unchanged";
                return input;

            }

        }

    }

}