using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiDeviceConnect
{
    public partial class Form1 : Form
    {
        #region Fields
        private bool BLE_Connected              = false;
        private bool Notifying                  = false;
        private bool Saving                     = false;
        int ui_update_count                     = 0;
        private int service_discovery_timeout   = 0;
        List<BLEDevice> ConnectedDevices = new List<BLEDevice>();
        #endregion

        #region Event Handlers
        private async void ConnectionStatusUpdate(object sender, BLEComms.ConnectionStatus status)
        {
            //Obtain the NU_BLE sender:
            BLEDevice device = (BLEDevice)sender;
            switch (status)
            {
                case BLEComms.ConnectionStatus.Connected:
                    BLE_Connected = true;
                    switch (device.Id)
                    {
                        case "Device_1":
                            this.Invoke((MethodInvoker)(() => label1.BackColor = Color.Green));
                            break;
                        case "Device_2":
                            this.Invoke((MethodInvoker)(() => label2.BackColor = Color.Green));
                            break;
                        case "Device_3":
                            this.Invoke((MethodInvoker)(() => label3.BackColor = Color.Green));
                            break;
                    }
                    this.Invoke((MethodInvoker)(() => connectButton.Text = "Disconnect"));
                    StatusUpdated(this, BLEComms.MsgType.Success, device.Id + " connected!", null);
                    await Finish_BLE_Connection(device);
                    break;
                case BLEComms.ConnectionStatus.Disconnected:
                    switch (device.Id)
                    {
                        case "Device_1":
                            this.Invoke((MethodInvoker)(() => label1.BackColor = Color.Red));
                            break;
                        case "Device_2":
                            this.Invoke((MethodInvoker)(() => label2.BackColor = Color.Red));
                            break;
                        case "Device_3":
                            this.Invoke((MethodInvoker)(() => label3.BackColor = Color.Red));
                            break;
                    }
                    device.ConnectionStatus -= this.ConnectionStatusUpdate;
                    ConnectedDevices.Remove(device);
                    if (ConnectedDevices.Count == 0)
                    {
                        this.Invoke((MethodInvoker)(() => connectButton.Text = "Connect"));
                        BLE_Connected = false;
                    }
                    StatusUpdated(this, BLEComms.MsgType.Success, device.Id + " disconnected!", null);
                    break;
                case BLEComms.ConnectionStatus.Timeout:
                    device.ConnectionStatus -= this.ConnectionStatusUpdate;
                    //One of the devices has timed out, reset all connections:
                    foreach (var c in ConnectedDevices.ToList())
                    {
                        c.Disconnect();
                    }
                    break;
                case BLEComms.ConnectionStatus.Paired:
                    break;
                case BLEComms.ConnectionStatus.Unpaired:
                    break;
            }
        }

        private void NotificationReceived(object sender, NotificationEventArgs args)
        {
            //Obtain the NU_BLE sender:
            BLEDevice bleDevice = (BLEDevice)sender;

            ui_update_count++;
            if (ui_update_count == 10)
            {
                //Generate a message string:
                string message = "Data: ";
                for (int i = 0; i < args.data.Length - 1; i++)
                {
                    message = message + (char)args.data[i];
                }
                //Print the message on the UI:
                if (Equals(bleDevice.Id, "Device_1"))
                {
                    //Add data to the readonly text box:
                    this.Invoke((MethodInvoker)(() => richTextBox1.Text = message));
                }
                if (Equals(bleDevice.Id, "Device_2"))
                {
                    //Add data to the readonly text box:
                    this.Invoke((MethodInvoker)(() => richTextBox2.Text = message));
                }
                if (Equals(bleDevice.Id, "Device_3"))
                {
                    //Add data to the readonly text box:
                    this.Invoke((MethodInvoker)(() => richTextBox3.Text = message));
                }
                ui_update_count = 0;
            }
        }
        #endregion

        public Form1()
        {
            InitializeComponent();
        }

        #region Buttons
        private void connectButton_Click(object sender, EventArgs e)
        {
            if (!BLE_Connected)
            {
                ConnectToDevices();
            }
            else
            {
                foreach (var n in ConnectedDevices.ToList()) //"ToList()" added to avoid the exception "Collection was modified; enumeration operation may not execute" that is triggered by modifying the connection.
                {
                    n.Disconnect();
                }
                foreach (var d in ConnectedDevices)
                {
                    if (d != null)
                    {
                        if (d.Notifying)
                        {
                            d.CharacteristicNotificationEvent -= NotificationReceived;
                        }
                    }
                }
                Notifying = false;
            }
        }

        private async void streamButton_Click(object sender, EventArgs e)
        {
            if (!Notifying)
            {
                await StartNotifications();
            }
            else
            {
                bool result = false;
                foreach (var d in ConnectedDevices)
                {
                    if (d != null)
                    {
                        if (d.Notifying)
                        {
                            result = false;
                            result = await d.DisableDataStream();
                            d.CharacteristicNotificationEvent -= NotificationReceived;
                        }
                    }
                }
                if (result)
                {
                    streamButton.Enabled = false;
                    streamButton.Text = "Stream Data";
                    Notifying = false;
                }
            }
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (!Saving)
            {
                foreach (var c in ConnectedDevices)
                {
                    c.StartSavingAsync();
                }
                saveButton.Text = "Stop Saving";
                Saving = true;
            }
            else
            {
                foreach (var c in ConnectedDevices)
                {
                    c.StopSaving();
                }
                saveButton.Text = "Save Data";
                Saving = false;
            }
        }
        #endregion


        #region Connection Methods
        private void ConnectToDevices()
        {
            StatusUpdated(this, BLEComms.MsgType.Status, "Connecting...", null);
            string[] names = new string[3] { "Device_1", "Device_2", "Device_3" };
            foreach (var name in names)
            {
                Begin_BLE_Connection(name);
            }
        }

        private async void Begin_BLE_Connection(string name)
        {
            //Generate a new IMU class instance:
            BLEDevice bleDevice = new BLEDevice(name);
            //Subscribe to events:
            bleDevice.StatusUpdate += StatusUpdated;
            bleDevice.ConnectionStatus += this.ConnectionStatusUpdate;
            //Add the instances to a list:
            ConnectedDevices.Add(bleDevice);

            string address = string.Empty;
            switch (name)
            {
                case "Device_1":
                    address = textBox1.Text;
                    break;
                case "Device_2":
                    address = textBox2.Text;
                    break;
                case "Device_3":
                    address = textBox3.Text;
                    break;
            }
            //Connect:
            await bleDevice.QuickScanAndConnect(address);
        }

        private async Task Finish_BLE_Connection(BLEDevice device)
        {
            //If all the desired IMUs have connected start the data stream:
            if (ConnectedDevices.Count == BLEDevice.TOTAL_NUMBER_OF_DEVICES)
            {
                await Task.Delay(1000);
                if (!await WaitForServiceDiscovery(1000))
                {
                    StatusUpdated(this, BLEComms.MsgType.Error, "Required BLE services not available!", null);
                }
                //Start the data stream:
                await StartNotifications();
                this.Invoke((MethodInvoker)(() => streamButton.Enabled = true));
            }
        }

        private async Task<bool> WaitForServiceDiscovery(int cycles)
        {
            foreach (var n in ConnectedDevices.ToList())
            {
                while (!n.CheckForCoreCharacteristics() && service_discovery_timeout < cycles)
                {
                    //Delay while waiting for all services to be discovered:
                    await Task.Delay(10);
                    service_discovery_timeout++;
                }
                if (!n.CheckForCoreCharacteristics())
                {
                    return false;
                }
            }
            return true;
        }
        #endregion

        #region Data Methods
        private async Task StartNotifications()
        {
            bool success = false;
            foreach (var d in ConnectedDevices.ToList()) //foreach iterates by reference, i.e. a duplicate object is not created.
            {
                if (d != null)
                {
                    if (!d.Notifying)
                    {
                        success = await d.EnableDataStream();
                        d.CharacteristicNotificationEvent -= NotificationReceived;    //Attempt to prevent multiple subscriptions to the same event.
                        d.CharacteristicNotificationEvent += NotificationReceived;
                    }
                }
            }
            success = await CheckIfNotifying();
            if (!success)
            {
                if(await EndDataStream())
                {
                    this.Invoke((MethodInvoker)(() => streamButton.Enabled = true));
                    this.Invoke((MethodInvoker)(() => streamButton.Text = "Stream Data"));
                }
            }
            if (success)
            {
                this.Invoke((MethodInvoker)(() => streamButton.Text = "Disable"));
                this.Invoke((MethodInvoker)(() => saveButton.Enabled = true));
                Notifying = true;
            }
        }

        private async Task<bool> CheckIfNotifying()
        {
            bool result = false;
            foreach (var d in ConnectedDevices.ToList())
            {
                int notification_timeout = 0;
                while (!d.Notifying && notification_timeout < 100)
                {
                    await Task.Delay(10);
                    notification_timeout++;
                }
                if (!d.Notifying)
                {
                    result = false;
                }
                else
                {
                    result = true;
                }
            }
            return result;
        }

        private async Task<bool> EndDataStream()
        {
            bool result = false;
            foreach (var d in ConnectedDevices)
            {
                if (d != null)
                {
                    if (d.Notifying)
                    {
                        result = await d.DisableDataStream();
                        d.CharacteristicNotificationEvent -= NotificationReceived;
                    }
                }
            }
            return result;
        }

        #endregion

        #region UI
        private void StatusUpdated(object sender, BLEComms.MsgType type, string message, byte[] data)
        {
            this.Invoke((MethodInvoker)(() => toolStripStatusLabel.Text = message));
        }
        #endregion
    }
}
