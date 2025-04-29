using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPILoadRequestOrderByField
    {

        // DIRECT PROPERTIES FROM JSON JOB FILE

        private string _FieldName;
        public string FieldName
        {
            get => _FieldName;
            set
            {
                if (_FieldName != value)
                {
                    _FieldName = value;
                }
            }
        }

        private bool _SortDesc;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool SortDesc
        {
            get => _SortDesc;
            set
            {
                if (_SortDesc != value)
                {
                    _SortDesc = value;
                }
            }
        }

        public string OrderBy => FieldName + (SortDesc ? " DESC" : "");

        public SalesforceAPILoadRequestOrderByField(string FieldName, bool SortDesc = false)
        {
            _FieldName = FieldName;
            _SortDesc = SortDesc;
        }

    }

}