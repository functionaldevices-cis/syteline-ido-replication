using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class MapFieldSource
    {

        /***********************************************************************************************************/
        /********************************************** PROPERTIES *************************************************/
        /***********************************************************************************************************/

        public string Type { get; set; }

        public string Value { get; set; }


        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/


        public MapFieldSource(string encodedValue = null, string type = null, string value = null)
        {

            if (encodedValue != null)
            {

                if (encodedValue.StartsWith("P(") && encodedValue.EndsWith(")"))
                {
                    this.Type = "IDOProperty";
                    this.Value = encodedValue.Substring(1).Trim('(', ')');
                }
                else
                {
                    this.Type = "Literal";
                    this.Value = encodedValue;
                }

            } else if (type != null && value != null)
            {

                this.Type = type;
                this.Value = value;

            }


        }

    }

}