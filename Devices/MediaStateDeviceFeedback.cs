using System;
using System.Runtime.Serialization;

namespace Hspi.Devices
{
    [Serializable]
    internal class MediaStateDeviceFeedback : DeviceFeedback
    {
        public enum State
        {
            Playing = 100,
            Paused = 101,
            Stopped = 102,
            Unknown = 103,
        }

        public MediaStateDeviceFeedback(string id) : base(id, TypeCode.Double)
        {
        }

        protected MediaStateDeviceFeedback(SerializationInfo info, StreamingContext context) :
            base(info, context)
        {
        }
    }
}