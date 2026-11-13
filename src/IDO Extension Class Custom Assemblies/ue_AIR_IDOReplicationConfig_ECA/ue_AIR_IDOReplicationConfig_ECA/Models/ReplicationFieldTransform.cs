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

namespace ue_AIR_IDOReplicationConfig_ECA.Models
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
        // VALUE DOESN'T FAIL EVERY RECORD IN THE RULE.

        public object Convert(object input)
        {

            if (input == null || this.IsNone)
            {
                return input;
            }

            try
            {
                return ReplicationFieldTransformTypes.GetLogic(this.Type)(input);
            }
            catch (Exception)
            {
                return input;
            }

        }

    }

}