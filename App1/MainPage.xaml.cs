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
        private Timer stateTimer, errorTimer, testTimer;
        private const byte SPI_CHIP_SELECT_LINE = 0;
        private byte[] address_TXB0Dm = new byte[8]; // Transmit register 0/2 (3 at all) and byte 0/7 (8 at all)
        private int DELTA_T_TIMER_CALLBACK = 5, DELTA_T_MCP_EXECUTOR = 100, DELTA_T_ERROR_TIMER = 10;
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
        private const int ACCEL_RES = 1024;         /* The ADXL345 has 10 bit resolution giving 1024 unique values                     */
        private const int ACCEL_DYN_RANGE_G = 8;    /* The ADXL345 had a total dynamic range of 8G, since we're configuring it to +-4G */
        private const int UNITS_PER_G = ACCEL_RES / ACCEL_DYN_RANGE_G;  // Ratio of raw int values to G units          

        long[] timerArray = new long[10];

        struct Acceleration
        {
            public double X;
            public double Y;
            public double Z;
        };

        // Data to set the execution context for checking the message answer from remote mcpExecutor when sending stop / start sequence
        enum CheckExecution
        {
            stopExecution,
            startExecution
        };

        // FOR TESTING
        long timerValue;

        private GlobalDataSet globalDataSet;

        private ServerComm serverComm;
        private GlobalData globalData;
        private Diagnose diagnose;
        private int timestamp;
        private int sensorCounter;
        private int MAX_SENSOR_COUNT = 1;
        private bool sendRequest;
        private int MAX_DELAY_TIME = 100;
        private bool executionFinished;
        private int stepCounter;
        private Stopwatch timeStopper = new Stopwatch();
        private Stopwatch timer_programExecution = new Stopwatch();

        private Task task_mcpExecutorService;

        // DATA FOR ERROR HANDLING
        private const int MAX_ERROR_COUNTER_TRANSFER = 3;
        private int errorCounterTransfer;
        private bool firstStart;
        private bool startSequenceIsActive;
        private bool stopSequenceIsActive;
        private bool getProgramDuration;

        public MainPage()
        {
            this.InitializeComponent();

            // Initilize data
            executionFinished = false;
            errorCounterTransfer = 0;
            stepCounter = 0;
            sendRequest = true;
            firstStart = true;
            startSequenceIsActive = false;
            stopSequenceIsActive = false;
            timestamp = 0;
            sensorCounter = 1;
            counter = 1;
            mcpDeviceCounter = 0;
            globalDataSet = new GlobalDataSet(); // Get things like mcp2515, logic_Mcp2515_Sender, logic_Mcp2515_Receiver
            serverComm = new ServerComm(globalDataSet);
            diagnose = new Diagnose(globalDataSet);
            mcp2515 = globalDataSet.Mcp2515;

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
                globalDataSet.REQUEST_DATA = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_16, GpioPinValue.Low, GpioPinDriveMode.Output);
                globalDataSet.REQUEST_DATA_HANDSHAKE = configureGpio(gpioController, (int)RASPBERRYPI.GPIO.GPIO_26, GpioPinDriveMode.Input);

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
                settings.ClockFrequency = 5000000;
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

        private Acceleration ReadAccel(byte rxStateIst, byte rxStateSoll)
        {
            byte[] returnMessage = new byte[mcp2515.MessageSizeAdxl];

            if ((rxStateIst & rxStateSoll) == 1) for (int i = 0; i < mcp2515.MessageSizeAdxl; i++) returnMessage[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB0Dx[i]);
            else if ((rxStateIst & rxStateSoll) == 2) for (int i = 0; i < mcp2515.MessageSizeAdxl; i++) returnMessage[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB1Dx[i]);

            // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
            globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);


            if (getProgramDuration) timerArray[3] = timer_programExecution.ElapsedMilliseconds;

            /* In order to get the raw 16-bit data values, we need to concatenate two 8-bit bytes for each axis */
            short AccelerationRawX = BitConverter.ToInt16(returnMessage, 0);
            short AccelerationRawY = BitConverter.ToInt16(returnMessage, 2);
            short AccelerationRawZ = BitConverter.ToInt16(returnMessage, 4);

            /* Convert raw values to G's */
            Acceleration accel;
            accel.X = (double)AccelerationRawX / UNITS_PER_G;
            accel.Y = (double)AccelerationRawY / UNITS_PER_G;
            accel.Z = (double)AccelerationRawZ / UNITS_PER_G;

            if (getProgramDuration) timerArray[4] = timer_programExecution.ElapsedMilliseconds;

            return accel;
        }

        private void MainPage_Unloaded(object sender, object args)
        {
            globalDataSet.SPIDEVICE.Dispose();
        }

        public async void mcpExecutorService_task()
        {
            await Task.Run(() => execServ_v3_mcp2515());
        }

        private void execServ_v1_mcp2515()
        {
            string xText, yText, zText;

            // Start timer to measure the program execution
            timer_programExecution.Reset();
            timer_programExecution.Start();
            long timerValue = timer_programExecution.ElapsedMilliseconds;

            while (!globalDataSet.StopAllOperations)
            {
                if (globalDataSet.clientIsConnected)
                {
                    // Send request to mcp2515 devices to pre-save sensor data and check handshake content
                    if (sendRequestToMcp2515(255, 0, true))
                    {
                        // TODO Change timestamp to system clock
                        for (int i = 0; i <= MAX_MCP_DEVICE_COUNTER; i++)
                        {
                            if (sendRequestToMcp2515(i, 0, false))
                            {
                                if (globalDataSet.DebugMode) Debug.Write("Read sensor values from device " + i + "\n");

                                // Read the sensor data
                                Acceleration accel = ReadAccel(0x00, 0x00); // IMPORTANT: REMOVE BOTH 0x00 WHEN USE THIS FUNCTION

                                // Create string strings with sensor content
                                xText = String.Format("x{0:F3}", accel.X);
                                yText = String.Format("y{0:F3}", accel.Y);
                                zText = String.Format("z{0:F3}", accel.Z);

                                string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                                diagnose.sendToSocket(i.ToString(), message);

                                // Generate pseudo timestamp
                                timestamp = timestamp + DELTA_T_MCP_EXECUTOR;

                                // Stop timer to measure the program execution
                                Debug.WriteLine(timer_programExecution.ElapsedMilliseconds - timerValue);
                            }
                        }
                    }
                    // Add delay between execution
                    Task.Delay(-1).Wait(DELTA_T_MCP_EXECUTOR);
                }
                else timestamp = 0;
            }
            timer_programExecution.Stop();
        }

        private void execServ_v2_mcp2515()
        {
            string xText, yText, zText;
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;

            // Start timer to measure the program execution
            timer_programExecution.Reset();
            timer_programExecution.Start();
            long timerValue = timer_programExecution.ElapsedMilliseconds;

            while (!globalDataSet.StopAllOperations)
            {
                if (globalDataSet.clientIsConnected)
                {
                    globalDataSet.REQUEST_DATA.Write(GpioPinValue.High);

                    timeStopper.Reset();
                    timeStopper.Start();
                    while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High))
                    {
                    }
                    timeStopper.Stop();
                    if (timeStopper.ElapsedMilliseconds > globalDataSet.MAX_WAIT_TIME)
                    {
                        if (globalDataSet.DebugMode) Debug.Write("Abort waiting. Max. waiting time reached." + "\n");
                        globalDataSet.REQUEST_DATA.Write(GpioPinValue.Low);
                        errorCounterTransfer++;
                    }
                    else
                    {
                        if (globalDataSet.DebugMode) Debug.Write("Finished waiting, check which rx buffer." + "\n");
                        // Check in which rx buffer the message is send to
                        rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();

                        if (globalDataSet.DebugMode) Debug.WriteLine("Read sensor values from device ");

                        // Read the sensor data
                        Acceleration accel = ReadAccel(rxStateIst, rxStateSoll);

                        // Create string strings with sensor content
                        xText = String.Format("x{0:F3}", accel.X);
                        yText = String.Format("y{0:F3}", accel.Y);
                        zText = String.Format("z{0:F3}", accel.Z);

                        string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                        diagnose.sendToSocket("0", message);

                        // Generate pseudo timestamp
                        timestamp = timestamp + DELTA_T_MCP_EXECUTOR;
                        Debug.WriteLine(timestamp);

                        // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
                        globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);

                        // Finish handshake
                        globalDataSet.REQUEST_DATA.Write(GpioPinValue.Low);
                        while (globalDataSet.REQUEST_DATA_HANDSHAKE.Read() == GpioPinValue.High)
                        {
                        }
                        //Debug.WriteLine(timer_programExecution.ElapsedMilliseconds - timerValue);
                    }
                }

                else timestamp = 0;
            }
            timer_programExecution.Stop();
        }

        private void execServ_v3_mcp2515()
        {
            if (getProgramDuration)
            {
                timerValue = timer_programExecution.ElapsedMilliseconds;
                timer_programExecution.Reset();
                timer_programExecution.Start();
            }

            while (!globalDataSet.StopAllOperations)
            {
                if (globalDataSet.clientIsConnected)
                {
                    stopSequenceIsActive = true;
                    // Send start sequence
                    if (firstStart == true) executeStartSequence();

                    // Aquire sensor data
                    if (startSequenceIsActive) executeAqcuisition_v1();
                }
                else
                {
                    // Send stop sequence
                    if (stopSequenceIsActive)
                    {
                        Task.Delay(-1).Wait(200);
                        timestamp = 0;
                        firstStart = true;
                        executeStopSequence();
                    }
                }
            }
            //timer_programExecution.Stop();
        }

        private void execServ_v4_mcp2515()
        {
            while (!globalDataSet.StopAllOperations)
            {
                if (globalDataSet.clientIsConnected)
                {
                    stopSequenceIsActive = true;
                    // Send start sequence
                    if (firstStart == true) executeStartSequence();

                    // Aquire sensor data
                    if (startSequenceIsActive) executeAqcuisition_v2();
                }
                else
                {
                    // Send stop sequence
                    if (stopSequenceIsActive)
                    {
                        Task.Delay(-1).Wait(200);
                        timestamp = 0;
                        firstStart = true;
                        executeStopSequence();
                    }
                }
            }
            timer_programExecution.Stop();
        }

        private void executeStopSequence()
        {
            byte[] identifier = new byte[2];
            int requestIdLow = 0x80;
            int requestIdHigh = 0x80;
            identifier[0] = Convert.ToByte(requestIdLow);
            identifier[1] = Convert.ToByte(requestIdHigh);

            // Wait some time to for finishing sensor acquistion
            Task.Delay(-1).Wait(100);

            if (globalDataSet.DebugMode) Debug.Write("Init tx buffer 0" + "\n");
            globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_init_tx_buffer0(0x02, identifier);

            if (globalDataSet.DebugMode) Debug.Write("Send request with id " + requestIdLow + " and " + requestIdHigh + "\n");
            for (int i = 0; i < 2; i++) globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], identifier[i]);
            waitAndcheckAnswer(identifier, CheckExecution.stopExecution);
        }

        private void waitAndcheckAnswer(byte[] identifier, CheckExecution executionToCheck)
        {
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;
            byte[] retMsg = new byte[2];

            // Wait for handshake to check that stop sequence is received
            if (globalDataSet.DebugMode) Debug.Write("Wait for handshake / data from mcp device" + "\n");

            timeStopper.Reset();
            timeStopper.Start();
            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High) && (timeStopper.ElapsedMilliseconds <= globalDataSet.MAX_WAIT_TIME))
            {
            }
            timeStopper.Stop();

            if (timeStopper.ElapsedMilliseconds > globalDataSet.MAX_WAIT_TIME)
            {
                if (globalDataSet.DebugMode) Debug.Write("Abort waiting. Max. waiting time reached. Try again" + "\n");
                errorCounterTransfer++;
                Task.Delay(-1).Wait(200);
            }
            else
            {
                if (globalDataSet.DebugMode) Debug.WriteLine("Finished waiting.");
                errorCounterTransfer = 0;

                // Check in which rx buffer the message is send to
                rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();

                // Check handshake message
                if (globalDataSet.DebugMode) Debug.Write("Check handshake from mcp device" + "\n");

                // Its possible that message is in rx buffer 0 or 1. So we need to check this with rxState
                if ((rxStateIst & rxStateSoll) == 1) for (int i = 0; i < 2; i++) retMsg[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB0Dx[i]);
                else if ((rxStateIst & rxStateSoll) == 2) for (int i = 0; i < 2; i++) retMsg[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB1Dx[i]);

                // Check if message is correct (correct start or stop sequence)
                if ((retMsg[0] == identifier[0]) && (retMsg[1] == identifier[1]))
                {
                    // Only for debugging
                    if (globalDataSet.DebugMode)
                    {
                        if(retMsg[0] == 255) Debug.WriteLine("Received start sequence. Start program execution.");
                        else if (retMsg[0] == 128) Debug.WriteLine("Received stop sequence. Stop program execution.");
                    }

                    if (executionToCheck == CheckExecution.startExecution) {
                        firstStart = false;
                        startSequenceIsActive = true;
                    }
                    else if(executionToCheck == CheckExecution.stopExecution) stopSequenceIsActive = false;
                }
                else
                {
                    // Do nothing and try again
                    errorCounterTransfer++;
                }
            }
        }

        private void executeAqcuisition_v1()
        {
            if(getProgramDuration) timerArray[0] = timer_programExecution.ElapsedMilliseconds;

            string xText, yText, zText;
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;

            globalDataSet.REQUEST_DATA.Write(GpioPinValue.High);

            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High))
            {
            }
            if (getProgramDuration) timerArray[1] = timer_programExecution.ElapsedMilliseconds;

            if (globalDataSet.DebugMode) Debug.Write("Finished waiting, check which rx buffer." + "\n");
            // Check in which rx buffer the message is send to
            rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();

            if (getProgramDuration) timerArray[2] = timer_programExecution.ElapsedMilliseconds;

            if (globalDataSet.DebugMode) Debug.WriteLine("Read sensor values from device ");

            // Read the sensor data
            Acceleration accel = ReadAccel(rxStateIst, rxStateSoll);
           
            // Create string strings with sensor content
            xText = String.Format("x{0:F3}", accel.X);
            yText = String.Format("y{0:F3}", accel.Y);
            zText = String.Format("z{0:F3}", accel.Z);

            //string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
            string message = xText + "::" + yText + "::" + zText;
            diagnose.sendToSocket("0", message);

            // Generate pseudo timestamp
            timestamp = timestamp + DELTA_T_MCP_EXECUTOR;

            if (getProgramDuration) timerArray[5] = timer_programExecution.ElapsedMilliseconds;

            // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
            globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);

            if (getProgramDuration) timerArray[6] = timer_programExecution.ElapsedMilliseconds;

            // Finish handshake
            globalDataSet.REQUEST_DATA.Write(GpioPinValue.Low);
            while (globalDataSet.REQUEST_DATA_HANDSHAKE.Read() == GpioPinValue.High)
            {
            }

            if (getProgramDuration) timerArray[7] = timer_programExecution.ElapsedMilliseconds;

            if (getProgramDuration)
            {
                for (int i = 0; i < timerArray.Length; i++)
                {
                    Debug.WriteLine(timerArray[i]);
                }
            }

        }

        private void executeAqcuisition_v2()
        {
            string xText, yText, zText;
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;

            globalDataSet.REQUEST_DATA.Write(GpioPinValue.High);

            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High))
            {
            }
            if (globalDataSet.DebugMode) Debug.Write("Finished waiting, check which rx buffer." + "\n");
            // Check in which rx buffer the message is send to
            rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();

            if (globalDataSet.DebugMode) Debug.WriteLine("Read sensor values from device ");

            // Read the sensor data
            Acceleration accel = ReadAccel(rxStateIst, rxStateSoll);

            // Create string strings with sensor content
            xText = String.Format("x{0:F3}", accel.X);
            yText = String.Format("y{0:F3}", accel.Y);
            zText = String.Format("z{0:F3}", accel.Z);

            string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
            diagnose.sendToSocket("0", message);

            // Generate pseudo timestamp
            timestamp = timestamp + DELTA_T_MCP_EXECUTOR;

            // Reset interrupt for buffer 0 because message is read -> Reset all interrupts
            globalDataSet.mcp2515_execute_write_command(new byte[] { mcp2515.CONTROL_REGISTER_CANINTF, mcp2515.CONTROL_REGISTER_CANINTF_VALUE.RESET_ALL_IF }, globalDataSet.MCP2515_PIN_CS_RECEIVER);

            // Finish handshake
            globalDataSet.REQUEST_DATA.Write(GpioPinValue.Low);
        }

        private void executeStartSequence()
        {
            byte[] identifier = new byte[2];
            int requestIdLow = 0xFF;
            int requestIdHigh = 0xFF;
            identifier[0] = Convert.ToByte(requestIdLow);
            identifier[1] = Convert.ToByte(requestIdHigh);

            if (globalDataSet.DebugMode) Debug.Write("Init tx buffer 0" + "\n");
            globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_init_tx_buffer0(0x02, identifier);

            if (globalDataSet.DebugMode) Debug.Write("Send request with id " + requestIdLow + " and " + requestIdHigh + "\n");
            for (int i = 0; i < 2; i++) globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], identifier[i]);
            waitAndcheckAnswer(identifier, CheckExecution.startExecution);
        }

        private bool sendRequestToMcp2515(int requestIdLow, int requestIdHigh, bool checkMessage)
        {
            byte[] identifier = new byte[2];
            byte[] retMsg = new byte[2];
            byte rxStateIst = 0x00;
            byte rxStateSoll = 0x03;

            identifier[0] = Convert.ToByte(requestIdLow);
            identifier[1] = Convert.ToByte(requestIdHigh);

            if (globalDataSet.DebugMode) Debug.Write("Init tx buffer 0" + "\n");
            globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_init_tx_buffer0(0x02, identifier);

            if (globalDataSet.DebugMode) Debug.Write("Send request with id " + requestIdLow + " and " + requestIdHigh + "\n");
            for (int i = 0; i < 2; i++)
            {
                globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_load_tx_buffer0(mcp2515.REGISTER_TXB0Dx[i], identifier[i]);
            }

            // Wait for handshake from one of the sensors that the request is received
            if (globalDataSet.DebugMode) Debug.Write("Wait for handshake / data from mcp device" + "\n");

            timeStopper.Reset();
            timeStopper.Start();
            while ((globalDataSet.MCP2515_PIN_INTE_RECEIVER.Read() == GpioPinValue.High) && (timeStopper.ElapsedMilliseconds <= globalDataSet.MAX_WAIT_TIME))
            {
            }
            timeStopper.Stop();

            if (timeStopper.ElapsedMilliseconds > globalDataSet.MAX_WAIT_TIME)
            {
                if (globalDataSet.DebugMode) Debug.Write("Abort waiting. Max. waiting time reached." + "\n");
                errorCounterTransfer++;
                return false;
            }
            else
            {
                if (globalDataSet.DebugMode) Debug.Write("Finished waiting, check which rx buffer" + "\n");
                // Check in which rx buffer the message is send to
                rxStateIst = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_get_state_command();
            }

            // Check handshake message
            if (checkMessage)
            {
                if (globalDataSet.DebugMode) Debug.Write("Check handshake from mcp device" + "\n");
                // Its possible that message is in rx buffer 0 or 1. So we need to check this with rxState
                if ((rxStateIst & rxStateSoll) == 1) for (int i = 0; i < 2; i++) retMsg[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB0Dx[i]);
                else if ((rxStateIst & rxStateSoll) == 2) for (int i = 0; i < 2; i++) retMsg[i] = globalDataSet.LOGIC_MCP2515_RECEIVER.mcp2515_read_rx_buffer0(mcp2515.REGISTER_RXB1Dx[i]);

                // If the message is correct than return true
                if ((retMsg[0] == identifier[0]) && (retMsg[1] == identifier[1])) return true;
                else return false;
            }
            else return true;
        }
    }
}

