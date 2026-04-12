using Infor.DocumentManagement.ICP;
using Mongoose.Core.Extensions;
using Mongoose.IDO;
using Mongoose.IDO.DataAccess;
using Mongoose.IDO.Metadata;
using Mongoose.IDO.Protocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ue_AIR_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_AIR_IDOReplicationRules_ECA.Helpers {

    public class SytelineInternalAPI {

        public IIDOCommands IDOCommands { get; set; }

        public SytelineInternalAPI(IIDOCommands IDOCommands)
        {
            this.IDOCommands = IDOCommands;
        }

        public T ParseIDOPropertyValue<T>(IDOPropertyValue value)
        {

            if (!value.IsNull)
            {
                return value.GetValue<T>();
            }

            return default;

        }

        public string BuildFilterString(List<string> filters)
        {

            return string.Join(" AND ", filters.Select(filter => "(" + filter + ")"));

        }

        public LoadRecordsResponseData LoadRecords(string IDOName, string filter, string orderBy, List<string> properties, int recordCap = 0)
        {

            // GENERIC SYSTEM PROPS

            LoadCollectionRequestData oLoadRequest;
            LoadCollectionResponseData oLoadResponse = new LoadCollectionResponseData();

            // SET UP DATA LOAD REQUEST PARAMETERS

            oLoadRequest = new LoadCollectionRequestData()
            {
                IDOName = IDOName,
                RecordCap = recordCap,
                Filter = filter,
                OrderBy = orderBy,
                ReadMode = ReadMode.ReadCommitted
            };
            oLoadRequest.PropertyList.SetProperties(string.Join(", ", properties));

            // LOAD THE RECORD(S)

            oLoadResponse = this.IDOCommands.LoadCollection(oLoadRequest);

            // IF WE HAVE A VALID RECORD

            return new LoadRecordsResponseData(
                queryIDOName: IDOName,
                queryFilter: filter,
                queryOrderBy: orderBy,
                queryProperties: properties,
                loadCollectionResponseData: oLoadResponse,
                loadCollectionRequestData: oLoadRequest
            );

        }

        public LoadRecordsResponseData RefreshRequest(LoadRecordsResponseData loadRecordsResponseData)
        {

            // REFRESH THE RECORD(S)

            loadRecordsResponseData.LoadCollectionResponseData = this.IDOCommands.LoadCollection(loadRecordsResponseData.LoadCollectionRequestData);

            return loadRecordsResponseData;

        }

        public int CreateRecord(string IDOName, IDOUpdateItem record)
        {

            if (this.IDOCommands != null)
            {


                // CREATE THE UPDATE REQUEST WRAPPER

                UpdateCollectionRequestData oUpdateRequest = new UpdateCollectionRequestData(IDOName);
                oUpdateRequest.Items.Add(record);

                // SEND THE UPDATE REQUEST

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;

            }

            return 0;

        }

        public int CreateRecords(string IDOName, List<IDOUpdateItem> records)
        {

            if (this.IDOCommands != null)
            {

                // CREATE THE UPDATE REQUEST WRAPPER

                UpdateCollectionRequestData oUpdateRequest = new UpdateCollectionRequestData(IDOName);
                oUpdateRequest.Items.AddRange(records);

                // SEND THE UPDATE REQUEST

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;

            }

            return 0;

        }

        public int UpdateRecord(string IDOName, IDOUpdateItem record)
        {

            if (this.IDOCommands != null)
            {

                // GENERIC SYSTEM PROPS

                UpdateCollectionRequestData oUpdateRequest = new UpdateCollectionRequestData(IDOName);
                oUpdateRequest.Items.Add(record);

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;

            }

            return 0;

        }

        public int UpdateRecords(string IDOName, List<IDOUpdateItem> records)
        {

            if (this.IDOCommands != null)
            {

                // GENERIC SYSTEM PROPS

                UpdateCollectionRequestData oUpdateRequest = new UpdateCollectionRequestData(IDOName);
                oUpdateRequest.Items.AddRange(records);

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;

            }

            return 0;

        }

        public IDOUpdateItem BuildInsertItem(Dictionary<string, object> propertyUpdates)
        {

            // CREATE AN UPDATE ITEM OBJECT

            IDOUpdateItem oUpdateItem = new IDOUpdateItem(UpdateAction.Insert);

            foreach (KeyValuePair<string, object> propertyUpdate in propertyUpdates)
            {

                // IF WE HAVE A VALUE IN THE VALUE PROP, USE THAT. OTHERWISE, USE WHAT IS IN THE LOAD COLLECTION.

                if (propertyUpdate.Value != null)
                {

                    oUpdateItem.Properties.Add(propertyUpdate.Key, propertyUpdate.Value, true);

                }

            }

            return oUpdateItem;

        }

        public IDOUpdateItem BuildUpdateItem(string itemID = null, List<IDOUpdateProperty> propertyUpdates = null)
        {

            IDOUpdateItem oUpdateItem = itemID != null ? new IDOUpdateItem(UpdateAction.Update, itemID)
            {
                ItemID = itemID
            } : new IDOUpdateItem(UpdateAction.Update)
            {
                UseOptimisticLocking = false
            };

            if (propertyUpdates != null)
            {

                foreach (IDOUpdateProperty propertyUpdate in propertyUpdates)
                {

                    // IF WE HAVE A VALUE IN THE VALUE PROP, USE THAT. OTHERWISE, USE WHAT IS IN THE LOAD COLLECTION.

                    if (propertyUpdate.Value != null)
                    {

                        oUpdateItem.Properties.Add(propertyUpdate.Name, propertyUpdate.Value, propertyUpdate.Modified);

                    }

                }

            }

            return oUpdateItem;

        }

    }

}