using HomeSeerAPI;
using NullGuard;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    /// <summary>
    ///  Base class for Child Devices
    /// </summary>
    /// <seealso cref="Hspi.DeviceDataBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class FeedbackDeviceData : DeviceDataBase
    {
        protected FeedbackDeviceData(int? refId)
        {
            if (refId.HasValue)
            {
                RefId = refId.Value;
            }
        }

        public override IList<VSVGPairs.VGPair> GraphicsPairs => new List<VSVGPairs.VGPair>();

        public override int HSDeviceType => 0;

        public override string HSDeviceTypeString => Invariant($"{PluginData.PluginName} Feedback Device");

        public override IList<VSVGPairs.VSPair> StatusPairs => new List<VSVGPairs.VSPair>();

        public override void DeviceCreated(IHSApplication HS, int refId)
        {
            base.DeviceCreated(HS, refId);
            HS.SetDeviceValueByRef(refId, 0D, false);
            HS.set_DeviceInvalidValue(refId, true);
        }

        public abstract void UpdateValue(IHSApplication HS, object value);
    };
}