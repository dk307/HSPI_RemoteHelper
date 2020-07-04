using System;
using System.Runtime.Serialization;

namespace Hspi.Devices
{
    [Serializable]
    internal class DevicePoweredOffException : DeviceException
    {
        public DevicePoweredOffException(string message) : base(message)
        {
        }

        protected DevicePoweredOffException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public DevicePoweredOffException()
        {
        }

        public DevicePoweredOffException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}