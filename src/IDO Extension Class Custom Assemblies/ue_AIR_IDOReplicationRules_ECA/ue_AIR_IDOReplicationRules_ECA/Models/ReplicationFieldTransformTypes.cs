using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_AIR_IDOReplicationRules_ECA.Models
{

    /***********************************************************************************************************/
    /*
    /* The transformations a map field can apply (SourceTransformation). Each takes the value as the IDO returns
    /* it (DateTime, decimal, byte, string, ...) and returns the value to send. Nulls are never passed in. The
    /* Value strings must match the SourceTransformation inline list on ue_AIR_IDOReplicationMapFields.
    /*
    /***********************************************************************************************************/

    public static class ReplicationFieldTransformTypes
    {

        private static readonly string[] TruthyStrings = new string[] { "1", "true", "yes", "y" };

        private static readonly string[] DateTimeFormats = new string[]
        {
            "yyyyMMdd HH:mm:ss.fff",
            "yyyyMMdd HH:mm:ss",
            "yyyyMMdd",
            "s",
            "o",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd"
        };

        public static List<ReplicationFieldTransformType> Definitions => new List<ReplicationFieldTransformType>() {
            new ReplicationFieldTransformType(
                Value: "",
                Label: "None"
            ),
            new ReplicationFieldTransformType(
                Value: "DateTimeToDate",
                Label: "DateTime to Date",
                Logic: (object input) => ReplicationFieldTransformTypes.ToDateTime(input).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ),
            new ReplicationFieldTransformType(
                Value: "DecimalToInteger",
                Label: "Decimal to Integer",
                Logic: (object input) => (long)Math.Round(ReplicationFieldTransformTypes.ToDecimal(input), MidpointRounding.AwayFromZero)
            ),
            new ReplicationFieldTransformType(
                Value: "DecimalToUSDString",
                Label: "Decimal to USD String",
                Logic: (object input) => Math.Round(ReplicationFieldTransformTypes.ToDecimal(input), 2, MidpointRounding.AwayFromZero).ToString("$#,##0.00", CultureInfo.InvariantCulture)
            ),
            new ReplicationFieldTransformType(
                Value: "ByteToBoolean",
                Label: "Checkbox (Byte) to Boolean",
                Logic: (object input) => ReplicationFieldTransformTypes.IsTruthy(input)
            ),
            new ReplicationFieldTransformType(
                Value: "ByteToTrueFalseString",
                Label: "Checkbox (Byte) to True/False String",
                Logic: (object input) => ReplicationFieldTransformTypes.IsTruthy(input) ? "True" : "False"
            ),
            new ReplicationFieldTransformType(
                Value: "ByteToYesNoString",
                Label: "Checkbox (Byte) to Yes/No String",
                Logic: (object input) => ReplicationFieldTransformTypes.IsTruthy(input) ? "Yes" : "No"
            )
        };

        public static List<string> Values => ReplicationFieldTransformTypes.Definitions.Select(def =>
            def.Value
        ).ToList();

        public static List<KeyValuePair<string, string>> ValueLabelPairs => ReplicationFieldTransformTypes.Definitions.Select(def =>
            new KeyValuePair<string, string>(def.Value, def.Label)
        ).ToList();

        public static Func<object, object> GetLogic(string value)
        {

            ReplicationFieldTransformType match = ReplicationFieldTransformTypes.Definitions.FirstOrDefault(def => def.Value == value);

            return match != null ? match.Logic : ((object input) => input);

        }



        /***********************************************************************************************************/
        /************************************************* HELPERS *************************************************/
        /***********************************************************************************************************/

        // THE IDO USUALLY RETURNS A DateTime. STRINGS ARE READ IN MONGOOSE'S yyyyMMdd FORMAT OR ISO FORMATS, WITH INVARIANT CULTURE.

        private static DateTime ToDateTime(object input)
        {

            if (input is DateTime)
            {
                return (DateTime)input;
            }

            string text = (Convert.ToString(input, CultureInfo.InvariantCulture) ?? "").Trim();
            DateTime parsed;

            if (DateTime.TryParseExact(text, ReplicationFieldTransformTypes.DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return DateTime.Parse(text, CultureInfo.InvariantCulture);

        }

        private static decimal ToDecimal(object input)
        {

            if (input is string)
            {
                return decimal.Parse(((string)input).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            return Convert.ToDecimal(input, CultureInfo.InvariantCulture);

        }

        private static bool IsTruthy(object input)
        {

            if (input is bool)
            {
                return (bool)input;
            }

            if (input is string)
            {
                return ReplicationFieldTransformTypes.TruthyStrings.Contains(((string)input).Trim().ToLowerInvariant());
            }

            try
            {
                return Convert.ToDecimal(input, CultureInfo.InvariantCulture) != 0;
            }
            catch (Exception)
            {
                return ReplicationFieldTransformTypes.TruthyStrings.Contains((Convert.ToString(input, CultureInfo.InvariantCulture) ?? "").Trim().ToLowerInvariant());
            }

        }

    }

}
