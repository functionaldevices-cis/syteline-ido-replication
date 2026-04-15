using System;
using System.Collections.Generic;
using System.Linq;

namespace ue_AIR_IDOReplicationRules_ECA.Models.SytelineAPI
{

    public class ReplicationFieldMap
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string OutputFieldName { get; set; }

        public List<ReplicationFieldMapSource> ParsedSources { get; set; } = new List<ReplicationFieldMapSource>();



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

        public ReplicationFieldMap(string OutputFieldName)
        {
            this.OutputFieldName = OutputFieldName;
        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        public void AddSource(string encodedValue = null, ReplicationFieldMapSource source = null)
        {
            if (encodedValue != null)
            {
                this.ParsedSources.Add(new ReplicationFieldMapSource(encodedValue: encodedValue));
            } else if (source != null)
            {
                this.ParsedSources.Add(source);
            }

        }

    }

}