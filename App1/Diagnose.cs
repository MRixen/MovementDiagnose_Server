using CanTest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App1
{
    class Diagnose
    {

        private GlobalDataSet globalDataSet;
        private int cntr = 0;

        public Diagnose(GlobalDataSet globalDataSet)
        {
            this.globalDataSet = globalDataSet;
        }

        public void sendToSocket(string id, string msg)
        {
            // Get buffer data
            bool[] bufferState = globalDataSet.getBufferState();
            string[] sendBuffer = globalDataSet.getSendBuffer();

            // Set message to local buffer
            sendBuffer[cntr] = ":"+id+":" + msg + ";";
            bufferState[cntr] = true;

            if(globalDataSet.DebugMode) Debug.Write("sendBuffer[cntr]: " + sendBuffer[cntr]);

            // Set local buffer to global buffer
            globalDataSet.setBufferState(bufferState);
            globalDataSet.setSendBuffer(sendBuffer);


            cntr += 1;
            if (cntr>=bufferState.Length-1)  cntr = 0;
        }
    }
}
