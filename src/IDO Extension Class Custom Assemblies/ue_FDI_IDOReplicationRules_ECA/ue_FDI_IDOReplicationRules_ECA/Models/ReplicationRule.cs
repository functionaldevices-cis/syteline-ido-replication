using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mongoose.IDO;
using Mongoose.IDO.Protocol;
using ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class ReplicationRule
    {

        /***********************************************************************************************************/
        /******************************************* DATA PROPERTIES ***********************************************/
        /***********************************************************************************************************/

        public string RuleNum { get; set; }

        public string TargetType { get; set; }

        public string CredentialValue01 { get; set; }

        public string CredentialValue02 { get; set; }

        public string CredentialValue03 { get; set; }

        public string CredentialValue04 { get; set; }

        public string CredentialValue05 { get; set; }

        public string CredentialValue06 { get; set; }

        public string CredentialValue07 { get; set; }

        public string CredentialValue08 { get; set; }

        public string CredentialValue09 { get; set; }

        public string CredentialValue10 { get; set; }

        public string Option01 { get; set; }

        public string Option02 { get; set; }

        public string Option03 { get; set; }

        public string Option04 { get; set; }

        public string Option05 { get; set; }

        public string Option06 { get; set; }

        public string Option07 { get; set; }

        public string Option08 { get; set; }

        public string Option09 { get; set; }

        public string Option10 { get; set; }

        public Dictionary<string, MapField> MapFields { get; set; }



        /***********************************************************************************************************/
        /**************************************** CALCULATED PROPERTIES ********************************************/
        /***********************************************************************************************************/

        public List<MapField> MapFieldsList => this.MapFields.Values.ToList();


        /***********************************************************************************************************/
        /*********************************************** CONSTRUCTOR ***********************************************/
        /***********************************************************************************************************/

        public ReplicationRule(IDOItem record, Dictionary<string, int> propertyKeys)
        {

            this.RuleNum = record.PropertyValues[propertyKeys["RuleNum"]].Value;
            this.TargetType = record.PropertyValues[propertyKeys["TargetType"]].Value;
            this.CredentialValue01 = record.PropertyValues[propertyKeys["CredentialValue01"]].Value;
            this.CredentialValue02 = record.PropertyValues[propertyKeys["CredentialValue02"]].Value;
            this.CredentialValue03 = record.PropertyValues[propertyKeys["CredentialValue03"]].Value;
            this.CredentialValue04 = record.PropertyValues[propertyKeys["CredentialValue04"]].Value;
            this.CredentialValue05 = record.PropertyValues[propertyKeys["CredentialValue05"]].Value;
            this.CredentialValue06 = record.PropertyValues[propertyKeys["CredentialValue06"]].Value;
            this.CredentialValue07 = record.PropertyValues[propertyKeys["CredentialValue07"]].Value;
            this.CredentialValue08 = record.PropertyValues[propertyKeys["CredentialValue08"]].Value;
            this.CredentialValue09 = record.PropertyValues[propertyKeys["CredentialValue09"]].Value;
            this.CredentialValue10 = record.PropertyValues[propertyKeys["CredentialValue10"]].Value;
            this.Option01 = record.PropertyValues[propertyKeys["Option01"]].Value;
            this.Option02 = record.PropertyValues[propertyKeys["Option02"]].Value;
            this.Option03 = record.PropertyValues[propertyKeys["Option03"]].Value;
            this.Option04 = record.PropertyValues[propertyKeys["Option04"]].Value;
            this.Option05 = record.PropertyValues[propertyKeys["Option05"]].Value;
            this.Option06 = record.PropertyValues[propertyKeys["Option06"]].Value;
            this.Option07 = record.PropertyValues[propertyKeys["Option07"]].Value;
            this.Option08 = record.PropertyValues[propertyKeys["Option08"]].Value;
            this.Option09 = record.PropertyValues[propertyKeys["Option09"]].Value;
            this.Option10 = record.PropertyValues[propertyKeys["Option10"]].Value;

            this.MapFields = new Dictionary<string, MapField>();

        }



        /***********************************************************************************************************/
        /************************************************* METHODS *************************************************/
        /***********************************************************************************************************/

    }
}
