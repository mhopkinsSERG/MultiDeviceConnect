using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiDeviceConnect
{
    partial class BLEDevice
    {
        #region Fields
        private bool subscribedToConnectionStatus = false;
        private bool notificationsEnabled = false;      //Secondary flag necessary as on occasion a notification will be received after they have been disabled.  
        private BLEComms bleComms = new BLEComms();
        private SaveManager saveManager;
        readonly object eventLock = new object();       //Lock for event delegate access.
        #endregion

        #region Properties
        public string Id { get; set; }
        public bool Connected { get; private set; }
        public bool Notifying { get; private set; }
        public bool Paired { get; private set; }
        public bool SaveFlag { get; set; }
        #endregion

        #region Event Delegates
        //Program Status:
        public delegate void MessageChangedEvent(object sender, BLEComms.MsgType type, string message, byte[] data = null);     //Information changed event delegate.
        public event MessageChangedEvent StatusUpdate;                                                                          //Information changed event.

        //Connection Status:
        public delegate void ConnectionStatusEvent(object sender, BLEComms.ConnectionStatus status);                //Connection changed event delegate.
        public event ConnectionStatusEvent ConnectionStatus;                                                        //Connection changed event.

        //Notifications:
        private EventHandler<NotificationEventArgs> characteristicNotificationEvent;        //Backing store.
        public event EventHandler<NotificationEventArgs> CharacteristicNotificationEvent
        {
            add
            {
                lock (eventLock)
                {
                    if (characteristicNotificationEvent == null || !characteristicNotificationEvent.GetInvocationList().Contains(value))
                    {
                        characteristicNotificationEvent += value;
                    }
                }
            }
            remove
            {
                lock (eventLock)
                {
                    characteristicNotificationEvent -= value;
                }
            }
        }
        #endregion

        #region Event Handlers
        private void StatusUpdated(object sender, BLEComms.MsgType type, string message, byte[] data)
        {
            //Propagate the event:
            OnStatusUpdate(type, message, data);
        }

        /// <summary>
        /// Connection status event handler. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void ConnectionStatusChanged(object sender, BLEComms.ConnectionStatus status)
        {
            switch (status)
            {
                case BLEComms.ConnectionStatus.Connected:
                    Connected = true;
                    //Propagate the event:
                    OnConnectionStatus(BLEComms.ConnectionStatus.Connected);
                    break;
                case BLEComms.ConnectionStatus.Disconnected:
                    Connected = false;
                    Notifying = false;
                    bleComms.ConnectionStatusChanged -= this.ConnectionStatusChanged;
                    subscribedToConnectionStatus = false;
                    OnConnectionStatus(BLEComms.ConnectionStatus.Disconnected);
                    break;
                case BLEComms.ConnectionStatus.Timeout:
                    Connected = false;
                    Notifying = false;
                    bleComms.ConnectionStatusChanged -= this.ConnectionStatusChanged;
                    subscribedToConnectionStatus = false;
                    OnConnectionStatus(BLEComms.ConnectionStatus.Timeout);
                    break;
                case BLEComms.ConnectionStatus.Paired:
                    Paired = true;
                    OnConnectionStatus(BLEComms.ConnectionStatus.Paired);
                    break;
                case BLEComms.ConnectionStatus.Unpaired:
                    Paired = false;
                    OnConnectionStatus(BLEComms.ConnectionStatus.Unpaired);
                    break;
            }
        }

        /// <summary>
        /// Notification received event handler. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void NotificationReceived(object sender, NotificationEventArgs args)
        {
            await Task.Run(async () => {
                if (!Notifying)
                {
                    if (notificationsEnabled)
                    {
                        //Update the notification status flag:
                        Notifying = true;
                    }
                }
                //Process the data:
                await ProcessData(args.data);
                //Propagate the event:
                OnCharacteristicNotification(args);
            });
        }

        
        #endregion

        #region Event Triggers
        /// <summary>
        /// Thread safe method for triggering the status event.
        /// </summary>
        /// <param name="args"></param>
        protected virtual void OnStatusUpdate(BLEComms.MsgType type, string message, byte[] data = null)
        {
            //Thread safe method for triggering the event.
            //Make a private copy of the delegate and check it for null:
            MessageChangedEvent localCopy = StatusUpdate;
            if (localCopy != null)
            {
                localCopy(this, type, message, data);
            }
        }

        /// <summary>
        /// Thread safe method for triggering the connection status event.
        /// </summary>
        /// <param name="args"></param>
        protected virtual void OnConnectionStatus(BLEComms.ConnectionStatus status)
        {
            //Thread safe method for triggering the event.
            //Make a private copy of the delegate and check it for null:
            ConnectionStatusEvent localCopy = ConnectionStatus;
            if (localCopy != null)
            {
                localCopy(this, status);
            }
        }

        /// <summary>
        /// Thread safe method for triggering the characteristic notification event.
        /// </summary>
        /// <param name="args"></param>
        protected virtual void OnCharacteristicNotification(NotificationEventArgs args)
        {
            //Make a private copy of the delegate and check it for null:
            EventHandler<NotificationEventArgs> localCopy;
            lock (eventLock)
            {
                localCopy = characteristicNotificationEvent;
            }
            localCopy?.Invoke(this, args);
        }
        #endregion

        #region Constructor
        public BLEDevice()
        {
            bleComms.StatusUpdate += this.StatusUpdated;
            Connected = false;
            Notifying = false;
            Paired = false;
        }

        public BLEDevice(string _Id)
        {
            Id = _Id;
            bleComms.StatusUpdate += this.StatusUpdated;
            Connected = false;
            Notifying = false;
            Paired = false;
        }
        #endregion

        #region Connect/Disconnect
        public async Task QuickScanAndConnect(string address)
        {
            if (!subscribedToConnectionStatus)
            {
                bleComms.ConnectionStatusChanged += this.ConnectionStatusChanged;
                subscribedToConnectionStatus = true;
            }
            await bleComms.QuickScanAndConnect(Convert.ToUInt64(address));
        }

        public async void Disconnect()
        {
            if (Notifying)
            {
                await DisableDataStream();
                await Task.Delay(10);     //Delay necessary as the write command to instruct the IMU to stop streaming data keeps the characteristic open, despite its disposal.
            }
            bleComms.Disconnect();
        }
        #endregion


        #region Data Methods
        public bool CheckForCoreCharacteristics()
        {
            //Data stream characteristic:
            if ((bleComms.FindCharacteristic(BLEComms.DATA_STREAM_UUID) == null) || bleComms.FindCharacteristic(BLEComms.COMMAND_UUID) == null)
            {
                return false;
            }
            return true;
        }
        public async Task<bool> EnableDataStream()
        {
            //Enable notifications:
            bleComms.EnableCharacteristicNotifications(BLEComms.DATA_STREAM_UUID);
            //Subscribe to the notification event:
            bleComms.CharacteristicNotificationEvent -= NotificationReceived;
            bleComms.CharacteristicNotificationEvent += NotificationReceived;
            notificationsEnabled = true;          
            return await WriteCommand(BitConverter.GetBytes(START_DEVICE));
        }

        public async Task<bool> DisableDataStream()
        {
            //Write to the command register to instruct it to stop sending data:
            bool result = await WriteCommand(BitConverter.GetBytes(STOP_LOGGING));
            //Disable notifications:
            await bleComms.CancelCharacteristicNotifications(BLEComms.DATA_STREAM_UUID);
            //Unsubscribe from the notification event:
            bleComms.CharacteristicNotificationEvent -= NotificationReceived;
            notificationsEnabled = false;
            Notifying = false;
            return result;
        }

        public async Task<bool> WriteCommand(byte[] command)
        {
            byte[] output = command;
            Array.Reverse(output);
            return await bleComms.WriteCharacteristic(BLEComms.COMMAND_UUID, output);
        }


        public async Task ProcessData(byte[] source)
        {
            //Data is supplied in human readable format, write this to file:
            if (SaveFlag)
            {               
                await saveManager.SaveAsync(Encoding.ASCII.GetString(source));
            }
        }

        public void StartSavingAsync()
        {
            //Generate a filename:
            string filename = GenerateFileName();
            //Create the save file:
            StartSavingAsync(filename);
        }

        public void StartSavingAsync(string filename)
        {
            saveManager = new SaveManager();
            string title = "Device Data:";
            Task.Run(() => saveManager.InitSaveFileAsync(filename, title));
            SaveFlag = true;
        }

        public void StopSaving()
        {
            SaveFlag = false;
        }

        private string GenerateFileName()
        {
            //Combine the device ID with a date and time:
            return Id + "_" + DateTime.Now.ToString("ddMMyy_HHmmss") + ".csv";
        }
        #endregion


    }
}
