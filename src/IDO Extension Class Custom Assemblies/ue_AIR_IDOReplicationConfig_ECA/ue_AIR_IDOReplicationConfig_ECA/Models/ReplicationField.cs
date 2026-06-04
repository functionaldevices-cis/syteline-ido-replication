using Mongoose.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ue_AIR_IDOReplicationConfig_ECA.Models
{

    public class ReplicationField
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string OutputFieldName { get; set; }

        public string SourceType { get; set; }

        public string SourceValue { get; set; }

        public ReplicationFieldTransform Transformaton { get; set; } = new ReplicationFieldTransform();



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public ReplicationField(string outputFieldName, string encodedValue, string transformationName = null)
        {
            this.OutputFieldName = outputFieldName;

            if (encodedValue.StartsWith("P(") && encodedValue.EndsWith(")"))
            {
                this.SourceType = "IDOProperty";
                this.SourceValue = encodedValue.Substring(1).Trim('(', ')');
            }
            else
            {
                this.SourceType = "Literal";
                this.SourceValue = encodedValue;
            }

            this.Transformaton = new ReplicationFieldTransform(transformationName ?? "");

        }

        public ReplicationField(string outputFieldName, string sourceType, string sourceValue, string transformationName = null)
        {
            this.OutputFieldName = outputFieldName;

            this.SourceType = sourceType;
            this.SourceValue = sourceValue;

            this.Transformaton = new ReplicationFieldTransform(transformationName ?? "");

        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

    }

}