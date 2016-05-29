﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace CanTest
{
    class GlobalDataSet
    {
        private int mAX_WAIT_TIME = 800;
        private int TIME_SLOW_DOWN_CODE = 1;
        private bool spi_not_initialized = true;
        private GpioPin mCP2515_PIN_CS_SENDER, mCP2515_PIN_INTE_SENDER;
        private GpioPin mCP2515_PIN_CS_RECEIVER, mCP2515_PIN_INTE_RECEIVER;
        private GpioPin rEQUEST_DATA, sTART_PIN_OUT, aRDUINO_TEST_PIN;
        private MCP2515 mcp2515;
        private SpiDevice spiDevice;
        private Logic_Mcp2515_Sender logic_Mcp2515_Sender;
        private Logic_Mcp2515_Receiver logic_Mcp2515_Receiver;
        private bool debugMode;

        public GlobalDataSet()
        {
            debugMode = false;
            mcp2515 = new MCP2515();
            logic_Mcp2515_Sender = new Logic_Mcp2515_Sender(this);
            logic_Mcp2515_Receiver = new Logic_Mcp2515_Receiver(this);
        }

        public int MAX_WAIT_TIME
        {
            get
            {
                return mAX_WAIT_TIME;
            }

            set
            {
                mAX_WAIT_TIME = value;
            }
        }

        public async void init_mcp2515_task()
        {
            await Task.Run(() => init_mcp2515());
        }

        private void init_mcp2515()
        {
            logic_Mcp2515_Receiver.init_mcp2515_receiver();
        }

        public bool Spi_not_initialized
        {
            get
            {
                return spi_not_initialized;
            }

            set
            {
                spi_not_initialized = value;
            }
        }

        public GpioPin MCP2515_PIN_CS_SENDER
        {
            get
            {
                return mCP2515_PIN_CS_SENDER;
            }

            set
            {
                mCP2515_PIN_CS_SENDER = value;
            }
        }

        public GpioPin MCP2515_PIN_INTE_SENDER
        {
            get
            {
                return mCP2515_PIN_INTE_SENDER;
            }

            set
            {
                mCP2515_PIN_INTE_SENDER = value;
            }
        }

        public GpioPin MCP2515_PIN_CS_RECEIVER
        {
            get
            {
                return mCP2515_PIN_CS_RECEIVER;
            }

            set
            {
                mCP2515_PIN_CS_RECEIVER = value;
            }
        }

        public GpioPin MCP2515_PIN_INTE_RECEIVER
        {
            get
            {
                return mCP2515_PIN_INTE_RECEIVER;
            }

            set
            {
                mCP2515_PIN_INTE_RECEIVER = value;
            }
        }

        public SpiDevice SPIDEVICE
        {
            get
            {
                return spiDevice;
            }

            set
            {
                spiDevice = value;
            }
        }

        public Logic_Mcp2515_Receiver LOGIC_MCP2515_RECEIVER
        {
            get
            {
                return logic_Mcp2515_Receiver;
            }

            set
            {
                logic_Mcp2515_Receiver = value;
            }
        }

        public Logic_Mcp2515_Sender LOGIC_MCP2515_SENDER
        {
            get
            {
                return logic_Mcp2515_Sender;
            }

            set
            {
                logic_Mcp2515_Sender = value;
            }
        }

        public GpioPin REQUEST_DATA
        {
            get
            {
                return rEQUEST_DATA;
            }

            set
            {
                rEQUEST_DATA = value;
            }
        }

        public GpioPin START_PIN_OUT
        {
            get
            {
                return sTART_PIN_OUT;
            }

            set
            {
                sTART_PIN_OUT = value;
            }
        }

        public GpioPin REQUEST_DATA_HANDSHAKE
        {
            get
            {
                return aRDUINO_TEST_PIN;
            }

            set
            {
                aRDUINO_TEST_PIN = value;
            }
        }

        internal MCP2515 Mcp2515
        {
            get
            {
                return mcp2515;
            }

            set
            {
                mcp2515 = value;
            }
        }

        public bool DebugMode
        {
            get
            {
                return debugMode;
            }

            set
            {
                debugMode = value;
            }
        }

        public void writeSimpleCommandSpi(byte command, GpioPin cs_pin)
        {
            cs_pin.Write(GpioPinValue.Low);
            spiDevice.Write(new byte[] { command });
            cs_pin.Write(GpioPinValue.High);
            Task.Delay(-1).Wait(TIME_SLOW_DOWN_CODE);
        }

        public byte[] readSimpleCommandSpi(byte registerAddress, GpioPin cs_pin)
        {
            byte[] returnMessage = new byte[1];
            byte[] spiMessage = new byte[1];

            cs_pin.Write(GpioPinValue.Low);
            spiDevice.Write(new byte[] { mcp2515.SPI_INSTRUCTION_READ, registerAddress });
            spiDevice.Read(returnMessage);
            cs_pin.Write(GpioPinValue.High);
            Task.Delay(-1).Wait(TIME_SLOW_DOWN_CODE);

            return returnMessage;
        }

        public byte mcp2515_execute_read_command(byte registerToRead, GpioPin cs_pin)
        {
            byte[] returnMessage = new byte[1];
            byte[] sendMessage = new byte[1];

            // Enable device
            cs_pin.Write(GpioPinValue.Low);

            // Write spi instruction read  
            sendMessage[0] = mcp2515.SPI_INSTRUCTION_READ;
            spiDevice.Write(sendMessage);

            // Write the address of the register to read
            sendMessage[0] = registerToRead;
            spiDevice.Write(sendMessage);
            spiDevice.Read(returnMessage);

            // Disable device
            cs_pin.Write(GpioPinValue.High);
            Task.Delay(-1).Wait(TIME_SLOW_DOWN_CODE);

            return returnMessage[0];
        }

        public void mcp2515_execute_write_command(byte[] spiMessage, GpioPin cs_pin)
        {
            // Enable device
            cs_pin.Write(GpioPinValue.Low);

            // Write spi instruction write  
            spiDevice.Write(new byte[] { mcp2515.SPI_INSTRUCTION_WRITE });
            spiDevice.Write(spiMessage);
            cs_pin.Write(GpioPinValue.High);
            Task.Delay(-1).Wait(TIME_SLOW_DOWN_CODE);
        }

        public byte executeReadStateCommand(GpioPin cs_pin)
        {
            byte[] returnMessage = new byte[1];
            byte[] sendMessage = new byte[1];

            // Enable device
            cs_pin.Write(GpioPinValue.Low);

            // Write spi instruction read  
            sendMessage[0] = mcp2515.SPI_INSTRUCTION_READ_STATUS;
            spiDevice.Write(sendMessage);
            spiDevice.Read(returnMessage);

            // Disable device
            cs_pin.Write(GpioPinValue.High);
            Task.Delay(-1).Wait(TIME_SLOW_DOWN_CODE);

            return returnMessage[0];
        }











        private bool mcpExecutionIsActive = false;
        private bool stopAllOperations = false;

        public string[] sendBuffer = {"", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", "", "", "", "", "", "",
                                        "", "", "", "", ""};
        public bool[] bufferState = {false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false, false, false, false, false, false,
                                        false, false, false, false, false};

        public bool clientIsConnected
        {
            get
            {
                return mcpExecutionIsActive;
            }

            set
            {
                mcpExecutionIsActive = value;
            }
        }

        public bool StopAllOperations
        {
            get
            {
                return stopAllOperations;
            }

            set
            {
                stopAllOperations = value;
            }
        }

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
