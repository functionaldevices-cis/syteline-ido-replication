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

        public Dictionary<string, object> LookupTable { get; set; }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/


        public ReplicationFieldTransform(string Type = "", Dictionary<string, object> LookupTable = null)
        {

            this.Type = ReplicationFieldTransformTypes.Values.Contains(Type) || Type.StartsWith("Custom") ? Type : "";
            this.LookupTable = LookupTable;

        }

        public object Convert(string input)
        {
            if (this.Type.StartsWith("Custom"))
            {
                if (this.LookupTable == null || !this.LookupTable.ContainsKey(input))
                {
                    return input;
                }
                else
                {
                    return this.LookupTable[input];
                }
            }
            else
            {
                return ReplicationFieldTransformTypes.GetLogic(this.Type)(input);
            }
        }

    }

}