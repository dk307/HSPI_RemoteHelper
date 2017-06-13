using System;
using System.Runtime.Serialization;

namespace Hspi.Devices
{
    [Serializable]
    internal class SettableRangedDeviceFeedback : DeviceFeedback
    {
        public SettableRangedDeviceFeedback(string id, double low, double high, int decimalPlaces) :
            base(id, TypeCode.Double)
        {
            DecimalPlaces = decimalPlaces;
            High = high;
            Low = low;
        }

        protected SettableRangedDeviceFeedback(SerializationInfo info, StreamingContext context) :
            base(info, context)
        {
            Low = info.GetDouble(nameof(Low));
            High = info.GetDouble(nameof(High));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Low), Low);
            info.AddValue(nameof(High), High);
        }

        public double Low { get; }
        public double High { get; }
        public int DecimalPlaces { get; }
    }
}