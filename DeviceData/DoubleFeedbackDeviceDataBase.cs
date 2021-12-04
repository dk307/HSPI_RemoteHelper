using HomeSeerAPI;
using Hspi.Utils;
using NullGuard;
using System;
using System.Diagnostics;
using System.Globalization;
using static System.FormattableString;

namespace Hspi.DeviceData
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DoubleFeedbackDeviceDataBase : FeedbackDeviceData
    {
        protected DoubleFeedbackDeviceDataBase(int? refId) : base(refId)
        {
        }

        public override void UpdateValue(IHSApplication HS, object value)
        {
            if (value == null)
            {
                HS.set_DeviceInvalidValue(RefId, true);
            }
            else
            {
                try
                {
                    double doubleValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    HS.set_DeviceInvalidValue(RefId, false);
                    HS.SetDeviceValueByRef(RefId, doubleValue, true);
                }
                catch (Exception ex)
                {
                    HS.set_DeviceInvalidValue(RefId, true);
                    Trace.WriteLine(Invariant($"Failed to update {RefId} with {value} with Error:{ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }
    }
}