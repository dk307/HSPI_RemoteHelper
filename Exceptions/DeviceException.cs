using System;
using System.Runtime.Serialization;

namespace Hspi.Devices
{
    [Serializable]
    public class DeviceException : Exception
    {
        public DeviceException()
        {
        }

        public DeviceException(string message) : base(message)
        {
        }

        public DeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DeviceException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}