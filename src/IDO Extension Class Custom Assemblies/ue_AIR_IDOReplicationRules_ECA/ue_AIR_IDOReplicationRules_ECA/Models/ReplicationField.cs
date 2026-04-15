using System;
using System.Collections.Generic;
using System.Linq;

namespace ue_AIR_IDOReplicationRules_ECA.Models
{

    public class ReplicationField
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string OutputFieldName { get; set; }

        public List<ReplicationFieldSource> ParsedSources { get; set; } = new List<ReplicationFieldSource>();



        /***********************************************************************************************************/
        /**************************************** CALCULATED PROPERTIES ********************************************/
        /***********************************************************************************************************/

        public List<string> IDOProperties => this.ParsedSources.Where(
            source => source.Type == "IDOProperty"
        ).Select(
            source => source.Value
        ).ToList();



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public ReplicationField(string OutputFieldName)
        {
            this.OutputFieldName = OutputFieldName;
        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        public void AddSource(string encodedValue = null, ReplicationFieldSource source = null)
        {
            if (encodedValue != null)
            {
                this.ParsedSources.Add(new ReplicationFieldSource(encodedValue: encodedValue));
            } else if (source != null)
            {
                this.ParsedSources.Add(source);
            }

        }

    }

}