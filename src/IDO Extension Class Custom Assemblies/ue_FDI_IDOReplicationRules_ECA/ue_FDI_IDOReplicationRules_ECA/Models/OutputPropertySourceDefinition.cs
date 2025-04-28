using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Models
{
    public class OutputPropertySourceDefinition
    {

        public OutputPropertySourceType Type { get; set; }

        public string IDOName { get; set; }

        public string PropertyName { get; set; }

        public string Value { get; set; }

        public string KeyFilter { get; set; }

        public string KeyPropertyName { get; set; }

        public string ValuePropertyName { get; set; }

        public string Delimiter { get; set; }

        public OutputPropertySourceDefinition(OutputPropertySourceType type, string IDOName = null, string propertyName = null, string value = null, string keyFilter = null, string keyPropertyName = null, string valuePropertyName = null, string delimiter = ", ")
        {

            this.IDOName = "";
            this.PropertyName = "";
            this.Value = "";
            this.KeyFilter = "";
            this.KeyPropertyName = "";
            this.ValuePropertyName = "";
            this.Delimiter = "";

            switch (type)
            {

                case OutputPropertySourceType.MainIDOProp:

                    this.Type = type;
                    this.IDOName = IDOName ?? "";
                    this.PropertyName = propertyName ?? "";
                    this.Delimiter = delimiter;

                    break;

                case OutputPropertySourceType.ChildIDOPropConcat:

                    this.Type = type;
                    this.IDOName = IDOName ?? "";
                    this.KeyFilter = keyFilter;
                    this.KeyPropertyName = keyPropertyName;
                    this.ValuePropertyName = valuePropertyName ?? "";
                    this.Delimiter = delimiter;

                    break;

                case OutputPropertySourceType.Literal:

                    this.Type = type;
                    this.Value = value ?? "";
                    this.Delimiter = delimiter;

                    break;

            }

        }

    }

    public enum OutputPropertySourceType
    {
        MainIDOProp,
        ChildIDOPropConcat,
        Literal
    }

}
