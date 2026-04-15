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

    public static class ReplicationFieldTransformTypes
    {

        private static List<string> FuzzyTrue => new List<string>() { "1", "True", "true", "yes", "y" };

        public static List<ReplicationFieldTransformType> Definitions => new List<ReplicationFieldTransformType>() {
                new ReplicationFieldTransformType(
                    Value: "",
                    Label: "None"
                ),
                new ReplicationFieldTransformType(
                    Value: "DateTimeToDate",
                    Logic: (string input) => (
                        input == "" ? "" : DateTime.ParseExact(input, "s", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd") ?? ""
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "DecimalToInteger",
                    Logic: (string input) => (
                        Math.Round(decimal.Parse(input)).ToString() ?? ""
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "DecimalToUSD",
                    Logic: (string input) => (
                        Math.Round(decimal.Parse(input), 2).ToString("$###,###,###,###,#0.00") ?? ""
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "FuzzyBoolToTinyInt",
                    Logic: (string input) => (
                        FuzzyTrue.Contains(input) ? "Yes" : "No"
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "FuzzyBoolToTrueFalse",
                    Logic: (string input) => (
                        FuzzyTrue.Contains(input) ? true : false
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "FuzzyBoolToTrueFalseString",
                    Logic: (string input) => (
                        FuzzyTrue.Contains(input) ? "True" : "False"
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "FuzzyBoolToYesNoString",
                    Logic: (string input) => (
                        FuzzyTrue.Contains(input) ? "Yes" : "No"
                    )
                ),
                new ReplicationFieldTransformType(
                    Value: "Custom"
                )
            };

        public static List<string> Values => ReplicationFieldTransformTypes.Definitions.Select(def =>
            def.Value
        ).ToList();

        public static List<KeyValuePair<string, string>> ValueLabelPairs => ReplicationFieldTransformTypes.Definitions.Select(def =>
            new KeyValuePair<string, string>(def.Value, def.Label)
        ).ToList();

        public static Func<string, object> GetLogic(string value)
        {
            List<ReplicationFieldTransformType> matching = ReplicationFieldTransformTypes.Definitions.Where(def => def.Value == value).ToList();
            if (matching.Count > 0)
            {
                return matching[0].Logic;
            }
            else
            {
                return ((string input) => (input));
            }
        }

    }


}