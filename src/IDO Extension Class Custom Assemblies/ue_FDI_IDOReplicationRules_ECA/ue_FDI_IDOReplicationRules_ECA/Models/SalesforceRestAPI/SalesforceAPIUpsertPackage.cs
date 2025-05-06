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
        }


        public SalesforceAPIUpsertPackage(List<Dictionary<string, object>> records, bool allOrNone = false)
        {
            string[] keyParts;

            this.allOrNone = allOrNone;
            this.records = records.Count == 0 ? [] : records.Select(record =>
            {

                records[0].Keys.Where(key => key.Contains('.')).ToList().ForEach(key => {

                    keyParts = key.Split('.');

                    record[keyParts[0]] = new Dictionary<string, string>() { { keyParts[1], string.Concat(record[key]) } };
                    record.Remove(key);

                });

                return record;

            }).ToList();
        }

    }

}