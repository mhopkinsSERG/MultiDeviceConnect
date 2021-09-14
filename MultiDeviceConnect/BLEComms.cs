using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth.Advertisement;
using System.Text;
using System.Diagnostics;
using System.Reactive.Linq;

namespace MultiDeviceConnect
{
	/// <summary>
	/// A Bluetooth Low Energy library performing base functions such as connection, read/write operations and notification subscription.
	/// </summary>
	public partial class BLEComms
	{
		#region Class Variables
		private string VERSION_NUMBER = "1.0.0";
		private DeviceWatcher deviceWatcher;                                                            //A device watcher used to discover local Bluetooth LE devices.
		private List<BluetoothLEDevice> BLE_Devices = new List<BluetoothLEDevice>();                    //A list to store detected BLE devices.
		private List<BLE_Service> GATT_Services = new List<BLE_Service>();                              //A list to store both service and characteristic information.
		private List<GattCharacteristic> NotifyingCharacteristics = new List<GattCharacteristic>();     //A list to store all notifying characteristics.
		public BluetoothLEDevice CurrentDevice { get; private set; }                                    //The currently connected BLE device.
		#endregion


		#region Events
		//BLE Device Discovery:
		public delegate void DeviceWatcherChangedEvent(object sender, MsgType type, BluetoothLEDevice bleDevice);  //BLE device discovery event delegate.
		public event DeviceWatcherChangedEvent DeviceWatcherChanged;                                //BLE device discovery event.
		protected virtual void OnDeviceWatcherChanged(MsgType type, BluetoothLEDevice bleDevice)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			DeviceWatcherChangedEvent localCopy = DeviceWatcherChanged;
			if (localCopy != null)
			{
				localCopy(this, type, bleDevice);
			}
		}

		//Service Added:
		public delegate void GattDeviceServiceAddedEvent(object sender, GattDeviceService gattDeviceService);       //BLE service event delegate.
		public event GattDeviceServiceAddedEvent GattDeviceServiceAdded;                                            //BLE service event.
		protected virtual void OnGattDeviceServiceAdded(GattDeviceService gattDeviceService)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			GattDeviceServiceAddedEvent localCopy = GattDeviceServiceAdded;
			if (localCopy != null)
			{
				localCopy(this, gattDeviceService);
			}
		}

		//Characteristic Added:
		public delegate void CharacteristicAddedEvent(object sender, GattCharacteristic gattCharacteristic);        //BLE characteristic event delegate.
		public event CharacteristicAddedEvent CharacteristicAdded;                                                  //BLE characteristic event.
		protected virtual void OnCharacteristicAdded(GattCharacteristic gattCharacteristic)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			CharacteristicAddedEvent localCopy = CharacteristicAdded;
			if (localCopy != null)
			{
				localCopy(this, gattCharacteristic);
			}
		}

		//Characteristic Read:
		public delegate void CharacteristicReadEvent(object sender, MsgType type, byte[] data = null);              //BLE characteristic read event delegate.
		public event CharacteristicReadEvent CharacteristicRead;                                                    //BLE characteristic read event.
		protected virtual void OnCharacteristicRead(MsgType type, byte[] data = null)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			CharacteristicReadEvent localCopy = CharacteristicRead;
			if (localCopy != null)
			{
				localCopy(this, type, data);
			}
		}

		//Lock for event delegate access:
		readonly object eventLock = new object();

		/// <summary>
		/// //Thread safe method for triggering the notification event.
		/// <see cref="https://docs.microsoft.com/en-us/dotnet/standard/events/"/>
		/// </summary>
		/// <param name="type"></param>
		/// <param name="data"></param>
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

		/// <summary>
		/// Event handler and backing store used to transfer notification data.
		/// <see cref="https://docs.microsoft.com/en-us/dotnet/csharp/event-pattern"/>
		/// <see cref="https://medium.com/@dinesh.jethoe/events-in-c-explained-4a464b110fdc"/>
		/// <see cref="https://stackoverflow.com/questions/367523/how-to-ensure-an-event-is-only-subscribed-to-once"/>
		/// <see cref="https://stackoverflow.com/questions/4349195/what-is-the-preferred-way-to-bubble-events"/>
		/// </summary>
		private EventHandler<NotificationEventArgs> characteristicNotificationEvent;        //Backing store.
		public event EventHandler<NotificationEventArgs> CharacteristicNotificationEvent
		{
			add
			{
				lock (eventLock)    //To prevent variable changes whilst the check is occurring.
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

		//Program Status:
		public delegate void MessageChangedEvent(object sender, MsgType type, string message, byte[] data = null);  //Information changed event delegate.
		public event MessageChangedEvent StatusUpdate;                                                              //Information changed event.
		protected virtual void OnStatusUpdate(MsgType type, string message, byte[] data = null)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			MessageChangedEvent localCopy = StatusUpdate;
			if (localCopy != null)
			{
				localCopy(this, type, message, data);
			}
		}

		//Connection Status:
		public delegate void ConnectionStatusEvent(object sender, ConnectionStatus status);                         //Connection changed event delegate.
		public event ConnectionStatusEvent ConnectionStatusChanged;                                                 //Connection changed event.
		protected virtual void OnConnectionStatus(ConnectionStatus status)
		{
			//Thread safe method for triggering the event.
			//Make a private copy of the delegate and check it for null:
			ConnectionStatusEvent localCopy = ConnectionStatusChanged;
			if (localCopy != null)
			{
				localCopy(this, status);
			}
		}

		private bool CheckForSubscribers(Type type, string eventname, object obj)
		{
			var handler = type.GetField("Added", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(obj);
			if (handler == null)
			{
				//The event has no subscribers.
				return false;
			}
			else
			{
				//The event has subsribers:
				return true;
			}
		}
		#endregion

		#region Version
		public string GetVersion()
		{
			return VERSION_NUMBER;
		}
		#endregion

		#region Constructor
		public BLEComms()
		{

		}
		#endregion

		#region Search
		//Search for Bluetooth LE devices:
		public void StartWatcher()
		{
			BLE_Devices.Clear();
			string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };
			string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
			//Create the watcher:
			deviceWatcher = DeviceInformation.CreateWatcher(aqsAllBluetoothLEDevices, requestedProperties, DeviceInformationKind.AssociationEndpoint);
			//Register event handlers:
			deviceWatcher.Added += DeviceWatcher_Added;
			deviceWatcher.Stopped += DeviceWatcher_Stopped;
			deviceWatcher.Updated += DeviceWatcher_Updated;
			deviceWatcher.Removed += DeviceWatcher_Removed;
			deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
			//Start the watcher:
			deviceWatcher.Start();
			this.OnStatusUpdate(MsgType.Status, "Discovering BLE devices...");
		}

		//Stop searching for Bluetooth LE devices:
		public void StopWatcher()
		{
			if (deviceWatcher != null)
			{
				if (deviceWatcher?.Status == DeviceWatcherStatus.Started)
				{
					//Check if the watcher has subscribers and unregister as required:
					if (CheckForSubscribers(typeof(DeviceWatcher), "Added", deviceWatcher)) { deviceWatcher.Added -= DeviceWatcher_Added; }
					if (CheckForSubscribers(typeof(DeviceWatcher), "Stopped", deviceWatcher)) { deviceWatcher.Stopped -= DeviceWatcher_Stopped; }
					if (CheckForSubscribers(typeof(DeviceWatcher), "Updated", deviceWatcher)) { deviceWatcher.Updated -= DeviceWatcher_Updated; }
					if (CheckForSubscribers(typeof(DeviceWatcher), "Removed", deviceWatcher)) { deviceWatcher.Removed -= DeviceWatcher_Removed; }
					if (CheckForSubscribers(typeof(DeviceWatcher), "EnumerationCompleted", deviceWatcher)) { deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted; }
					deviceWatcher?.Stop();
					deviceWatcher = null;
				}
			}
		}

		//Obtain discovered Bluetooth LE devices:
		private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
		{
			//Protection against a rare condition in which the task runs after the app has stopped the deviceWatcher:
			if (sender == deviceWatcher)
			{
				//Update the user interface:
				OnStatusUpdate(MsgType.Status, "Device discovered."); // + args.Id
																	  //Retrieve device information and add the discovered device to the device list:
				_ = RetrieveBLEDevice(args.Id);
			}
		}

		private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
		{
			OnStatusUpdate(MsgType.Status, "Discovery stopped.");
		}

		//Retrieve discovered BLE devices:
		private Task RetrieveBLEDevice(string Id)
		{
			try
			{
				BluetoothLEDevice.FromIdAsync(Id).Completed = (asyncInfo, asyncStatus) =>
				{
					if (asyncStatus == AsyncStatus.Completed)
					{
						//Add any discovered BLE devices to the device list and raise a watcher changed event:
						BluetoothLEDevice bleDevice = asyncInfo.GetResults();
						BLE_Devices.Add(bleDevice);
						OnDeviceWatcherChanged(MsgType.Success, bleDevice);
					}
				};
			}
			catch (Exception e)
			{
				OnStatusUpdate(MsgType.Error, "No device could be found. Error: " + e.ToString());
				//Restart the watcher:
				StartWatcher();
			}
			return Task.CompletedTask;
		}

		private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
		{
			//Protection against a rare condition in which the task runs after the app has stopped the deviceWatcher:
			if (sender == deviceWatcher)
			{
				//Find the corresponding DeviceInformation in the currently stored collection and pass the update object to it to automatically update:
				foreach (var bleDevice in BLE_Devices)
				{
					if (bleDevice.DeviceInformation.Id == deviceInfoUpdate.Id)
					{
						bleDevice.DeviceInformation.Update(deviceInfoUpdate);
						break;
					}
				}
			}
		}

		private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
		{
			//Protection against a rare condition in which the task runs after the app has stopped the deviceWatcher:
			if (sender == deviceWatcher)
			{
				//Find the corresponding device in the device list and remove:
				foreach (var bleDevice in BLE_Devices)
				{
					if (bleDevice.DeviceInformation.Id == deviceInfoUpdate.Id)
					{
						BLE_Devices.Remove(bleDevice);
						break;
					}
				}
			}
		}

		private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object e)
		{
			//Protection against a rare condition in which the task runs after the app has stopped the deviceWatcher:
			if (sender == deviceWatcher)
			{
				OnStatusUpdate(MsgType.Success, "Device enumeration completed.");
			}
		}
		#endregion Search

		#region Connect
		//Discover whether or not the target device has been paired to the machine:
		public async Task CheckPaired(string name)
		{
			//Obtain the correct device using the device name:
			BluetoothLEDevice device = BLE_Devices.Where(d => d.Name == name).FirstOrDefault();
			if (device != null)
			{
				//Get Bluetooth device information:
				var deviceInfo = await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask();
				//Null guard:																				
				if (deviceInfo == null)
				{
					throw new ArgumentNullException("Failed to obtain Bluetooth device information.");
				}

				if (!deviceInfo.DeviceInformation.Pairing.IsPaired)
				{
					//Pair the device:
					await PairToDevice(deviceInfo);
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "The device is already paired.");
				}
			}
		}

		//Attempt to pair to a BLE device via deviceId:
		public async Task PairToDevice(string name)
		{
			//Obtain the correct device using the device name:
			BluetoothLEDevice device = BLE_Devices.Where(d => d.Name == name).FirstOrDefault();
			if (device != null)
			{
				await PairToDevice(device);
			}
		}

		public async Task PairToDevice(BluetoothLEDevice device)
		{
			//Attempt a custom pairing:
			if (device.DeviceInformation.Pairing.CanPair)
			{
				OnStatusUpdate(MsgType.Status, "Attempting to pair...");
				var customPairing = device.DeviceInformation.Pairing.Custom;
				customPairing.PairingRequested += CustomPairing_PairingRequested;
				//Attempt to pair:
				DevicePairingResult result = await customPairing.PairAsync(DevicePairingKinds.ConfirmOnly);
				customPairing.PairingRequested -= CustomPairing_PairingRequested;
				OnStatusUpdate(MsgType.Status, "Pairing result = " + result.Status.ToString());
				if (result.Status == DevicePairingResultStatus.Paired)
				{
					//Obtain services and characteristics:
					await OpenDataChannels(device);
					OnConnectionStatus(ConnectionStatus.Paired);
				}
				else
				{
					OnConnectionStatus(ConnectionStatus.Unpaired);
				}
			}
			else
			{
				OnStatusUpdate(MsgType.Status, "The device cannot be paired with.");
			}
		}

		private void CustomPairing_PairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
		{
			args.Accept();
		}

		public async Task UnpairDevice(string name)
		{
			//Obtain the correct device using the device name:
			BluetoothLEDevice device = BLE_Devices.Where(d => d.Name == name).FirstOrDefault();
			if (device != null)
			{
				//Get Bluetooth device information:
				var deviceInfo = await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask();
				//Null guard:
				if (deviceInfo == null)
				{
					throw new ArgumentNullException("Failed to obtain Bluetooth device information.");
				}

				if (deviceInfo.DeviceInformation.Pairing.IsPaired)
				{
					OnStatusUpdate(MsgType.Status, "Terminating the bond...");
					//Delete the bond:
					DeviceUnpairingResult result = await device.DeviceInformation.Pairing.UnpairAsync();
					OnStatusUpdate(MsgType.Status, "Unpairing result = " + result.Status.ToString());
				}
			}
		}

		/// <summary>
		/// Pairing and Bonding Definitions:
		/// Pairing: authentication process involving key generation.
		/// Bonding: key preservation for encryption on connection and reconnection.
		/// SM (Security Manager): portion of the BLE protocol stack that specifies all security measures.
		/// SMP (Security Manager Protocol): the command sequence between two devices - strictly regulates timing of pairing air packets.
		/// OOB (Out Of Band): pairing information is shared via means such as NFC, UART etc. rather than the BLE radio.
		/// Passkey (or pin code): string of numbers entered by the user for authentication (must be 6 bits).
		/// Numeric comparison: same as the passkey, but displayed rather than entered via a keyboard.
		/// MITM (Man In The Middle): during communication between A and B, device C may be inserted to simulate A or B and thus, has the ability to intercept communication.
		/// LESC/SC (LE Secure Connections): a key generation method using the Diffie-Hellman key exchange algorithm.
		/// Legacy Pairing: the pairing method employed before LESC.
		/// TK (Temporary Key): if "just work" pairing is employed, the TK is 0, if the "passkey" method is employed, the TK is the passkey, if "OOB" pairing is employed, the TK is the information in the OOB.
		/// STK (Short Term Key): random numbers of devices A and B are encrypted through TK to obtain an STK (legacy pairing).
		/// LTK (Long Term Key): used by legacy pairing and LESC.
		/// IRK (Identity Resolving Key): used to determine whether or not an address is from the same device as some devices change addresses over time.
		/// Idendity Address: the unique address of the device. There are four types, public, random static, private resolvable and random unresolved.
		/// IO Capabilities: Input and output capabilities of the BLE device, e.g. whether there is a keyboard/display etc.
		/// Key Size (/Key Length): default length of keys is 16 bytes.
		/// <see cref="https://www.programmersought.com/article/93244797602/"/>
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>

		public async Task<bool> CheckConnected(ulong address)
		{
			//Obtain a Bluetooth LE object:
			BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
			if (device.ConnectionStatus == BluetoothConnectionStatus.Connected) { return true; }
			else { return false; }
		}

		public async Task Connect(string name)
		{
			if (BLE_Devices != null && BLE_Devices.Count > 0)
			{
				//Obtain the correct device using the device name:
				BluetoothLEDevice device = BLE_Devices.Where(d => d.Name == name).FirstOrDefault();
				await Connect(device);
			}
		}

		public async Task Connect(BluetoothLEDevice device)
		{

			if (device != null)
			{
				if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
				{
					//StopWatcher();
					OnStatusUpdate(MsgType.Status, "Opening data channels.");
					await OpenDataChannels(device);
				}
			}
		}

		/// <summary>
		/// Awaitable connect method utilising observables. This allows chaining of device discovery and connection to a specific discovered device.
		/// <see cref="https://stackoverflow.com/questions/35420940/windows-uwp-connect-to-ble-device-after-discovery"/>
		/// <see cref="http://introtorx.com/Content/v1.0.10621.0/08_Transformation.html"/>
		/// <see cref="https://www.codeproject.com/Articles/1081707/Using-Reactive-Extensions-the-basics"/>
		/// </summary>
		/// <param name="address"></param>
		public async Task ScanAndConnect(ulong address)
		{
			//If no devices are stored, attempt discovery:
			if (BLE_Devices == null || BLE_Devices.Count == 0)
			{
				string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };
				string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
				//Create the watcher:
				DeviceWatcher watcher = DeviceInformation.CreateWatcher(aqsAllBluetoothLEDevices, requestedProperties, DeviceInformationKind.AssociationEndpoint);    //Enable GATT.
																																									  //Convert events to observable sequences:
				IObservable<BluetoothLEDevice> source = Observable.FromEventPattern(watcher, nameof(watcher.Added)).SelectMany(async evt =>
				{
					var added = ((DeviceInformation)evt.EventArgs);
					return await GetBLEDevice(added.Id);
				});
				//Attach the event handlers:
				source.Publish().Connect();
				//Start the watcher and await results:
				watcher.Start();

				try
				{
					//Obtain the device with an address matching the desired target (timeout if not found):
					var device = await source.TakeUntil(d => d?.BluetoothAddress == address).Select(async d => await Observable.Return(d)).Timeout(DateTime.Now.AddSeconds(SLOW_SCAN_TIMEOUT));
					watcher.Stop();
					await Connect(await device);
				}
				catch (Exception e)
				{
					watcher.Stop();
					OnConnectionStatus(ConnectionStatus.Timeout);
					OnStatusUpdate(MsgType.Error, "Connection error. " + e.Message);
				}

				//Obtain the device with an address matching the desired target:

			}
			else
			{
				await Connect(address);
			}
		}

		//Obtain a BLE Device object from the supplied ID string:
		private async Task<BluetoothLEDevice> GetBLEDevice(string Id)
		{
			try
			{
				return await BluetoothLEDevice.FromIdAsync(Id);
			}
			catch (Exception e)
			{
				OnStatusUpdate(MsgType.Error, e.Message);
			}
			return null;
		}

		/// <summary>
		/// Method for discovering and connecting to devices using the AdvertisementWatcher class. Note: this is quicker than using DeviceWatcher.
		/// </summary>
		/// <param name="address"></param>
		/// <returns></returns>
		/// 
		public async Task QuickScanAndConnect(ulong address)
		{
			//If no devices are stored, attempt discovery:
			if (BLE_Devices == null || BLE_Devices.Count == 0)
			{
				//Create the watcher:
				BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
				//Convert events to observable sequences:
				IObservable<BluetoothLEDevice> source = Observable.FromEventPattern(watcher, nameof(watcher.Received)).SelectMany(async evt =>
				{
					var added = ((BluetoothLEAdvertisementReceivedEventArgs)evt.EventArgs);
					return await GetBLEDevice(added.BluetoothAddress);
				});
				//Attach the event handlers:
				source.Publish().Connect();
				//Start the watcher and await results:
				watcher.Start();

				try
				{
					//Obtain the device with an address matching the desired target (timeout if not found):
					var device = await source.TakeUntil(d => d?.BluetoothAddress == address).Select(async d => await Observable.Return(d)).Timeout(DateTime.Now.AddSeconds(QUICK_SCAN_TIMEOUT));
					watcher.Stop();
					await Connect(await device);
				}
				catch (Exception e)
				{
					watcher.Stop();
					OnConnectionStatus(ConnectionStatus.Timeout);
					OnStatusUpdate(MsgType.Error, "Connection error. " + e.Message);
				}
			}
			else
			{
				await Connect(address);
			}
		}

		private async Task<BluetoothLEDevice> GetBLEDevice(UInt64 address)
		{
			try
			{
				return await BluetoothLEDevice.FromBluetoothAddressAsync(address);
			}
			catch (Exception e)
			{
				OnStatusUpdate(MsgType.Error, e.Message);
			}
			return null;
		}

		/// <summary>
		/// Method for connecting via BLE address. It's possible that the DeviceWatcher must have been started prior to a call to this to prevent FromBluetoothAddressAsync returning null.
		/// </summary>
		/// <param name="address"></param>
		/// <returns></returns>
		public async Task Connect(ulong address)
		{
			await Connect(await BluetoothLEDevice.FromBluetoothAddressAsync(address));
		}

		private async Task OpenDataChannels(BluetoothLEDevice device)
		{
			//Connect via interaction with the services:
			ObtainServices(device);
			//Obtain Bluetooth device information:
			CurrentDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(device.BluetoothAddress);
			//Null guard:
			if (CurrentDevice == null)
			{
				throw new ArgumentNullException("Failed to obtain Bluetooth device information.");
			}
			else
			{
				//Subscribe to the connection status changed event:
				CurrentDevice.ConnectionStatusChanged += DeviceConnectionStatusChanged;
			}
		}

		public void Disconnect()
		{
			if (CurrentDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				//Dispose of all characteristic and service sessions:
				if (ClearBluetoothLEDeviceAsync().Result)
				{
					OnStatusUpdate(MsgType.Success, "The device has been disconnected.");
					OnConnectionStatus(ConnectionStatus.Disconnected);
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "Unable to disconnect.");
				}
			}
		}

		private async Task<bool> ClearBluetoothLEDeviceAsync()
		{
			await DisableCharacteristicNotifications();
			foreach (var s in GATT_Services)
			{
				s.Dispose();
			}
			GATT_Services.Clear();
			CurrentDevice.ConnectionStatusChanged -= DeviceConnectionStatusChanged;
			CurrentDevice?.Dispose();
			CurrentDevice = null;
			//Forced garbage collection absolutely required for disconnect:
			GC.Collect();
			GC.WaitForPendingFinalizers();
			return true;
		}

		//Handle connection changes:
		private void DeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
		{
			if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && CurrentDevice != null)
			{
				OnConnectionStatus(ConnectionStatus.Disconnected);
				CurrentDevice.ConnectionStatusChanged -= DeviceConnectionStatusChanged;
			}
			else
			{
				if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected && CurrentDevice != null)
				{
					OnConnectionStatus(ConnectionStatus.Connected);
				}
			}
		}

		#endregion Connect

		#region Services and Characteristics
		//Obtain services and characteristics from a connected GATT device:
		public async void ObtainServices(BluetoothLEDevice device)
		{
			if (device != null)
			{
				//Obtain the services for the selected device:
				var result = await device.GetGattServicesAsync();
				if (result.Status == GattCommunicationStatus.Success)
				{
					GATT_Services.Clear();
					foreach (GattDeviceService service in result.Services)
					{
						//Obtain the corresponding characteristics for the discovered service:
						List<GattCharacteristic> gattCharacteristics = await ObtainCharacteristics(service);
						GATT_Services.Add(new BLE_Service(service, gattCharacteristics));
					}
				}
			}
		}

		public void ObtainServices(string name)
		{
			//Obtain the correct device using the device name:
			BluetoothLEDevice device = BLE_Devices.Where(d => d.Name == name).FirstOrDefault();
			ObtainServices(device);
		}

		//Obtain GATT characteristics asynchronously from a specified service:
		public async Task<List<GattCharacteristic>> ObtainCharacteristics(GattDeviceService service)
		{
			List<GattCharacteristic> gattCharacteristics = new List<GattCharacteristic>();
			var result = await service.GetCharacteristicsAsync();
			if (result.Status == GattCommunicationStatus.Success)
			{
				foreach (var c in result.Characteristics)
				{
					gattCharacteristics.Add(c);
				}
			}
			return gattCharacteristics;
		}

		//Read data from a specified characteristic via its UUID:
		public async Task<byte[]> ReadCharacteristic(string uuid)
		{
			//Find the characteristic in the stored characteristic list and then read from it:
			return await ReadCharacteristic(FindCharacteristic(uuid));
		}

		/// <summary>
		/// Find a characteristic in the discovered service and characteristic list via its UUID.
		/// </summary>
		/// <param name="uuid"> The UUID of the target characteristic.</param>
		/// <returns></returns>
		public GattCharacteristic FindCharacteristic(string uuid)
		{
			foreach (var s in GATT_Services.ToList())
			{
				foreach (var c in s.Characteristics)
				{
					//Check for short (16-bit UUIDs):
					if (uuid.Length == SHORT_UUID_LENGTH)
					{
						if (c.Uuid.ToString().Contains(uuid))
						{
							return c;
						}

					}
					else
					{
						//Check for long UUIDs:
						if (Equals(c.Uuid.ToString(), uuid))
						{
							return c;
						}
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Read data from a specified characteristic.
		/// </summary>
		/// <param name="characteristic"> The desired characteristic to obtain data from.</param>
		/// <returns></returns>
		public async Task<byte[]> ReadCharacteristic(GattCharacteristic characteristic)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
				if (properties.HasFlag(GattCharacteristicProperties.Read))
				{
					//The characteristic supports being read:
					GattReadResult result = await characteristic.ReadValueAsync();
					if (result.Status == GattCommunicationStatus.Success)
					{
						var reader = DataReader.FromBuffer(result.Value);
						byte[] input = new byte[reader.UnconsumedBufferLength];
						reader.ReadBytes(input);
						return input;
					}
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "The characteristic does not support read operations.");
				}
			}
			return null;
		}

		/// <summary>
		/// Write a byte array to a characteristic specified by its UUID.
		/// </summary>
		/// <param name="uuid"> The UUID of the target characteristic.</param>
		/// <param name="value"> The values to be written.</param>
		/// <returns></returns>
		public async Task<bool> WriteCharacteristic(string uuid, byte[] value)
		{
			return await WriteCharacteristic(FindCharacteristic(uuid), value);
		}

		/// <summary>
		/// Write a byte array to a characteristic specified by its UUID.
		/// </summary>
		/// <param name="uuid"> The UUID of the target characteristic.</param>
		/// <param name="value"> The values to be written.</param>
		/// <returns></returns>
		public async Task<bool> WriteCharacteristic(GattCharacteristic characteristic, byte[] value)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
				if (properties.HasFlag(GattCharacteristicProperties.Write))
				{
					//The characteristic supports being written to:
					var writer = new DataWriter();
					writer.WriteBytes(value);
					try
					{
						GattCommunicationStatus result = await characteristic.WriteValueAsync(writer.DetachBuffer());
						if (result == GattCommunicationStatus.Success)
						{
							string str = "Data " + System.Text.Encoding.ASCII.GetString(value) + " written to the characterstic.";
							OnStatusUpdate(MsgType.Success, str);
							return true;
						}
						else
						{
							OnStatusUpdate(MsgType.Error, "Could not write to the characteristic.");
						}
					}
					catch (Exception e)
					{
						OnStatusUpdate(MsgType.Error, e.Message);
					}
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "The characteristic does not support write operations.");
				}
			}
			else
			{
				OnStatusUpdate(MsgType.Error, "Characteristic is null.");
			}
			return false;
		}

		/// <summary>
		/// Write a single byte to a characteristic specified by its UUID.
		/// </summary>
		/// <param name="uuid"> The UUID of the target characteristic.</param>
		/// <param name="value"> The value to be written.</param>
		/// <returns></returns>
		public async Task<bool> WriteCharacteristic(string uuid, byte value)
		{
			return await WriteCharacteristic(FindCharacteristic(uuid), value);
		}

		/// <summary>
		/// Write a single byte to a characteristic.
		/// </summary>
		/// <param name="characteristic"> The target characteristic.</param>
		/// <param name="value"> The value to be written.</param>
		/// <returns></returns>
		public async Task<bool> WriteCharacteristic(GattCharacteristic characteristic, byte value)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
				if (properties.HasFlag(GattCharacteristicProperties.Write))
				{
					//The characteristic supports being written to:
					var writer = new DataWriter();
					writer.WriteByte(value);
					try
					{
						GattCommunicationStatus result = await characteristic.WriteValueAsync(writer.DetachBuffer());
						if (result == GattCommunicationStatus.Success)
						{
							string str = "Data " + System.Text.Encoding.ASCII.GetString(new[] { value }) + " written to the characterstic.";
							OnStatusUpdate(MsgType.Success, str);
							return true;
						}
						else
						{
							OnStatusUpdate(MsgType.Error, "Could not write to the characteristic.");
						}
					}
					catch (Exception e)
					{
						OnStatusUpdate(MsgType.Error, e.Message);
					}
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "The characteristic does not support write operations.");
				}
			}
			else
			{
				OnStatusUpdate(MsgType.Error, "No characteristic has been selected.");
			}
			return false;
		}

		/// <summary>
		/// Enable notifications for a characteristic specified by its UUID.
		/// </summary>
		/// <param name="uuid"> The UUID of the target characteristic.</param>
		public void EnableCharacteristicNotifications(string uuid)
		{
			EnableCharacteristicNotifications(FindCharacteristic(uuid));
		}

		/// <summary>
		/// Enable notifications for a specified characteristic.
		/// </summary>
		/// <param name="characteristic"> The target notifying characteristic.</param>
		public async void EnableCharacteristicNotifications(GattCharacteristic characteristic)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
				if (properties.HasFlag(GattCharacteristicProperties.Notify))
				{
					//The characteristic supports notifications, inform the Server device that the client wishes to know each time the characteristic changes:
					GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
					if (status == GattCommunicationStatus.Success)
					{
						OnStatusUpdate(MsgType.Success, "The server has been informed of the client's interest.");
						//Subscribe to the characteristic changed event:
						characteristic.ValueChanged += CharacteristicValueChanged;
						if (!NotifyingCharacteristics.Contains(characteristic))
						{
							NotifyingCharacteristics.Add(characteristic);
						}
					}
					else
					{
						OnStatusUpdate(MsgType.Error, "Could not update the Client Characteristic Configuration Descriptor (CCCD)");
					}
				}
				else
				{
					OnStatusUpdate(MsgType.Error, "The characteristic does not support notifications.");
				}
			}
		}

		/// <summary>
		/// Disable notifications for a characteristic within a list of characteristics specified by its UUID.
		/// </summary>
		/// <param name="uuid"></param>
		public async Task CancelCharacteristicNotifications(string uuid)
		{
			await CancelCharacteristicNotifications(FindCharacteristic(uuid));
		}

		/// <summary>
		/// Disable notifications for a characteristic within a list of characteristics.
		/// </summary>
		/// <param name="characteristic"></param>
		public async Task CancelCharacteristicNotifications(GattCharacteristic characteristic)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				//Find the characteristic in the list:
				int idx = NotifyingCharacteristics.FindIndex(c => c == characteristic);
				if (idx > 0)
				{
					await DisableCharacteristicNotifications(NotifyingCharacteristics[idx]);
					//Dispose of the service:
					//NotifyingCharacteristics[idx]?.Service?.Dispose();
					NotifyingCharacteristics[idx] = null;
					NotifyingCharacteristics.RemoveAt(idx);
				}
			}
		}

		/// <summary>
		/// Disable notifications from a single specified characteristic.
		/// </summary>
		/// <param name="characteristic"></param>
		public async Task DisableCharacteristicNotifications(GattCharacteristic characteristic)
		{
			if (characteristic != null && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				//Unsubscribe to the characteristic changed event:
				characteristic.ValueChanged -= this.CharacteristicValueChanged;
				GattCommunicationStatus status = await characteristic?.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);   //This results in the characteristic being set to null.
				OnStatusUpdate(MsgType.Success, "The server has been instructed to stop notifications.");
			}
		}

		/// <summary>
		/// Disable and dispose of all notifying characteristics.
		/// </summary>
		public async Task DisableCharacteristicNotifications()
		{
			if (NotifyingCharacteristics != null && NotifyingCharacteristics.Count > 0 && CurrentDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
			{
				foreach (var c in NotifyingCharacteristics)
				{
					await DisableCharacteristicNotifications(c);
					c?.Service?.Dispose();
				}
				for (int i = 0; i < NotifyingCharacteristics.Count; i++)
				{
					NotifyingCharacteristics[i] = null;
				}
				NotifyingCharacteristics.Clear();
			}
		}

		public void EnableCharacteristicIndications()
		{

		}

		/// <summary>
		/// Event handler called on BLE notification. Obtains the data as a byte array and propagates the event.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		void CharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			//An indicate or notify reported a characteristic value change:
			var reader = DataReader.FromBuffer(args.CharacteristicValue);
			byte[] input = new byte[reader.UnconsumedBufferLength];
			reader.ReadBytes(input);
			if (characteristicNotificationEvent != null)
			{
				OnCharacteristicNotification(new NotificationEventArgs(MsgType.Data, input));

				Debug.WriteLine("Data from " + CurrentDevice.BluetoothAddress.ToString() + ": " + Encoding.ASCII.GetString(input));
			}
		}
		#endregion Services and Characteristics 
	}

	/// <summary>
	/// Class for storing service information alongside contained characteristics.
	/// </summary>
	public class BLE_Service : IDisposable
	{
		#region Fields
		private bool IsDisposed = false;
		public GattDeviceService Service;
		public List<GattCharacteristic> Characteristics = new List<GattCharacteristic>();
		#endregion

		#region Constructor
		public BLE_Service(GattDeviceService _service)
		{
			Service = _service;
		}

		public BLE_Service(GattDeviceService _service, List<GattCharacteristic> _characteristics)
		{
			Service = _service;
			Characteristics = _characteristics;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!IsDisposed)
			{
				if (disposing)
				{
					//Clear property values:					
					Service?.Session?.Dispose();
					Service = null;

					for (int i = 0; i < Characteristics.Count; i++)
					{
						Characteristics[i] = null;
					}
				}
				IsDisposed = true;
			}
		}
		#endregion
	}

	/// <summary>
	/// System status event arguments.
	/// </summary>
	public class StatusEventArgs : EventArgs
	{
		public string status { get; set; }
		public StatusEventArgs(string _status)
		{
			this.status = _status;
		}
	}

	/// <summary>
	/// Notification event arguments.
	/// </summary>
	public class NotificationEventArgs : EventArgs
	{
		//Immutable reference types:
		public BLEComms.MsgType type { get; set; }
		public byte[] data { get; set; }

		public NotificationEventArgs(BLEComms.MsgType _type, byte[] _data)
		{
			this.type = _type;
			this.data = _data;
		}
	}
}
