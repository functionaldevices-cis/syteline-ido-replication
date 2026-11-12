using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationConfig_ECA.Models.AzureEventHubAPI
{

    /***********************************************************************************************************/
    /*
    /* Parses a Microsoft Fabric event schema URL, such as
    /* https://<host>.messagingcatalog.azure.net/schemagroups/<schema set id>/schemas/<schema name>/versions/<version>
    /*
    /* The schema name becomes the CloudEvents "type" and the full URL becomes "dataschema". Fabric Event Schema
    /* Eventstreams use those to match each event to its schema.
    /*
    /***********************************************************************************************************/

    public class FabricEventSchemaUri
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string Url { get; set; }

        public string SchemaSetID { get; set; }

        public string SchemaName { get; set; }

        public string Version { get; set; }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public FabricEventSchemaUri(string url)
        {

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("The Event Schema URI is empty.");
            }

            this.Url = url.Trim();

            // USE ONLY THE PATH SO QUERY STRINGS AND FRAGMENTS CAN'T CONFUSE THE SPLIT

            Uri parsedUri;
            string path = Uri.TryCreate(this.Url, UriKind.Absolute, out parsedUri) ? parsedUri.AbsolutePath : this.Url;
            string[] segments = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            this.SchemaSetID = this.ValueAfter(segments, "schemagroups") ?? throw new FormatException("No '/schemagroups/<id>' segment found in the Event Schema URI: " + this.Url);
            this.SchemaName = this.ValueAfter(segments, "schemas") ?? throw new FormatException("No '/schemas/<name>' segment found in the Event Schema URI: " + this.Url);
            this.Version = this.ValueAfter(segments, "versions") ?? throw new FormatException("No '/versions/<version>' segment found in the Event Schema URI: " + this.Url);

        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

        private string ValueAfter(string[] segments, string keyword)
        {

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1];
                }
            }

            return null;

        }

    }

}
