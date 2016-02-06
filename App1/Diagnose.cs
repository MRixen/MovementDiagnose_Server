using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App1
{
    class Diagnose
    {

        private GlobalData globalData = new GlobalData();
        private int cntr = 0;

        public void sendToSocket(string id, string msg)
        {
            // Get buffer data
            bool[] bufferState = globalData.getBufferState();
            string[] sendBuffer = globalData.getSendBuffer();

            // Set message to local buffer
            sendBuffer[cntr] = ":"+id+":" + msg + ";";
            bufferState[cntr] = true;

            // Set local buffer to global buffer
            globalData.setBufferState(bufferState);
            globalData.setSendBuffer(sendBuffer);

            cntr += 1;
            if (cntr>=bufferState.Length-1)  cntr = 0;
        }
    }
}
