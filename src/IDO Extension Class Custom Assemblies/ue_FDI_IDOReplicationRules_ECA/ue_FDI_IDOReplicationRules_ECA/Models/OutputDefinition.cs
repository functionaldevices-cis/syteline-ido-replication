using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class OutputDefinition
    {

        public string PrimaryIDOName { get; set; }

        public string PrimaryIDOFilter { get; set; }

        public string UniqueIDPropertyName { get; set; }

        public List<OutputPropertyDefinition> Properties { get; set; }

        public OutputDefinition(string primaryIDOName, string primaryIDOFilter, string uniqueIDPropertyName, List<OutputPropertyDefinition> properties)
        {

            this.PrimaryIDOName = primaryIDOName;
            this.PrimaryIDOFilter = primaryIDOFilter;
            this.UniqueIDPropertyName = uniqueIDPropertyName;
            this.Properties = properties;

        }

    }

}
