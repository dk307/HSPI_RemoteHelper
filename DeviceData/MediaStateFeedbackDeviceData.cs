using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class MediaStateFeedbackDeviceData : DoubleFeedbackDeviceDataBase
    {
        public MediaStateFeedbackDeviceData(int? refId) : base(refId)
        {
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
                        Value = (int)MediaStateDeviceFeedback.State.Stopped,
                        Status = "Stopped",
                    },

                    new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Value = (int)MediaStateDeviceFeedback.State.Playing,
                        Status = "Playing",
                    },
                    
                    new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Value = (int)MediaStateDeviceFeedback.State.Paused,
                        Status = "Paused",
                    },

                    new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                    {
                        PairType = VSVGPairs.VSVGPairType.SingleValue,
                        Value = (int)MediaStateDeviceFeedback.State.Unknown,
                        Status = "Unknown",
                    }
                };
                return pairs;
            }
        }

        public override bool StatusDevice => true;
    };
}