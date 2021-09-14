using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiDeviceConnect
{
    partial class BLEDevice
    {
        public const UInt16 START_DEVICE = 0x3001;
        public const UInt16 STOP_LOGGING = 0x3000;
        public const UInt16 STOP_IMU = 0x30FF;
        public const UInt16 DISABLE_SD_LOGGING = 0x3200;
        public const UInt16 ENABLE_SD_LOGGING = 0x3201;
        public const UInt16 TOGGLE_BLE_LOGGING = 0x3401;
        public const UInt16 TOGGLE_SERIAL_LOGGING = 0x3601;
        public const UInt16 ENABLE_USB_MOUNT = 0x3801;

        public const int TOTAL_NUMBER_OF_DEVICES = 3;
    }
}
