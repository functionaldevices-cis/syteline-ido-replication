using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{

    public class SalesforceCredential
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string SecurityToken { get; set; }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public SalesforceCredential(string ClientId, string ClientSecret, string Username, string Password, string SecurityToken)
        {
            this.ClientId = ClientId;
            this.ClientSecret = ClientSecret;
            this.Username = Username;
            this.Password = Password;
            this.SecurityToken = SecurityToken;
        }

    }

}