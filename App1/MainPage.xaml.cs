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
        // TODO: Change sensor data to acquire only the constant g values -> low pass filter
        private MCP2515 mcp2515;
        private Timer stateTimer, errorTimer;
        private const byte SPI_CHIP_SELECT_LINE = 0;
        private int DELTA_T_TIMER_CALLBACK = 5, DELTA_T_MCP_EXECUTOR = 100, DELTA_T_ERROR_TIMER = 10;


        // DATA FOR ADXL SENSOR
        private const byte ACCEL_REG_X = 0x32;              /* Address of the X Axis data register                  */
        private const byte ACCEL_REG_Y = 0x34;              /* Address of the Y Axis data register                  */
        private const byte ACCEL_REG_Z = 0x36;              /* Address of the Z Axis data register                  */
        private const byte ACCEL_I2C_ADDR = 0x53;           /* 7-bit I2C address of the ADXL345 with SDO pulled low */
        private const byte ACCEL_SPI_RW_BIT = 0x80;         /* Bit used in SPI transactions to indicate read/write  */
        private const byte ACCEL_SPI_MB_BIT = 0x40;         /* Bit used to indicate multi-byte SPI transactions     */
        private const int ACCEL_RES = 1024;         /* The ADXL345 has 10 bit resolution giving 1024 unique values                     */
        private const int ACCEL_DYN_RANGE_G = 8;    /* The ADXL345 had a total dynamic range of 8G, since we're configuring it to +-4G */
        private const int UNITS_PER_G = ACCEL_RES / ACCEL_DYN_RANGE_G;  // Ratio of raw int values to G units          

        struct McpExecutorDataFrame
        {
            public double X;
            public double Y;
            public double Z;
            public int ident;
            public float timeStamp;
        };

        // Data to set the execution context for checking the message answer from remote mcpExecutor when sending stop / start sequence
        enum CheckExecution
        {
            stopExecution,
            startExecution
        };

        private GlobalDataSet globalDataSet;
        private ServerComm serverComm;
        private Diagnose diagnose;
        private Task task_mcpExecutorService;

        private const byte MAX_MCP_DEVICE_COUNTER = 2; // max. 255
        private GpioPin[] mcpExecutor_request = new GpioPin[MAX_MCP_DEVICE_COUNTER];
        private GpioPin[] mcpExecutor_handshake = new GpioPin[MAX_MCP_DEVICE_COUNTER];
        private int mcpExecutorCounter;

        // DATA FOR ERROR HANDLING
        private const int MAX_ERROR_COUNTER_TRANSFER = 20;
        private int errorCounterTransfer;

        // DATA FOR DEBUGGING
        private Stopwatch timeStopper = new Stopwatch();
        private Stopwatch timer_programExecution = new Stopwatch();
        private Stopwatch timeStampWatch = new Stopwatch();
        private bool firstStart;
        private bool startSequenceIsActive;
        private bool stopSequenceIsActive;
        private bool getProgramDuration;
        private long timerValue;
        private long[] timerArray = new long[10];

        public MainPage()
        {
            this.InitializeComponent();

            // Initilize data
            errorCounterTransfer = 0;
            mcpExecutorCounter = 0;
            firstStart = true;
            startSequenceIsActive = false;
            stopSequenceIsActive = false;
            globalDataSet = new GlobalDataSet(); // Get things like mcp2515, logic_Mcp2515_Sender, logic_Mcp2515_Receiver
            serverComm = new ServerComm(globalDataSet);
            diagnose = new Diagnose(globalDataSet);
            mcp2515 = globalDataSet.Mcp2515;

            // USER CONFIGURATION
            globalDataSet.DebugMode = false;
            getProgramDuration = false;

            // Inititalize raspberry pi and gpio
            init_raspberry_pi_gpio();
            init_raspberry_pi_spi();

            // Inititalize mcp2515
            Task task_initMcp2515 = new Task(globalDataSet.init_mcp2515_task);
            task_initMcp2515.Start();
            task_initMcp2515.Wait();

            // Start executor service
            task_mcpExecutorService = new Task(mcpExecutorService_task);
            task_mcpExecutorService.Start();

            // Inititalize background tasks
            stateTimer = new Timer(this.StateTimer, null, 0, DELTA_T_TIMER_CALLBACK); // Create timer to display the state of message transmission
            errorTimer = new Timer(this.ErrorTimer, null, 0, DELTA_T_ERROR_TIMER); // Create timer to display the state of message transmission

            // Inititalize server
            Task<bool> serverStarted = serverComm.StartServer();
        }

        private void init_raspberry_pi_gpio()
        {
            if (globalDataSet.DebugMode) Debug.Write("Start GPIO init \n");

            var gpioController = GpioController.GetDefault();

            if (gpioController == null)
            {
                return;
            }
            try
            {
                if (globalDataSet.DebugMode) Debug.Write("Configure pins \n");
                // Configure pins
                globalDataSet.MCP2515_PIN_CS_SENDER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_19, GpioPinValue.High, GpioPinDriveMode.Output);
                globalDataSet.MCP2515_PIN_INTE_SENDER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_5, GpioPinDriveMode.Input);
                globalDataSet.MCP2515_PIN_CS_RECEIVER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_12, GpioPinValue.High, GpioPinDriveMode.Output);
                globalDataSet.MCP2515_PIN_INTE_RECEIVER = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_13, GpioPinDriveMode.Input);
                globalDataSet.REQUEST_DATA_EXECUTOR_0 = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_17, GpioPinValue.Low, GpioPinDriveMode.Output);
                globalDataSet.REQUEST_DATA_HANDSHAKE_EXECUTOR_0 = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_4, GpioPinDriveMode.Input);
                globalDataSet.REQUEST_DATA_EXECUTOR_1 = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_22, GpioPinValue.Low, GpioPinDriveMode.Output);
                globalDataSet.REQUEST_DATA_HANDSHAKE_EXECUTOR_1 = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_27, GpioPinDriveMode.Input);

                // Set IOs for the mcp executors
                mcpExecutor_request[0] = globalDataSet.REQUEST_DATA_EXECUTOR_0;
                mcpExecutor_request[1] = globalDataSet.REQUEST_DATA_EXECUTOR_1;

                mcpExecutor_handshake[0] = globalDataSet.REQUEST_DATA_HANDSHAKE_EXECUTOR_0;
                mcpExecutor_handshake[1] = globalDataSet.REQUEST_DATA_HANDSHAKE_EXECUTOR_1;
            }
            catch (FileLoadException ex)
            {
                if (globalDataSet.DebugMode) Debug.Write("Exception in initGPIO: " + ex + "\n");
            }
        }

        private async void init_raspberry_pi_spi()
        {
            if (globalDataSet.DebugMode) Debug.Write("Init SPI interface" + "\n");
            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 10000000;
                settings.Mode = SpiMode.Mode3;
                string aqs = SpiDevice.GetDeviceSelector();
                var dis = await DeviceInformation.FindAllAsync(aqs);
                globalDataSet.SPIDEVICE = await SpiDevice.FromIdAsync(dis[0].Id, settings);
                if (globalDataSet.SPIDEVICE == null)
                {
                    if (globalDataSet.DebugMode) Debug.Write("SPI Controller is currently in use by another application. \n");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (globalDataSet.DebugMode) Debug.Write("SPI Initialization failed. Exception: " + ex.Message + "\n");
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
                    if (globalDataSet.DebugMode) Debug.Write("Spi device ready" + "\n");
                }
                else
                {
                    if (globalDataSet.DebugMode) Debug.Write("Spi device not ready" + "\n");
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

        private void StateTimer(object state)
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
                if (globalDataSet.DebugMode) Debug.Write("ERROR TRANSFER - STOP ALL OPERATIONS" + "\n");
                globalDataSet.StopAllOperations = true;
                errorCounterTransfer = 0;
            }
        }

        private byte[] generateIdentifier(int identifierTemp)
        {
            byte[] identifier = BitConverter.GetBytes(identifierTemp);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(identifier);
                if (globalDataSet.DebugMode) Debug.Write("IsLittleEndian \n");
            }

            if (globalDataSet.DebugMode) Debug.Write("Convert " + identifierTemp + " to " + identifier[0] + " and " + identifier[1] + "\n");
            if (globalDataSet.DebugMode) Debug.Write("Convert " + identifierTemp + " to " + identifier + "\n");

            // Return max 2 bytes
            return identifier;
        }

        private McpExecutorDataFrame ReadAccel(byte rxStateIst, byte rxStateSoll)
        {
            byte[] returnMessage = new byte[mcp2515.MessageSizeAdxl];
            byte[] returnMessageTemp = new byte[1];

            if ((rxStateIst & rxStateSoll) == 1)
            {
                byte[] spiMessage = new byte[1];

                //for (int i = 0; i < mcp2515.MessageSizeAdxl; i++) returnMessage[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer(mcp2515.REGISTER_RXB0Dx[i]);
                returnMessage = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer_v3(mcp2515.SPI_INSTRUCTION_READ_RX_BUFFER0);
                // We need to check sidl only because we have not so much devices.
                //Debug.WriteLine("REGISTER_RXB0SIDL: " + globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer(mcp2515.REGISTER_RXB0SIDL));
            }
            else if ((rxStateIst & rxStateSoll) == 2)
            {
                //for (int i = 0; i < mcp2515.MessageSizeAdxl; i++) returnMessage[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer(mcp2515.REGISTER_RXB1Dx[i]);
                returnMessage = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer_v3(mcp2515.SPI_INSTRUCTION_READ_RX_BUFFER1);
                //Debug.WriteLine("REGISTER_RXB1SIDL: " + globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_buffer(mcp2515.REGISTER_RXB1SIDL));
            }

            if (getProgramDuration) timerArray[3] = timer_programExecution.ElapsedMilliseconds;

            // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
            //globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);

            /* In order to get the raw 16-bit data values, we need to concatenate two 8-bit bytes for each axis */
            short AccelerationRawX = BitConverter.ToInt16(returnMessage, 0);
            short AccelerationRawY = BitConverter.ToInt16(returnMessage, 2);
            short AccelerationRawZ = BitConverter.ToInt16(returnMessage, 4);
            int identifier = Convert.ToInt32(returnMessage[6]);
            long timeStamp = timeStampWatch.ElapsedMilliseconds;

            /* Convert raw values to G's */
            McpExecutorDataFrame mcpExecutorDataFrame;
            mcpExecutorDataFrame.X = (double)AccelerationRawX / UNITS_PER_G;
            mcpExecutorDataFrame.Y = (double)AccelerationRawY / UNITS_PER_G;
            mcpExecutorDataFrame.Z = (double)AccelerationRawZ / UNITS_PER_G;
            mcpExecutorDataFrame.ident = identifier;
            mcpExecutorDataFrame.timeStamp = (float)timeStamp / 1000;

            if (getProgramDuration) timerArray[4] = timer_programExecution.ElapsedMilliseconds;

            return mcpExecutorDataFrame;
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
            while (!globalDataSet.StopAllOperations)
            {
                if (globalDataSet.clientIsConnected)
                {
                    if (getProgramDuration & !timer_programExecution.IsRunning)
                    {
                        timer_programExecution.Reset();
                        timer_programExecution.Start();
                    }

                    if (!timeStampWatch.IsRunning)
                    {
                        timeStampWatch.Reset();
                        timeStampWatch.Start();
                    }

                    executeAqcuisition();
                }
                else
                {

                    //Task.Delay(-1).Wait(200);
                    if (timer_programExecution.IsRunning) timer_programExecution.Stop();
                    if (timer_programExecution.IsRunning) timeStampWatch.Stop();
                }
            }
        }

        private void executeAqcuisition()
        {
            if (getProgramDuration) timerArray[0] = timer_programExecution.ElapsedMilliseconds;

            string xText, yText, zText, sensorId, timeStamp;
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;

            // Wait until a message is received in buffer 0 or 1
            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High))
            {
            }
            if (getProgramDuration) timerArray[1] = timer_programExecution.ElapsedMilliseconds;

            if (globalDataSet.DebugMode) Debug.Write("Finished waiting, check which rx buffer." + "\n");
            // Check in which rx buffer the message is
            rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();

            if (globalDataSet.DebugMode) Debug.WriteLine("Read sensor values from device ");

            if (getProgramDuration) timerArray[2] = timer_programExecution.ElapsedMilliseconds;

            // Read the sensor data
            McpExecutorDataFrame mcpExecutorDataFrame = ReadAccel(rxStateIst, rxStateSoll);

            // Create string with sensor content
            xText = String.Format("x{0:F3}", mcpExecutorDataFrame.X);
            yText = String.Format("y{0:F3}", mcpExecutorDataFrame.Y);
            zText = String.Format("z{0:F3}", mcpExecutorDataFrame.Z);
            sensorId = mcpExecutorDataFrame.ident.ToString();
            timeStamp = mcpExecutorDataFrame.timeStamp.ToString();

            //Debug.WriteLine("sensorId: " + sensorId);

            string message = xText + "::" + yText + "::" + zText + "::" + timeStamp;
            diagnose.sendToSocket(sensorId, message);

            if (getProgramDuration) timerArray[5] = timer_programExecution.ElapsedMilliseconds;

            // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
            //globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);

            if (getProgramDuration) timerArray[6] = timer_programExecution.ElapsedMilliseconds;
            if (getProgramDuration) for (int i = 0; i < timerArray.Length; i++) Debug.WriteLine(timerArray[i]);
        }
    }
}

