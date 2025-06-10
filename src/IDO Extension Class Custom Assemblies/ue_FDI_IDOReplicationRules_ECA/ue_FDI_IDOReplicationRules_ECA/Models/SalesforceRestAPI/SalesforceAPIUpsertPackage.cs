using System;
using System.Collections.Generic;
using System.Linq;

namespace ue_FDI_IDOReplicationRules_ECA.Models.SalesforceRestAPI
{
    public class SalesforceAPIUpsertPackage
    {
        public bool allOrNone
        {
            get; set;
        }

        public List<Dictionary<string, object>> records
        {
            get; set;
        } = new List<Dictionary<string, object>>();


        public SalesforceAPIUpsertPackage(List<Dictionary<string, object>> records, bool allOrNone = false)
        {

            this.allOrNone = allOrNone;
            if (records.Count > 0)
            {

                string[] keyParts;
                List<string> lookupFieldNames = records[0].Keys.Where(key => key.Contains('.')).ToList();

                this.records = records.Select(record =>
                {

                    lookupFieldNames.ForEach(key =>
                    {

                        keyParts = key.Split('.');

                        if (record[key] != null && record[key].ToString() != "")
                        {
                            record[keyParts[0]] = new Dictionary<string, object>() { { keyParts[1], record[key] } };
                        }
                        else
                        {
                            record[(keyParts[0].Contains("__r") ? keyParts[0].Replace("__r", "__c") : keyParts[0] + "Id")] = null;
                        }

                        record.Remove(key);

                    });

                    foreach (string key in record.Keys)
                    {
                        if ((record[key] ?? "").ToString() == "")
                        {
                            record[key] = null;
                        }
                    }

                    return record;

                }).ToList();

            }

        }

    }

}