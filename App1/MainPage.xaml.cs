using System;
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
        // TODO: Add something to shutdown everything (with stopAllOperations flag)
        // TODO: Change sensor data to acquire only the constant g values
        private MCP2515 mcp2515;
        private Timer periodicTimer, mcpExecutorTimer, errorTimer;
        private const byte SPI_CHIP_SELECT_LINE = 0;
        private byte[] address_TXB0Dm = new byte[8]; // Transmit register 0/2 (3 at all) and byte 0/7 (8 at all)
        private int DELTA_T_TIMER_CALLBACK = 500, DELTA_T_MCP_EXECUTOR = 10, DELTA_T_ERROR_TIMER = 10;
        private byte MAX_TX_BUFFER_SIZE = 8;
        private int counter;
        // TODO Change size
        private const byte MAX_MCP_DEVICE_COUNTER = 0; // max. 255
        private int mcpDeviceCounter;
        private byte[] mcpDevice = new byte[MAX_MCP_DEVICE_COUNTER];

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

        private ServerComm serverComm;
        private GlobalData globalData;
        private Diagnose diagnose;
        private int timestamp;
        private int sensorCounter;
        private int MAX_SENSOR_COUNT = 1;
        private bool sendRequest;
        private int MAX_DELAY_TIME = 100;
        private int MAX_WAIT_TIME = 800;
        private bool executionFinished;
        private int stepCounter;
        private Stopwatch timeStopper = new Stopwatch();

        private Task task_mcpExecutorService;

        // DATA FOR ERROR HANDLING
        private const int MAX_ERROR_COUNTER_TRANSFER = 5;
        private int errorCounterTransfer;

        // TODO: Add timestamp to data

        public MainPage()
        {
            this.InitializeComponent();

            // Initilize data
            executionFinished = false;
            errorCounterTransfer = 0;
            stepCounter = 0;
            sendRequest = true;
            timestamp = 0;
            sensorCounter = 1;
            counter = 1;
            mcpDeviceCounter = 0;
            serverComm = new ServerComm();
            globalData = serverComm.getGlobalData();
            diagnose = new Diagnose(globalData);
            mcp2515 = new MCP2515();
            globalDataSet = new GlobalDataSet();

            // Inititalize raspberry pi and gpio
            init_raspberry_pi_gpio();
            init_raspberry_pi_spi();

            // Inititalize mcp2515
            Task task_initMcp2515 = new Task(globalDataSet.init_mcp2515_task);
            task_initMcp2515.Start();
            task_initMcp2515.Wait();


            task_mcpExecutorService = new Task(mcpExecutorService_task);
            task_mcpExecutorService.Start();

            // Inititalize background tasks
            mcpExecutorTimer = new Timer(this.McpExecutorTimer, null, 0, DELTA_T_MCP_EXECUTOR); // Create timer to display the state of message transmission
            periodicTimer = new Timer(this.TimerCallback, null, 0, DELTA_T_TIMER_CALLBACK); // Create timer to display the state of message transmission
            errorTimer = new Timer(this.ErrorTimer, null, 0, DELTA_T_ERROR_TIMER); // Create timer to display the state of message transmission


            // Inititalize server
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

        private void ErrorTimer(object state)
        {
            // TODO Show red blinking warning message on screen
            if (errorCounterTransfer >= MAX_ERROR_COUNTER_TRANSFER)
            {
                Debug.Write("ERROR TRANSFER - STOP ALL OPERATIONS" +  "\n");
                globalData.StopAllOperations = true;
                errorCounterTransfer = 0;
            }
        }

        private void McpExecutorTimer(object state)
        {
            if (globalData.clientIsConnected)
            {
                //if (!(task_mcpExecutorService.Status == TaskStatus.Running) && ((task_mcpExecutorService.Status == TaskStatus.Created) || (task_mcpExecutorService.Status == TaskStatus.RanToCompletion)) )
                //{
                //    task_mcpExecutorService.Start();
                //}
            }
        }

        private byte[] generateIdentifier(int identifierTemp)
        {
            byte[] identifier = BitConverter.GetBytes(identifierTemp);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(identifier);
                Debug.Write("IsLittleEndian \n");
            }

            Debug.Write("Convert " + identifierTemp + " to " + identifier[0] + " and " + identifier[1] + "\n");
            Debug.Write("Convert " + identifierTemp + " to " + identifier + "\n");

            // Return max 2 bytes
            return identifier;
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
                //Debug.Write("Read sensor data: " + returnMessage[i].ToString() + " from buffer 0 at byte" + mcp2515.REGISTER_RXB0Dx[i].ToString() + "\n");
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

        public async void mcpExecutorService_task()
        {
            await Task.Run(() => execServ_mcp2515());
        }

        private void execServ_mcp2515()
        {
            string xText, yText, zText;
            while (!globalData.StopAllOperations)
            {
                if (globalData.clientIsConnected)
                {
                    // Send request to mcp2515 devices to pre-save sensor data and check handshake content
                    if (sendRequestToMcp2515(255, 0, true))
                    {
                        // TODO Change timestamp to system clock
                        for (int i = 0; i <= MAX_MCP_DEVICE_COUNTER; i++)
                        {
                            if (sendRequestToMcp2515(i, 0, false))
                            {
                                Debug.Write("Read sensor values from device " + i + "\n");

                                //// Read the sensor data
                                //Acceleration accel = ReadAccel();

                                ////Debug.Write("accel.X: " + accel.X + "\n");
                                ////Debug.Write("accel.Y: " + accel.Y + "\n");
                                ////Debug.Write("accel.Z: " + accel.Z + "\n");

                                //// Create string strings with sensor content
                                //xText = String.Format("x{0:F3}", accel.X);
                                //yText = String.Format("y{0:F3}", accel.Y);
                                //zText = String.Format("z{0:F3}", accel.Z);

                                //string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                                ////Debug.Write("message: " + message + "\n");
                                //diagnose.sendToSocket(i.ToString(), message);

                                //// Generate pseudo timestamp
                                //timestamp = timestamp + DELTA_T_MCP_EXECUTOR;
                            }
                        }
                    }
                    // Add delay between execution
                    Task.Delay(-1).Wait(DELTA_T_MCP_EXECUTOR);
                }
            }
        }

        private bool sendRequestToMcp2515(int requestIdLow, int requestIdHigh, bool checkMessage)
        {
            byte[] identifier = new byte[2];
            byte[] retMsg = new byte[2];

            identifier[0] = Convert.ToByte(requestIdLow);
            identifier[1] = Convert.ToByte(requestIdHigh);

            Debug.Write("Init tx buffer 0" + "\n");
            globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_init_tx_buffer0(0x02, identifier);

            Debug.Write("Send request with id " + requestIdLow + " and " + requestIdHigh + "\n");
            for (int i = 0; i < 2; i++)
            {
                globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], identifier[i]);
            }

            // Wait for handshake from one of the sensors that the request is received
            Debug.Write("Wait for handshake / data from mcp device" + "\n");

            timeStopper.Reset();
            timeStopper.Start();
            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High) && (timeStopper.ElapsedMilliseconds <= MAX_WAIT_TIME))
            {
            }
            timeStopper.Stop();

            if (timeStopper.ElapsedMilliseconds > MAX_WAIT_TIME)
            {
                Debug.Write("Abort waiting. Max. waiting time reached." + "\n");
                errorCounterTransfer++;
            }
            else Debug.Write("Finished waiting" + "\n");

            // Check handshake message
            if (checkMessage)
            {
                Debug.Write("Check handshake from mcp device" + "\n");
                for (int i = 0; i < 2; i++) retMsg[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB0Dx[i]);
                if ((retMsg[0] == identifier[0]) & (retMsg[1] == identifier[1])) return true;
                else return false;
            }
            else return true;
        }
    }
}

