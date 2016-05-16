using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace App1
{
    class GlobalData
    {
        public string[] sendBuffer = {"", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", ""};
        public bool[] bufferState = {false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false};
        
        public string[] getSendBuffer()
        {
            return sendBuffer;
        }

        public void setSendBuffer(string[] sendBuffer)
        {
            this.sendBuffer = sendBuffer;
        }

        public bool[] getBufferState()
        {
            return bufferState;
        }

        public void setBufferState(bool[] bufferState)
        {
            this.bufferState = bufferState;
        }

}
}
