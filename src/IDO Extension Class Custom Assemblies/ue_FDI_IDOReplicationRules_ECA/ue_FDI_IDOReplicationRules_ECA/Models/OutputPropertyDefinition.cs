using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class OutputPropertyDefinition
    {

        public string Name { get; set; }

        public OutputPropertySourceDefinition Source { get; set; }

        public OutputPropertyDefinition(string name, OutputPropertySourceDefinition source)
        {

            this.Name = name;
            this.Source = source;

        }

    }

}
