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

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace App1
{
    struct Acceleration
    {
        public double X;
        public double Y;
        public double Z;
    };

    public sealed partial class MainPage : Page
    {
        // TODO: Change sensor data to acquire only the constant g values

        private const byte ACCEL_REG_POWER_CONTROL = 0x2D;  /* Address of the Power Control register                */
        private const byte ACCEL_REG_DATA_FORMAT = 0x31;    /* Address of the Data Format register                  */
        private const byte ACCEL_REG_X = 0x32;              /* Address of the X Axis data register                  */
        private const byte ACCEL_REG_Y = 0x34;              /* Address of the Y Axis data register                  */
        private const byte ACCEL_REG_Z = 0x36;              /* Address of the Z Axis data register                  */
        private const byte ACCEL_I2C_ADDR = 0x53;           /* 7-bit I2C address of the ADXL345 with SDO pulled low */
        private const byte SPI_CHIP_SELECT_LINE = 0;        /* Chip select line to use                              */
        private const byte ACCEL_SPI_RW_BIT = 0x80;         /* Bit used in SPI transactions to indicate read/write  */
        private const byte ACCEL_SPI_MB_BIT = 0x40;         /* Bit used to indicate multi-byte SPI transactions     */

        private SpiDevice SPIAccel;
        private Timer periodicTimer, saveTimer;

        private static GpioPin pin5, pin6, pin13, pin19;
        private int sensorSelector = -1;
        private int MAX_SENSOR_COUNT = 4;
        private int sensorCounter = 3;
        private GpioPin[] cs_pin = { pin5, pin6, pin13, pin19 };
        private GpioPinValue[] sensorOFF = { GpioPinValue.High, GpioPinValue.High, GpioPinValue.High, GpioPinValue.High };
        private int[] pinNumbers = { 5, 6, 13, 19 };

        private int counter = 0;
        private ServerComm serverComm;
        private GlobalData globalData;
        private Diagnose diagnose;
        private int timestamp;

        // Measurement intervall
        private int DELTA_T = 10;

        // TODO: Add timestamp to data

        public MainPage()
        {
            this.InitializeComponent();

            timestamp = 0;

            serverComm = new ServerComm();
            globalData = serverComm.getGlobalData();
            diagnose = new Diagnose(globalData);

            Task<bool> serverStarted = serverComm.StartServer();

            Unloaded += MainPage_Unloaded;
            initGPIO();
            InitSPIAccel();
        }

        private void initGPIO()
        {
            ////Debug.Write("Start GPIO init \n");

            var gpio = GpioController.GetDefault();

            if (gpio == null)
            {
                return;
            }
            try
            {
                ////Debug.Write("Set pin values in Init \n");

                for (int i = 0; i <= cs_pin.Length-1; i++)
                {
                    cs_pin[i] = gpio.OpenPin(pinNumbers[i]);
                    cs_pin[i].Write(GpioPinValue.High);
                    cs_pin[i].SetDriveMode(GpioPinDriveMode.Output);
                }
            }
            catch (FileLoadException ex)
            {
                ////Debug.Write("Exception in x: " + ex + "\n");
            }

        }

        private async void InitSPIAccel()
        {
            //Debug.Write("InitSPIAccel");
            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 5000000;                              /* 5MHz is the rated speed of the ADXL345 accelerometer                     */
                settings.Mode = SpiMode.Mode3;                                  /* The accelerometer expects an idle-high clock polarity, we use Mode3    
                                                                                 * to set the clock polarity and phase to: CPOL = 1, CPHA = 1         
                                                                                 */
                string aqs = SpiDevice.GetDeviceSelector();                     /* Get a selector string that will return all SPI controllers on the system */
                var dis = await DeviceInformation.FindAllAsync(aqs);            /* Find the SPI bus controller devices with our selector string             */
                SPIAccel = await SpiDevice.FromIdAsync(dis[0].Id, settings);    /* Create an SpiDevice with our bus controller and SPI settings             */
                if (SPIAccel == null)
                {
                    //Debug.Write("SPI Controller is currently in use by another application.");
                    return;
                }
            }
            catch (Exception ex)
            {
                //Debug.Write("SPI Initialization failed. Exception: " + ex.Message);
                return;
            }

            /* 
             * Initialize the accelerometer:
             *
             * For this device, we create 2-byte write buffers:
             * The first byte is the register address we want to write to.
             * The second byte is the contents that we want to write to the register. 
             */
            byte[] WriteBuf_DataFormat = new byte[] { ACCEL_REG_DATA_FORMAT, 0x01 };        /* 0x01 sets range to +- 4Gs                         */
            byte[] WriteBuf_PowerControl = new byte[] { ACCEL_REG_POWER_CONTROL, 0x08 };    /* 0x08 puts the accelerometer into measurement mode */

            /* Write the register settings */
           // try
           // {
                //Debug.Write("Write the register\n");
                for (int i = 3; i <= MAX_SENSOR_COUNT - 1; i++)
                {
                    selectSensor(i);
                    SPIAccel.Write(WriteBuf_DataFormat);
                    SPIAccel.Write(WriteBuf_PowerControl);
                    disableAllSensor();
                    //Debug.Write("Write the register 2\n");
                }
            //}
            /* If the write fails display the error and stop running */
           // catch (Exception ex)
           // {
           //     Text_Status.Text = "Failed to communicate with device: " + ex.Message;
           //     return;
            //}
            //Debug.Write("Create periodicTimer\n");
            periodicTimer = new Timer(this.TimerCallback, null, 0, DELTA_T);
        }

        private void selectSensor(int selector)
        {
            // Set all sensor off
            for (int i = 0; i <= cs_pin.Length - 1; i++) cs_pin[i].Write(sensorOFF[i]);
            // Enable specific sensor
            cs_pin[selector].Write(GpioPinValue.Low);
        }

        private void disableAllSensor()
        {
            for (int i = 0; i <= cs_pin.Length - 1; i++) cs_pin[i].Write(sensorOFF[i]);
        }

        private void MainPage_Unloaded(object sender, object args)
        {
            SPIAccel.Dispose();
        }

        private void TimerCallback(object state)
        {
            string xText, yText, zText;
            string statusText;
            string sendString = " ";

            //Debug.Write("TimerCallback\n");

            if (sensorCounter > MAX_SENSOR_COUNT-1) sensorCounter = 3;

            /* Read and format accelerometer data */
            try
            {
                selectSensor(sensorCounter);
                Acceleration accel = ReadAccel();
                disableAllSensor();

                xText = String.Format("x{0:F3}", accel.X);
                yText = String.Format("y{0:F3}", accel.Y);
                zText = String.Format("z{0:F3}", accel.Z);
                //Debug.Write("Status acquisition: Running");

                //string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                string message = xText + "::" + yText + "::" + zText + "::" + timestamp;
                diagnose.sendToSocket(sensorCounter.ToString(), message);
            }
            catch (Exception ex)
            {
                xText = "X Axis: Error";
                yText = "Y Axis: Error";
                zText = "Z Axis: Error";
                //Debug.Write("Failed to read from Accelerometer no: " + sensorCounter + " exception: " + ex.Message);
            }

            /* UI updates must be invoked on the UI thread */
            var task = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                switch (sensorCounter)
                {
                    case 0: 
                        Text_X_Axis_0.Text = xText;
                        Text_Y_Axis_0.Text = yText;
                        Text_Z_Axis_0.Text = zText;
                        break;
                    case 1:
                        Text_X_Axis_1.Text = xText;
                        Text_Y_Axis_1.Text = yText;
                        Text_Z_Axis_1.Text = zText;
                        break;
                    case 2:
                        Text_X_Axis_2.Text = xText;
                        Text_Y_Axis_2.Text = yText;
                        Text_Z_Axis_2.Text = zText;
                        break;
                    case 3:
                        Text_X_Axis_3.Text = xText;
                        Text_Y_Axis_3.Text = yText;
                        Text_Z_Axis_3.Text = zText;
                        break;
                    default:
                        //Debug.Write("Sensor no: " + sensorCounter + " not exist!");
                        break;
                }
            });

            // Generate pseudo timestamp
            timestamp = timestamp + DELTA_T;
            sensorCounter += 1;
        }

        private Acceleration ReadAccel()
        {
            const int ACCEL_RES = 1024;         /* The ADXL345 has 10 bit resolution giving 1024 unique values                     */
            const int ACCEL_DYN_RANGE_G = 8;    /* The ADXL345 had a total dynamic range of 8G, since we're configuring it to +-4G */
            const int UNITS_PER_G = ACCEL_RES / ACCEL_DYN_RANGE_G;  /* Ratio of raw int values to G units                          */

            byte[] ReadBuf;
            byte[] RegAddrBuf;

            /* 
             * Read from the accelerometer 
             * We first write the address of the X-Axis register, then read all 3 axes into ReadBuf
             */
            ReadBuf = new byte[6 + 1];      /* Read buffer of size 6 bytes (2 bytes * 3 axes) + 1 byte padding */
            RegAddrBuf = new byte[1 + 6];   /* Register address buffer of size 1 byte + 6 bytes padding        */
                                            /* Register address we want to read from with read and multi-byte bit set                          */
            RegAddrBuf[0] = ACCEL_REG_X | ACCEL_SPI_RW_BIT | ACCEL_SPI_MB_BIT;
            SPIAccel.TransferFullDuplex(RegAddrBuf, ReadBuf);
            Array.Copy(ReadBuf, 1, ReadBuf, 0, 6);  /* Discard first dummy byte from read                      */

            /* Check the endianness of the system and flip the bytes if necessary */
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(ReadBuf, 0, 2);
                Array.Reverse(ReadBuf, 2, 2);
                Array.Reverse(ReadBuf, 4, 2);
            }

            /* In order to get the raw 16-bit data values, we need to concatenate two 8-bit bytes for each axis */
            short AccelerationRawX = BitConverter.ToInt16(ReadBuf, 0);
            short AccelerationRawY = BitConverter.ToInt16(ReadBuf, 2);
            short AccelerationRawZ = BitConverter.ToInt16(ReadBuf, 4);

            /* Convert raw values to G's */
            Acceleration accel;
            accel.X = (double)AccelerationRawX / UNITS_PER_G;
            accel.Y = (double)AccelerationRawY / UNITS_PER_G;
            accel.Z = (double)AccelerationRawZ / UNITS_PER_G;

            return accel;
        }

    }
}

