using Mongoose.IDO.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ue_FDI_IDOReplicationRules_ECA.Helpers
{
    public class Utils
    {
        public IIDOCommands IDOCommands { get; set; }
        public int BGTaskNum { get; set; }
        public int? DebugLevel { get; set; }

        public Utils(IIDOCommands IDOCommands, int BGTaskNum = 0, int? DebugLevel = null)
        {
            this.IDOCommands = IDOCommands;
            this.BGTaskNum = BGTaskNum;
            this.DebugLevel = DebugLevel;
        }

        public void WriteLogMessage(string sMessage, int iMinDebugLevel = 0)
        {

            if ((this.DebugLevel >= iMinDebugLevel) && (this.BGTaskNum > 0))
            {

                this.IDOCommands?.Invoke(new InvokeRequestData
                {
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


    }
}
