using System;
using System.Runtime.Serialization;

namespace Hspi.Devices
{
    [Serializable]
    internal class FeedbackNotFoundException : DeviceException
    {
        public FeedbackNotFoundException()
        {
        }

        public FeedbackNotFoundException(string message) : base(message)
        {
        }

        protected FeedbackNotFoundException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public FeedbackNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}