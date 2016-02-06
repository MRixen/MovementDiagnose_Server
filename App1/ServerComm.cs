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
    class ServerComm
    {
        private  Timer timer;
        private int timerTime = 10;

        public StreamSocketListener Listener { get; set; }

        private GlobalData globalData = new GlobalData();

        // This is the static method used to start listening for connections.

        public async Task<bool> StartServer()
        {
            timer = new Timer(TimerCallback, null, 0, timerTime);

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
            while (true)
            {
                try
                {
                    //Send data to client
                    Debug.Write("Send to client \n");
                    Stream outStream = args.Socket.OutputStream.AsStreamForWrite();
                    StreamWriter writer = new StreamWriter(outStream);


                    bool[] bufferState = globalData.getBufferState();
                    string[] sendbuffer = globalData.getSendBuffer();

                    for (int i = 0; i <= sendbuffer.Length-1; i++)
                    {
                        if (bufferState[i])
                        {
                            await writer.WriteLineAsync(sendbuffer[i]);
                            await writer.FlushAsync();
                            Debug.Write("Send data: " + sendbuffer[i] + "\n");
                        }
                        sendbuffer[i] = "";
                        bufferState[i] = false;
                        globalData.setBufferState(bufferState);
                        globalData.setSendBuffer(sendbuffer);
                    }

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
                    //writer.DetachStream();
                    // reader.DetachStream();
                    return;
                }
            }
        }

        private void TimerCallback(object state)
        {

        }

        private void myTestRoutine()
        {

        }

    }
}
