using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{

    public class AzureEventHubCredential
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string ConnectionString { get; set; }

        public string EventHubName { get; set; }


        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public AzureEventHubCredential(string ConnectionString)
        {
            this.ConnectionString = ConnectionString;
            this.EventHubName = ConnectionString.Substring(ConnectionString.IndexOf("EntityPath=") + "EntityPath=".Length);
        }

    }

}