using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ue_AIR_IDOReplicationConfig_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPICredential
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        // THE ORG'S MY DOMAIN NAME: "mycompany" FOR PRODUCTION, "mycompany--sandboxname.sandbox" FOR A SANDBOX

        public string Domain { get; set; }

        public string TokenRequestEndpoint => "https://" + this.Domain + ".my.salesforce.com/services/oauth2/token";




        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        // SIGN-IN USES THE OAUTH CLIENT CREDENTIALS FLOW ONLY. IT NEEDS THE CONSUMER KEY AND SECRET OF A SALESFORCE EXTERNAL
        // CLIENT APP (OR AN OLDER CONNECTED APP) THAT HAS THE FLOW TURNED ON AND A "RUN AS" USER, AND THE ORG'S MY DOMAIN NAME,
        // BECAUSE THIS FLOW CAN'T SIGN IN AT login.salesforce.com. THE USERNAME-PASSWORD FLOW ISN'T SUPPORTED: EXTERNAL
        // CLIENT APPS DON'T OFFER IT.

        public SalesforceAPICredential(string ClientId, string ClientSecret, string Domain)
        {

            this.ClientId = ClientId;
            this.ClientSecret = ClientSecret;
            this.Domain = Domain;

            if (string.IsNullOrWhiteSpace(this.ClientId))
            {
                throw new Exception("The Salesforce credential is missing a Client ID.");
            }

            if (string.IsNullOrWhiteSpace(this.ClientSecret))
            {
                throw new Exception("The Salesforce credential is missing a Client Secret.");
            }

            if (string.IsNullOrWhiteSpace(this.Domain))
            {
                throw new Exception("The Salesforce credential is missing a Domain. Enter the org's My Domain name, for example 'mycompany', or 'mycompany--sandboxname.sandbox' for a sandbox.");
            }

        }

    }

}