using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI
{
    public class IDOProperty
    {

        public string Name { get; set; }

        public string Value { get; set; }

        public string Filter { get; set; }

        public IDOProperty(string name = "", string value = "", string filter = "")
        {

            this.Name = name;
            this.Value = value;
            this.Filter = filter;

        }

    }

}
