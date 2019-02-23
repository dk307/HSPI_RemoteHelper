using HomeSeerAPI;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Hspi.DeviceData
{
    using Hspi.Utils;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class BoolFeedbackDeviceData : FeedbackDeviceData
    {
        public BoolFeedbackDeviceData(int? refId) : base(refId)
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
                    var booleanValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    HS.set_DeviceInvalidValue(RefId, false);
                    HS.SetDeviceValueByRef(RefId, booleanValue ? OnValue : OffValue, true);
                }
                catch (Exception ex)
                {
                    HS.set_DeviceInvalidValue(RefId, true);
                    Trace.WriteLine(Invariant($"Failed to update {RefId} with {value} with Error:{ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        public override IList<VSVGPairs.VSPair> StatusPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VSPair>
                {
                    new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Value = OffValue,
                        Status = "Off",
                    },

                    new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Value = OnValue,
                        Status = "On",
                    }
                };
                return pairs;
            }
        }

        public override IList<VSVGPairs.VGPair> GraphicsPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VGPair>
                {
                    new VSVGPairs.VGPair()
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Graphic = Path.Combine(PluginData.HSImagesPathRoot, "on.gif"),
                        Set_Value = OnValue
                    },

                    new VSVGPairs.VGPair()
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Graphic = Path.Combine(PluginData.HSImagesPathRoot, "off.gif"),
                        Set_Value = OffValue
                    }
                };

                return pairs;
            }
        }

        public override bool StatusDevice => true;

        private const int OnValue = 100;
        private const int OffValue = 0;
    }
}