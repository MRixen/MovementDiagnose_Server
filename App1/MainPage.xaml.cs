﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Devices.I2c;
using Windows.Devices.Spi;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using System.Diagnostics;
using Windows.System.Threading;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using Windows.Storage.Search;
using System.Threading.Tasks;
using CanTest;
using Windows.UI;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace App1
{
    public sealed partial class MainPage : Page
    {
        // TODO: Change sensor data to acquire only the constant g values
        private MCP2515 mcp2515;
        private Timer periodicTimer, mcpExecutorService;
        private const byte SPI_CHIP_SELECT_LINE = 0;
        private byte[] address_TXB0Dm = new byte[8]; // Transmit register 0/2 (3 at all) and byte 0/7 (8 at all)
        private int DELTA_T = 500, DELTA_T_MCP_EXECUTOR = 1;
        private byte MAX_TX_BUFFER_SIZE = 8;
        private int counter;
        private byte mcpExecutorSelector = 0x01;
        // TODO Change size
        private const byte MAX_MCP_EXECUTOR_SELECTOR = 0x01;

        // DATA FOR ADXL SENSOR
        private const byte ACCEL_REG_X = 0x32;              /* Address of the X Axis data register                  */
        private const byte ACCEL_REG_Y = 0x34;              /* Address of the Y Axis data register                  */
        private const byte ACCEL_REG_Z = 0x36;              /* Address of the Z Axis data register                  */
        private const byte ACCEL_I2C_ADDR = 0x53;           /* 7-bit I2C address of the ADXL345 with SDO pulled low */
        private const byte ACCEL_SPI_RW_BIT = 0x80;         /* Bit used in SPI transactions to indicate read/write  */
        private const byte ACCEL_SPI_MB_BIT = 0x40;         /* Bit used to indicate multi-byte SPI transactions     */

        struct Acceleration
        {
            public double X;
            public double Y;
            public double Z;
        };

        private GlobalDataSet globalDataSet;
        private bool execute_mcpExecutor = false;

        private ServerComm serverComm;
        private GlobalData globalData;
        private Diagnose diagnose;
        private int timestamp;
        private int sensorCounter;
        private int MAX_SENSOR_COUNT = 1;

        // TODO: Add timestamp to data

        public MainPage()
        {
            this.InitializeComponent();

            // Initilize data
            timestamp = 0;
            sensorCounter = 1;
            counter = 1;
            serverComm = new ServerComm();
            globalData = serverComm.getGlobalData();
            diagnose = new Diagnose(globalData);
            mcp2515 = new MCP2515();
            globalDataSet = new GlobalDataSet();

            // Inititalize raspberry pi and gpio
            init_raspberry_pi_gpio();
            init_raspberry_pi_spi();

            // Inititalize mcp2515
            Task task_mcp2515 = new Task(globalDataSet.init_mcp2515_task);
            task_mcp2515.Start();
            task_mcp2515.Wait();

            // Inititalize background tasks
            mcpExecutorService = new Timer(this.McpExecutorService, null, 0, DELTA_T_MCP_EXECUTOR); // Create timer to display the state of message transmission
            periodicTimer = new Timer(this.TimerCallback, null, 0, DELTA_T); // Create timer to display the state of message transmission

            // Inititalize server to connect to server
            Task<bool> serverStarted = serverComm.StartServer();
        }

        private void init_raspberry_pi_gpio()
        {
            Debug.Write("Start GPIO init \n");

            var gpioController = GpioController.GetDefault();

            if (gpioController == null)
            {
                return;
            }
            try
            {
                Debug.Write("Configure pins \n");
                // Configure pins
                globalDataSet.MCP2515_PIN_CS_SENDER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_19, GpioPinValue.High, GpioPinDriveMode.Output);
                globalDataSet.MCP2515_PIN_INTE_SENDER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_5, GpioPinDriveMode.Input);
                globalDataSet.MCP2515_PIN_CS_RECEIVER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_12, GpioPinValue.High, GpioPinDriveMode.Output);
                globalDataSet.MCP2515_PIN_INTE_RECEIVER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_13, GpioPinDriveMode.Input);
                globalDataSet.CS_PIN_SENSOR_ADXL1 = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_16, GpioPinValue.High, GpioPinDriveMode.Output);
                globalDataSet.ARDUINO_TEST_PIN = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_26, GpioPinValue.Low, GpioPinDriveMode.Output);

            }
            catch (FileLoadException ex)
            {
                Debug.Write("Exception in initGPIO: " + ex + "\n");
            }
        }

        private async void init_raspberry_pi_spi()
        {
            Debug.Write("Init SPI interface" + "\n");
            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 5000000;
                settings.Mode = SpiMode.Mode3;
                string aqs = SpiDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);
                globalDataSet.SPIDEVICE = await SpiDevice.FromIdAsync(dis[0].Id, settings);
                if (globalDataSet.SPIDEVICE == null)
                {
                    Debug.Write("SPI Controller is currently in use by another application. \n");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.Write("SPI Initialization failed. Exception: " + ex.Message + "\n");
                return;
            }

            // Send something to check that spi device is ready
            globalDataSet.Spi_not_initialized = true;
            while (globalDataSet.Spi_not_initialized)
            {
                bool error = false;
                try
                {
                    globalDataSet.SPIDEVICE.Write(new byte[] { 0xFF });
                }
                catch (Exception)
                {
                    error = true;
                }
                if (!error)
                {
                    globalDataSet.Spi_not_initialized = false;
                    Debug.Write("Spi device ready" + "\n");
                }
                else
                {
                    Debug.Write("Spi device not ready" + "\n");
                }
            }
        }

        private GpioPin configureGpio(GpioController gpioController, int gpioId, GpioPinDriveMode pinDriveMode)
        {
            GpioPin pinTemp;

            pinTemp = gpioController.OpenPin(gpioId);
            pinTemp.SetDriveMode(pinDriveMode);

            return pinTemp;
        }

        private GpioPin configureGpio(GpioController gpioController, int gpioId, GpioPinValue pinValue, GpioPinDriveMode pinDriveMode)
        {
            GpioPin pinTemp;

            pinTemp = gpioController.OpenPin(gpioId);
            pinTemp.Write(pinValue);
            pinTemp.SetDriveMode(pinDriveMode);

            return pinTemp;
        }

        private void TimerCallback(object state)
        {
            bool indicatorMode = false;

            if (globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.Low)
            {
                indicatorMode = true;
            }
            else
            {
                indicatorMode = false;
            }

            /* UI updates must be invoked on the UI thread */
            var task = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (indicatorMode)
                {
                    indicator.Background = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    indicator.Background = new SolidColorBrush(Colors.Green);
                }

            });
        }

        private void McpExecutorService(object state)
        {
            // Choose the specific mcp executor by setting the id that is mapped to the message identifier, 
            // identifier:  00000000 00000001
            // id:          1
            // identifier:  00000000 00000002
            // id:          2
            // etc.
            string xText, yText, zText;


            // TEST
            if (execute_mcpExecutor)
            {
                // TEST
                execute_mcpExecutor = false;

                if (mcpExecutorSelector > MAX_MCP_EXECUTOR_SELECTOR)
                {
                    mcpExecutorSelector = 0x01;
                }
                Debug.Write("Send identifier to mcp executor...");
                byte identifier = mcpExecutorSelector;

                // Configure tx buffer (msg length, identifier, etc.)
                globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_init_tx_buffer0(0x02, new byte[] { identifier, 0x00 });
                for (int i = 0; i < 2; i++)
                {
                    globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], identifier);
                    identifier = 0x00;
                }

                // TODO: Filter the message to receive only senssor value from current selected device

                Debug.Write("Wait for data from mcp executor...");
                while (globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.Low)
                {
                }
                Acceleration accel = ReadAccel();

                Debug.Write("accel.X: " + accel.X + "\n");
                Debug.Write("accel.Y: " + accel.Y + "\n");
                Debug.Write("accel.Z: " + accel.Z + "\n");

                // Show sensor data and set it to global data to send to clients
                xText = String.Format("x{0:F3}", accel.X);
                yText = String.Format("y{0:F3}", accel.Y);
                zText = String.Format("z{0:F3}", accel.Z);

                string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                Debug.Write("message: " + message);
                diagnose.sendToSocket(mcpExecutorSelector.ToString(), message);

                // Increase mcp executor selector to next device
                mcpExecutorSelector++;

                // Generate pseudo timestamp
                timestamp = timestamp + DELTA_T_MCP_EXECUTOR;
            }
        }

        private void Button_Click_execute_mcpExecutor(object sender, RoutedEventArgs e)
        {
            execute_mcpExecutor = true;
        }

        private void button_Click_test_arduino_mcp2515(object sender, RoutedEventArgs e)
        {
            globalDataSet.ARDUINO_TEST_PIN.Write(GpioPinValue.High);
        }

        private void button_Click_test_arduino_mcp2515_reset(object sender, RoutedEventArgs e)
        {
            globalDataSet.ARDUINO_TEST_PIN.Write(GpioPinValue.Low);
        }

        private Acceleration ReadAccel()
        {
            byte[] returnMessage = new byte[mcp2515.MessageSizeAdxl];
            const int ACCEL_RES = 1024;         /* The ADXL345 has 10 bit resolution giving 1024 unique values                     */
            const int ACCEL_DYN_RANGE_G = 8;    /* The ADXL345 had a total dynamic range of 8G, since we're configuring it to +-4G */
            const int UNITS_PER_G = ACCEL_RES / ACCEL_DYN_RANGE_G;  /* Ratio of raw int values to G units                          */

            globalDataSet.MCP2515_PIN_CS_RECEIVER.Write(GpioPinValue.Low);
            for (int i = 0; i < mcp2515.MessageSizeAdxl; i++)
            {
                returnMessage[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB0Dx[i]);
                Debug.Write("Read sensor data: " + returnMessage[i].ToString() + " from buffer 0 at byte" + mcp2515.REGISTER_RXB0Dx[i].ToString() + "\n");
            }
            globalDataSet.MCP2515_PIN_CS_RECEIVER.Write(GpioPinValue.High);

            /* In order to get the raw 16-bit data values, we need to concatenate two 8-bit bytes for each axis */
            short AccelerationRawX = BitConverter.ToInt16(returnMessage, 0);
            short AccelerationRawY = BitConverter.ToInt16(returnMessage, 2);
            short AccelerationRawZ = BitConverter.ToInt16(returnMessage, 4);

            /* Convert raw values to G's */
            Acceleration accel;
            accel.X = (double)AccelerationRawX / UNITS_PER_G;
            accel.Y = (double)AccelerationRawY / UNITS_PER_G;
            accel.Z = (double)AccelerationRawZ / UNITS_PER_G;

            return accel;
        }

        private void WriteAccel()
        {
            // Write sensor raw data to mcp2515 

            byte[] ReadBuf;
            byte[] RegAddrBuf;

            ReadBuf = new byte[6 + 1];      // Read buffer of size 6 bytes (2 bytes * 3 axes)
            RegAddrBuf = new byte[1 + 6];
            RegAddrBuf[0] = ACCEL_REG_X | ACCEL_SPI_RW_BIT | ACCEL_SPI_MB_BIT;
            globalDataSet.CS_PIN_SENSOR_ADXL1.Write(GpioPinValue.Low);
            globalDataSet.SPIDEVICE.TransferFullDuplex(RegAddrBuf, ReadBuf);
            globalDataSet.CS_PIN_SENSOR_ADXL1.Write(GpioPinValue.High);

            // Write sensor data to tx buffer
            // globalDataSet.MCP2515_PIN_CS_SENDER.Write(GpioPinValue.Low);
            for (int i = 0; i < mcp2515.MessageSizeAdxl; i++)
            {
                globalDataSet.LOGIC_MCP2515_SENDER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], ReadBuf[i]);
                Debug.Write("Write sensor data: " + ReadBuf[i].ToString() + " in buffer 0 at byte " + mcp2515.REGISTER_TXB0Dx[i].ToString() + "\n");
            }
            // globalDataSet.MCP2515_PIN_CS_SENDER.Write(GpioPinValue.High);
        }

        private void MainPage_Unloaded(object sender, object args)
        {
            globalDataSet.SPIDEVICE.Dispose();
        }
    }
}

