using System;
using System.Collections.Generic;
using System.Linq;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI
{

    public class MapField
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string OutputFieldName { get; set; }

        public List<MapFieldSource> ParsedSources { get; set; } = new List<MapFieldSource>();



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

        public MapField(string OutputFieldName)
        {
            this.OutputFieldName = OutputFieldName;
        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        public void AddSource(string encodedValue = null, MapFieldSource source = null)
        {
            if (encodedValue != null)
            {
                this.ParsedSources.Add(new MapFieldSource(encodedValue: encodedValue));
            } else if (source != null)
            {
                this.ParsedSources.Add(source);
            }

        }

    }

}