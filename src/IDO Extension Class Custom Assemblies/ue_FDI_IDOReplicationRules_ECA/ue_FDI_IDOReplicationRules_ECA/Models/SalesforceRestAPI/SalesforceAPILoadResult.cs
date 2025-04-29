using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPILoadResults
    {
        private List<SalesforceAPILoadResponse> responses
        {
            get; set;
        } = new List<SalesforceAPILoadResponse>();

        public int totalSize
        {
            get; set;
        }

        public bool done
        {
            get; set;
        }

        public string nextRecordsUrl
        {
            get; set;
        }

        public int recordCount
        {
            get; set;
        }

        public List<SalesforceAPILoadResponseRecord> records => this.responses.SelectMany(response => response.records).ToList();

        public SalesforceAPILoadResults()
        {

        }

        public void AddResponse(SalesforceAPILoadResponse response)
        {

            this.totalSize = response.totalSize;
            this.done = response.done;
            this.nextRecordsUrl = response.nextRecordsUrl;
            this.recordCount += response.records.Count;
            this.responses.Add(response);

        }

    }

}