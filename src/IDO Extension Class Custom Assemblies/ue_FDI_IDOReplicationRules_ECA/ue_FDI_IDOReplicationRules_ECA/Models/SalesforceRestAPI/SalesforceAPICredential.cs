using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPICredential
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string AuthFlow { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string SecurityToken { get; set; }

        public string Domain { get; set; }
        private string DefaultTokenRequestEndpointUrl => this.AuthFlow == "UsernamePassword" ? "https://login.salesforce.com/services/oauth2/token" : null;
        public string TokenRequestEndpoint => this.Domain != null ? "https://" + this.Domain + ".my.salesforce.com/services/oauth2/token" : this.DefaultTokenRequestEndpointUrl;




        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public SalesforceAPICredential(string ClientId, string ClientSecret, string AuthFlow = "UsernamePassword", string Username = null, string Password = null, string SecurityToken = null, string Domain = null)
        {

            this.ClientId = ClientId;
            this.ClientSecret = ClientSecret;
            this.AuthFlow = AuthFlow;
            this.Username = Username;
            this.Password = Password;
            this.SecurityToken = SecurityToken;
            this.Domain = Domain;

            switch (this.AuthFlow)
            {

                case "UsernamePassword":

                    if (this.Username == null)
                    {
                        throw new Exception("Error: The salesforce authentication flow is set to UsernamePassword, but the credentials are missing a username.");
                    }
                    if (this.Password == null)
                    {
                        throw new Exception("Error: The salesforce authentication flow is set to UsernamePassword, but the credentials are missing a password.");
                    }
                    if (this.SecurityToken == null)
                    {
                        throw new Exception("Error: The salesforce authentication flow is set to UsernamePassword, but the credentials are missing a security token.");
                    }
                    break;

                case "ClientCredentials":

                    if (this.Domain == null)
                    {
                        throw new Exception("Error: The salesforce authentication flow is set to ClientCredentials, but the credentials are missing a domain.");
                    }
                    break;

                default:

                    throw new Exception("Error: The AuthFlow config must be set to either UsernamePassword or ClientCredentials.");


            }
        }

    }

}