using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    /// <summary>
    ///  Base class for Root Devices
    /// </summary>
    /// <seealso cref="Hspi.DeviceDataBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class RootDeviceData : DeviceDataBase
    {
        public RootDeviceData()
        {
        }

        public RootDeviceData(int refID)
        {
            RefId = refID;
        }

        public override void DeviceCreated(IHSApplication HS, int refID)
        {
            base.DeviceCreated(HS, refID);
            HS.set_DeviceInvalidValue(refID, false);
            HS.SetDeviceValueByRef(refID, NotConnectedValue, false);
        }

        public IList<VSVGPairs.VSPair> GetStatusPairs(IEnumerable<DeviceCommand> commands)
        {
            var pairs = new List<VSVGPairs.VSPair>();
            int value = -100;
            foreach (var command in commands)
            {
                commandValues.Add(value, command);
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Control)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = value++,
                    ControlUse = ePairControlUse.Not_Specified,
                    Status = command.Id,
                    Render = Enums.CAPIControlType.Single_Text_from_List,
                });
            }

            pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
            {
                PairType = VSVGPairs.VSVGPairType.SingleValue,
                Value = NotConnectedValue,
                Status = "Not-Connected"
            });

            pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
            {
                PairType = VSVGPairs.VSVGPairType.SingleValue,
                Value = ConnectedValue,
                Status = "Connected"
            });

            return pairs;
        }

        private const int NotConnectedValue = 0;
        private const int ConnectedValue = 255;

        public override async Task HandleCommand(DeviceControl connector, double value, CancellationToken token)
        {
            if (commandValues.TryGetValue(value, out var commandId))
            {
                await connector.ExecuteCommand(commandId, token);
            }
        }

        public override bool StatusDevice => false;
        public override IList<VSVGPairs.VGPair> GraphicsPairs => new List<VSVGPairs.VGPair>();
        public override string HSDeviceTypeString => Invariant($"{PluginData.PluginName} Root Device");

        internal void UpdateConnectedState(IHSApplication HS, bool connected)
        {
            HS.SetDeviceValueByRef(RefId, connected ? ConnectedValue : NotConnectedValue, true);
        }

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Plugin.Root;

        public override IList<VSVGPairs.VSPair> StatusPairs => new List<VSVGPairs.VSPair>();

        private readonly IDictionary<double, DeviceCommand> commandValues = new Dictionary<double, DeviceCommand>();
    }
}