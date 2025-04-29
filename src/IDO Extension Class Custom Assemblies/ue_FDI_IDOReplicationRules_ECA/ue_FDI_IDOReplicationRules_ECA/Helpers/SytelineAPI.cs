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
using ue_FDI_IDOReplicationRules_ECA.Models.SytelineAPI;

namespace ue_FDI_IDOReplicationRules_ECA.Helpers {

    public class SytelineAPI {

        public IIDOCommands IDOCommands { get; set; }
        public int BGTaskNum { get; set; }
        public int? DebugLevel { get; set; }

        public SytelineAPI(IIDOCommands IDOCommands, int BGTaskNum = 0, int? DebugLevel = null)
        {
            this.IDOCommands = IDOCommands;
            this.BGTaskNum = BGTaskNum;
            this.DebugLevel = DebugLevel;
        }

        public void WriteLogMessage(string sMessage, int iMinDebugLevel = 0) {

            if ((this.DebugLevel >= iMinDebugLevel) && (this.BGTaskNum > 0)) {

                this.IDOCommands?.Invoke(new InvokeRequestData {
                    IDOName = "ProcessErrorLogs",
                    MethodName = "AddProcessErrorLog",
                    Parameters = new InvokeParameterList() {
                        BGTaskNum,
                        sMessage,
                        0
                    }
                });

            }

        }

        public List<T> ExtractPropertiesAsList<T>(LoadCollectionResponseData oLdResponse, string propertyName) {

            int index = oLdResponse.PropertyList.IndexOf(propertyName);
            if (index > -1) {
                return oLdResponse.Items.Select(record => record.PropertyValues[index].GetValue<T>()).ToList();
            } else {
                return new List<T>();
            }

        }

        public List<Dictionary<string, T>> UnpackRecords<T>(GetRecordsResponseData records) {

            return records.LoadCollectionResponseData.Items.Select(
                record => records.PropertyKeys.ToDictionary(
                    propertyName =>
                    {
                        return propertyName.Key;
                    },
                    propertyName =>
                    {
                        if (!record.PropertyValues[propertyName.Value].IsNull)
                        {
                            return record.PropertyValues[propertyName.Value].GetValue<T>();
                        }
                        return default;
                    }

                )
            ).ToList();

        }

        public string BuildFilterString(List<string> filters)
        {

            return string.Join(" AND ", filters.Select(filter => "(" + filter + ")"));

        }

        public GetRecordsResponseData GetRecords(SytelineQuery QueryDef) {

            // GENERIC SYSTEM PROPS

            LoadCollectionRequestData oLoadRequest;
            LoadCollectionResponseData oLoadResponse = new LoadCollectionResponseData();

            if (this.IDOCommands != null) {

                // SET UP DATA LOAD REQUEST PARAMETERS

                oLoadRequest = new LoadCollectionRequestData() {
                    IDOName = QueryDef.IDOName,
                    RecordCap = QueryDef.RecordCap,
                    Filter = QueryDef.Filter,
                    OrderBy = QueryDef.OrderBy,
                    ReadMode = ReadMode.ReadUncommitted
                };
                oLoadRequest.PropertyList.SetProperties(string.Join(", ", QueryDef.SelectProperties));

                // LOAD THE RECORD(S)
                if (this.IDOCommands != null) {
                    oLoadResponse = this.IDOCommands.LoadCollection(oLoadRequest);
                }

            }

            Dictionary<string, int> propertyKeys = Enumerable.Range(0, QueryDef.SelectProperties.Count).ToDictionary(
                i => QueryDef.SelectProperties[i],
                i => i
            );

            // IF WE HAVE A VALID RECORD

            return new GetRecordsResponseData(
                propertyKeys: propertyKeys,
                loadCollectionResponseData: oLoadResponse
            );

        }
        
        public int CreateRecord(SytelineQuery QueryDef) {

            if (this.IDOCommands != null) {

                // GENERIC SYSTEM PROPS

                UpdateCollectionRequestData oUpdateRequest;
                IDOUpdateItem oUpdateItem;

                // CREATE THE UPDATE REQUEST WRAPPER

                oUpdateRequest = new UpdateCollectionRequestData(QueryDef.IDOName);
                oUpdateItem = new IDOUpdateItem(UpdateAction.Insert);

                foreach (KeyValuePair<string, object> property in QueryDef.InsertProperties) {
                    oUpdateItem.Properties.Add(property.Key, property.Value ?? "", true);
                }

                oUpdateRequest.Items.Add(oUpdateItem);

                // SEND THE UPDATE REQUEST

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;

            }

            return 0;

        }

        public int UpdateRecords(string IDOName = null, Dictionary<string, Dictionary<string, object>> itemUpdates = null, LoadCollectionResponseData oLoadResponse = null)
        {

            if (this.IDOCommands != null)
            {

                // GENERIC SYSTEM PROPS

                UpdateCollectionRequestData oUpdateRequest;

                if (IDOName != null && itemUpdates != null)
                {

                    oUpdateRequest = new UpdateCollectionRequestData(IDOName);

                    // CREATE THE UPDATE REQUEST WRAPPER

                    foreach (KeyValuePair<string, Dictionary<string, object>> itemUpdate in itemUpdates)
                    {

                        oUpdateRequest.Items.Add(
                            this.BuildUpdateItem(
                                itemID: itemUpdate.Key,
                                propertyUpdates: itemUpdate.Value
                            )
                        );

                    }

                    // SEND THE UPDATE REQUEST

                    this.IDOCommands.UpdateCollection(oUpdateRequest);

                }
                else if (oLoadResponse != null)
                {

                    oUpdateRequest = new UpdateCollectionRequestData(oLoadResponse.IDOName);

                    int iCounter;

                    for (iCounter = 0; iCounter <= oLoadResponse.Items.Count - 1; iCounter++)
                    {

                        IDOItem record = oLoadResponse.Items[iCounter];

                        oUpdateRequest.Items.Add(
                            this.BuildUpdateItem(
                                itemID: record.ItemID,
                                propertyUpdates: oLoadResponse.PropertyList.List.Zip(record.PropertyValues, (k, v) => new { k, v }).ToDictionary(x => x.k, x => (object)x.v.Value)
                            )
                        );

                    }

                    this.IDOCommands.UpdateCollection(oUpdateRequest);

                }

                return 1;


            }

            return 0;

        }

        public int UpdateRecord(string IDOName, string itemID, Dictionary<string, object> propertyUpdates)
        {

            if (this.IDOCommands != null)
            {

                // GENERIC SYSTEM PROPS
                UpdateCollectionRequestData oUpdateRequest;

                // CREATE THE UPDATE REQUEST WRAPPER

                oUpdateRequest = new UpdateCollectionRequestData(IDOName);

                oUpdateRequest.Items.Add(this.BuildUpdateItem(
                    itemID: itemID,
                    propertyUpdates: propertyUpdates
                ));

                // SEND THE UPDATE REQUEST

                this.IDOCommands.UpdateCollection(oUpdateRequest);

                return 1;


            }

            return 0;

        }
        
        private IDOUpdateItem BuildUpdateItem(string itemID, Dictionary<string, object> propertyUpdates)
        {

            // CREATE AN UPDATE ITEM OBJECT

            IDOUpdateItem oUpdateItem = new IDOUpdateItem(UpdateAction.Update, itemID) { ItemID = itemID };

            foreach (KeyValuePair<string, object> propertyUpdate in propertyUpdates)
            {

                // IF WE HAVE A VALUE IN THE VALUE PROP, USE THAT. OTHERWISE, USE WHAT IS IN THE LOAD COLLECTION.

                oUpdateItem.Properties.Add(propertyUpdate.Key, propertyUpdate.Value ?? "", true);

            }

            return oUpdateItem;

        }

    }

}