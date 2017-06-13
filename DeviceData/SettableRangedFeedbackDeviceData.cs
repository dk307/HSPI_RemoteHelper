using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class SettableRangedFeedbackDeviceData : DoubleFeedbackDeviceDataBase
    {
        public SettableRangedFeedbackDeviceData(int? refId, SettableRangedDeviceFeedback feedback) : base(refId)
        {
            this.feedback = feedback;
        }

        public override IList<VSVGPairs.VSPair> StatusPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VSPair>();
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Both)
                {
                    PairType = VSVGPairs.VSVGPairType.Range,
                    RangeEnd = feedback.High,
                    RangeStart = feedback.Low,
                    IncludeValues = true,
                    Render = Enums.CAPIControlType.TextBox_Number,
                    RangeStatusDecimals = feedback.DecimalPlaces,
                });

                return pairs;
            }
        }

        public override async Task HandleCommand(DeviceControl connector, double value, CancellationToken token)
        {
            await connector.ExecuteCommand(new FeedbackValue(feedback, value), token);
        }

        public override bool StatusDevice => false;

        private readonly SettableRangedDeviceFeedback feedback;
    };
}