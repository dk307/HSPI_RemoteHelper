using HomeSeerAPI;
using NullGuard;

namespace Hspi.DeviceData
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class StringFeedbackDeviceData : FeedbackDeviceData
    {
        public StringFeedbackDeviceData(int? refId) : base(refId)
        {
        }

        public override void UpdateValue(IHSApplication HS, object value)
        {
            HS.set_DeviceInvalidValue(RefId, false);
            HS.SetDeviceString(RefId, (value ?? string.Empty).ToString(), false);
        }

        public override bool StatusDevice => true;
    }
}