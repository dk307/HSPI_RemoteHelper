using System;
using System.Net;

namespace Hspi.Devices
{
    internal abstract class IPAddressableDeviceControl : DeviceControl
    {
        public IPAddress DeviceIP { get; }

        protected IPAddressableDeviceControl(string name, IPAddress deviceIP) :
            base(name)
        {
            DeviceIP = deviceIP;
        }
    }
}