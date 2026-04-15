using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models
{

    public class ReplicationFieldTransformType
    {
        public string Value
        {
            get; set;
        }

        public string Label
        {
            get; set;
        }

        public Func<string, object> Logic
        {
            get; set;
        }

        public ReplicationFieldTransformType(string Value, string Label = null, Func<string, object> Logic = null)
        {
            this.Value = Value;
            this.Label = Label ?? Value;
            this.Logic = Logic ?? ((string input) => (input));
        }

    }

}