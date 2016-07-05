using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CanTest;
using Windows.Devices.Gpio;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace App1
{
    class ServerComm
    {
        private int i = 0;

        public StreamSocketListener Listener { get; set; }
        private MainPage mainPage;
        private GlobalDataSet globalDataSet;

        public ServerComm(GlobalDataSet globalDataSet)
        {
            this.globalDataSet = globalDataSet;
        }

        public async Task<bool> StartServer()
        {
            Listener = new StreamSocketListener();
            // Removes binding first in case it was already bound previously.
            Listener.ConnectionReceived -= Listener_ConnectionReceived;
            Listener.ConnectionReceived += Listener_ConnectionReceived;
            try
            {
                await Listener.BindServiceNameAsync("4555"); // Your port goes here.
                if(globalDataSet.DebugMode) Debug.Write("Server started \n");
                return true;
            }
            catch (Exception ex)
            {
                Listener.ConnectionReceived -= Listener_ConnectionReceived;
                Listener.Dispose();
                return false;
            }
        }

        private async void Listener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            var remoteAddress = args.Socket.Information.RemoteAddress.ToString();

            if(globalDataSet.DebugMode) Debug.Write("Client is connected. \n");

            // Set flag to begin the sending of sensor data (in MainPage) when client is connected
            globalDataSet.clientIsConnected = true;

            Stream outStream = args.Socket.OutputStream.AsStreamForWrite();
            StreamWriter writer = new StreamWriter(outStream);

            while (true)
            {
                try
                {
                    bool[] bufferState = globalDataSet.getBufferState();
                    string[] sendbuffer = globalDataSet.getSendBuffer();

                    if (bufferState[i])
                    {                        
                        writer.Write(sendbuffer[i]);
                        writer.Flush();
                    }
                    sendbuffer[i] = "";
                    bufferState[i] = false;
                    globalDataSet.setBufferState(bufferState);
                    globalDataSet.setSendBuffer(sendbuffer);

                    if (i < sendbuffer.Length - 1) i++;
                    else i = 0;                  
                }
                catch (Exception ex)
                {
                    if(globalDataSet.DebugMode) Debug.Write("Exception in sending \n");
                    globalDataSet.clientIsConnected = false;
                    return;
                }
            }
        }
    }
}
