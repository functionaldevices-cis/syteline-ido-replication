using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{

    public class SalesforceAPIUpsertResults
    {

        public List<SalesforceAPIUpsertResponse> responses
        {
            get; set;
        } = new List<SalesforceAPIUpsertResponse>();

        public SalesforceAPIUpsertResults()
        {

        }

        public void AddResponse(SalesforceAPIUpsertResponse response)
        {
            this.responses.Add(response);
        }
    }

}
