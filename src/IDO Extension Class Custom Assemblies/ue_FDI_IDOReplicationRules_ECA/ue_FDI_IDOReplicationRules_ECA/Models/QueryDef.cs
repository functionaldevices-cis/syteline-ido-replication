using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class QueryDef
    {

        public string IDOName { get; set; }
        public List<string> SelectProperties { get; set; } = new List<string>();
        public Dictionary<string, object> UpdateProperties { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> InsertProperties { get; set; } = new Dictionary<string, object>();
        public string Filter { get; set; } = "";
        public string BaseFilter { get; set; } = "";
        public string OrderBy { get; set; } = "";
        public int RecordCap { get; set; } = 0;

        public void SetFilter(string sNewFilter)
        {

            if (string.IsNullOrEmpty(this.BaseFilter))
            {

                this.Filter = sNewFilter;

            }
            else
            {

                this.Filter = this.BaseFilter + " And ( " + sNewFilter + " )";

            }

        }

        public QueryDef(string IDOName, List<string> selectProperties = null, Dictionary<string, object> updateProperties = null, Dictionary<string, object> insertProperties = null, string filter = "", string baseFilter = "", string orderBy = "", int recordCap = 0)
        {

            this.IDOName = IDOName;
            this.SelectProperties = selectProperties ?? new List<string>();
            this.UpdateProperties = updateProperties ?? new Dictionary<string, object>();
            this.InsertProperties = insertProperties ?? new Dictionary<string, object>();
            this.Filter = filter;
            this.BaseFilter = baseFilter;
            this.OrderBy = orderBy;
            this.RecordCap = recordCap;

        }

    }

}
