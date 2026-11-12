using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ue_AIR_IDOReplicationRules_ECA.Models.AzureEventHubAPI
{

    public class AzureEventHubSASCredential
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string ConnectionString { get; set; }
        
        public string EventHubName { get; set; }
        
        public string SharedAccessKeyName { get; set; }
        
        public string SharedAccessKey { get; set; }
        
        public string SharedAccessKeyUri { get; set; }
        
        public string MessagesUri { get; set; }

        private string SavedToken { get; set; }

        private DateTime SavedTokenExpiration { get; set; }

        public string Token
        {
            get
            {

                // GENERATE A NEW TOKEN IF THERE ISN'T ONE YET, OR IF THE CURRENT ONE EXPIRES WITHIN THE NEXT 5 MINUTES

                if (this.SavedToken == "" || this.SavedTokenExpiration <= DateTime.UtcNow.AddMinutes(5))
                {

                    DateTime expiration = DateTime.UtcNow.AddDays(7);
                    string expiry = Convert.ToString((long)(expiration - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.InvariantCulture);
                    string stringToSign = HttpUtility.UrlEncode(this.SharedAccessKeyUri) + "\n" + expiry;
                    string signature;

                    using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(this.SharedAccessKey)))
                    {
                        signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    }

                    this.SavedToken = string.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}", HttpUtility.UrlEncode(this.SharedAccessKeyUri), HttpUtility.UrlEncode(signature), expiry, this.SharedAccessKeyName);
                    this.SavedTokenExpiration = expiration;

                }

                return this.SavedToken;

            }
        }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public AzureEventHubSASCredential(string ConnectionString)
        {

            // PARSE THE CONNECTION STRING BY KEY NAME, SO THE ORDER OF THE PARTS DOESN'T MATTER. EACH PART IS SPLIT ON
            // ITS FIRST '=' ONLY, BECAUSE THE BASE64 SharedAccessKey CAN END WITH '='.

            Dictionary<string, string> connectionStringParts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in (ConnectionString ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {

                int separatorIndex = part.IndexOf('=');

                if (separatorIndex > 0)
                {
                    connectionStringParts[part.Substring(0, separatorIndex).Trim()] = part.Substring(separatorIndex + 1).Trim();
                }

            }

            List<string> missingKeys = new List<string>() { "Endpoint", "SharedAccessKeyName", "SharedAccessKey", "EntityPath" }.Where(key => !connectionStringParts.ContainsKey(key) || connectionStringParts[key] == "").ToList();

            if (missingKeys.Count > 0)
            {
                throw new ArgumentException("The Azure Event Hub connection string is missing " + string.Join(", ", missingKeys) + ". Use the connection string from a shared access policy on the event hub itself (it includes EntityPath), not the namespace.");
            }

            string endpoint = connectionStringParts["Endpoint"].Replace("sb://", "https://");
            string entityPath = connectionStringParts["EntityPath"];

            if (!endpoint.EndsWith("/"))
            {
                endpoint += "/";
            }

            this.ConnectionString = ConnectionString;
            this.EventHubName = entityPath;
            this.SharedAccessKeyName = connectionStringParts["SharedAccessKeyName"];
            this.SharedAccessKey = connectionStringParts["SharedAccessKey"];
            this.SharedAccessKeyUri = endpoint + entityPath;
            this.MessagesUri = endpoint + entityPath + "/messages";
            this.SavedToken = "";
            this.SavedTokenExpiration = DateTime.MinValue;

        }

    }

}