using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiDeviceConnect
{
	public partial class BLEComms
	{
		//UUIDs:
		public const int SHORT_UUID_LENGTH = 4;
		public const string COMMAND_UUID = "da9e0002-0000-1000-8000-00805f9b34fb";
		public const string DATA_REQUEST_UUID = "da9e0003-0000-1000-8000-00805f9b34fb";
		public const string DATA_STREAM_UUID = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";
		public const string DEVICE_NAME_SHORT_UUID = "2a00";
		public const string BATTERY_LEVEL_SHORT_UUID = "2a19";
		public const string MODEL_NUMBER_SHORT_UUID = "2a24";
		public const string SERIAL_NUMBER_SHORT_UUID = "2a25";
		public const string FIRMWARE_REVISION_NUMBER_SHORT_UUID = "2a26";
		public const string SOFTWARE_REVISION_NUMBER_SHORT_UUID = "2a28";
		public const string MANUFACTURER_SHORT_UUID = "2a29";

		public enum MsgType
		{
			Error = -1,
			Warning = -2,
			Empty = 0,
			Status = 1,
			Success = 2,
			Failed = 3,
			Data = 4
		}

		public enum ConnectionStatus
		{
			Disconnected,
			Connected,
			Timeout,
			Paired,
			Unpaired
		}

		public enum Result : int
		{
			DISCOVERY_IN_PROGRESS,
			DEVICE_DISCOVERED,
			DISCOVERY_STOPPED,
			DEVICE_NOT_FOUND,
			BLE_DEVICE_INFO_NOT_FOUND,
			DEVICE_ALREADY_PAIRED,
			DEVICE_CANNOT_PAIR,
			DEVICE_CANNOT_DISCONNECT,
			DEVICE_ATTEMPTING_RECONNECT,
			MAX_CONN_ATTEMPTS_REACHED,
			CHAR_READ_NOT_SUPPORTED,
			CHAR_WRITE_SUCCESS,
			CHAR_WRITE_ERROR,
			CHAR_WRITE_NOT_SUPPORTED,
			CHAR_CCCD_UPDATED,
			CHAR_CCCD_ERROR,
			CHAR_NOTIFY_NOT_SUPPORTED,
			CHAR_NOTIFY_STOPPED,
			CHAR_NOTIFY_RECEIVED,
			CHAR_INDICATE_NOT_SUPPORTED,
			INPUT_FORMAT_ERROR,
			SIGN_ERROR
		}

		//Data states:
		public const byte DATA_TYPE_BYTE = 1;
		public const byte DATA_START_BYTE = 2;
		public const byte TEXT_DATA = 1;
		public const byte GENERAL_NUMERICAL_DATA = 2;
		public const byte SENSOR_DATA = 3;
		public const byte MESSAGE_READ = 1;
		public const byte MESSAGE_WRITE = 2;

		//Connection parameters:
		public const int QUICK_SCAN_TIMEOUT = 10;
		public const int SLOW_SCAN_TIMEOUT = 60;
	}
}
