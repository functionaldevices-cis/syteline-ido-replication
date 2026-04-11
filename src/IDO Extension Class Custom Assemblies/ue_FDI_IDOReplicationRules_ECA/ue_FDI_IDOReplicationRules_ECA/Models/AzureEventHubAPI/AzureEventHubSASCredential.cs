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
                if (this.SavedToken == "" || this.SavedTokenExpiration == DateTime.MinValue || this.SavedTokenExpiration >= DateTime.UtcNow)
                {

                    TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
                    var week = 60 * 60 * 24 * 7;
                    var expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + week);
                    string stringToSign = HttpUtility.UrlEncode(this.SharedAccessKeyUri) + "\n" + expiry;
                    HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(this.SharedAccessKey));
                    var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                    
                    this.SavedToken = string.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}", HttpUtility.UrlEncode(this.SharedAccessKeyUri), HttpUtility.UrlEncode(signature), expiry, this.SharedAccessKeyName);
                    this.SavedTokenExpiration = DateTime.UtcNow.AddDays(7);

                }

                return this.SavedToken;

            }
        }



        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public AzureEventHubSASCredential(string ConnectionString)
        {

            List<string> connectionStringParts = ConnectionString.Split(';').Select(part => {
                return part.Replace("Endpoint=", "").Replace("SharedAccessKeyName=", "").Replace("SharedAccessKey=", "").Replace("EntityPath=", "");
            }).ToList();

            string endpoint = connectionStringParts[0];
            string sharedAccessKeyName = connectionStringParts[1];
            string sharedAccessKey = connectionStringParts[2];
            string entityPath = connectionStringParts[3];

            this.ConnectionString = ConnectionString;
            this.EventHubName = entityPath;
            this.SharedAccessKeyName = sharedAccessKeyName;
            this.SharedAccessKey = sharedAccessKey;
            this.SharedAccessKeyUri = $"{endpoint.Replace("sb://", "https://")}{entityPath}";
            this.MessagesUri = $"{endpoint.Replace("sb://", "https://")}{entityPath}/messages";
            this.SavedToken = "";
            this.SavedTokenExpiration = DateTime.MinValue;

        }

    }

}