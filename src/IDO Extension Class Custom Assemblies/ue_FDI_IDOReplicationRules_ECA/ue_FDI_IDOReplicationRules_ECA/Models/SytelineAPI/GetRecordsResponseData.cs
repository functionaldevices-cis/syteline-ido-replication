using Mongoose.IDO.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI
{

    public class GetRecordsResponseData
    {

        public Dictionary<string, int> PropertyKeys { get; set; }

        public List<IDOItem> Items { get { return this.LoadCollectionResponseData.Items; } }

        public LoadCollectionResponseData LoadCollectionResponseData { get; set; }

        public GetRecordsResponseData(Dictionary<string, int> propertyKeys, LoadCollectionResponseData loadCollectionResponseData)
        {

            this.PropertyKeys = propertyKeys;
            this.LoadCollectionResponseData = loadCollectionResponseData;

        }

    }

}
