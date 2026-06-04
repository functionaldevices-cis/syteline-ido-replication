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

        public object Convert(string input)
        {
            return ReplicationFieldTransformTypes.GetLogic(this.Type)(input);
        }

    }

}