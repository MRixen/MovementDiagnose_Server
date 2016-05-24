using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace App1
{

    // TODO Add delay time


    class ServerComm
    {
        private int i = 0;

        public StreamSocketListener Listener { get; set; }

        private GlobalData globalData = new GlobalData();
        private MainPage mainPage;

        // This is the static method used to start listening for connections.

        public async Task<bool> StartServer()
        {
            Listener = new StreamSocketListener();
            // Removes binding first in case it was already bound previously.
            Listener.ConnectionReceived -= Listener_ConnectionReceived;
            Listener.ConnectionReceived += Listener_ConnectionReceived;
            try
            {
                await Listener.BindServiceNameAsync("4555"); // Your port goes here.
                Debug.Write("Server started \n");
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

            // TODO Wait some time to get things ready

            Debug.Write("Client is connected. \n");

            // Set flag to begin the sending of sensor data (in MainPage) when client is connected
            globalData.clientIsConnected = true;

            Stream outStream = args.Socket.OutputStream.AsStreamForWrite();
            StreamWriter writer = new StreamWriter(outStream);

            while (true)
            {
                try
                {
                    //Send data to client
                    //Debug.Write("Send to client \n");

                    bool[] bufferState = globalData.getBufferState();
                    string[] sendbuffer = globalData.getSendBuffer();

                    //Debug.Write("sendBufferLength: " + (sendbuffer.Length - 1).ToString() + "\n");
                    //Debug.Write("sendBufferLength: " + (sendbuffer.Length - 1).ToString() + "\n");

                    // for (int i = 0; i <= sendbuffer.Length-1; i++)
                    //{
                    if (bufferState[i])
                    {                        
                        //await writer.WriteLineAsync(sendbuffer[i]);
                        writer.Write(sendbuffer[i]);
                        //writer.WriteLine();
                        writer.Flush();
                        //await writer.FlushAsync();
                        // TODO Add delay time
                        //globalData.McpExecutionIsActive = true;
                        Debug.Write("Send data to client / database: " + sendbuffer[i] + "\n");
                    }
                    sendbuffer[i] = "";
                    bufferState[i] = false;
                    globalData.setBufferState(bufferState);
                    globalData.setSendBuffer(sendbuffer);
                    //}

                    if (i < sendbuffer.Length - 1) i++;
                    else i = 0;

                    //// Receive data from client (handshake...)
                    //Debug.Write("Receive from client \n");
                    ////Read line from the remote client.
                    //Stream inStream = args.Socket.InputStream.AsStreamForRead();
                    //StreamReader reader = new StreamReader(inStream);
                    //string request = await reader.ReadLineAsync();
                    //Debug.Write("Received data: " + request + " \n");
                }
                catch (Exception ex)
                {
                    Debug.Write("Exception in sending \n");

                    //writer.DetachStream();
                    // reader.DetachStream();
                    return;
                }
            }
        }

        public GlobalData getGlobalData()
        {
            return globalData;
        }

    }
}
