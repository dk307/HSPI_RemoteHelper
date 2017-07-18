using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    using System;
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

        public override IList<VSVGPairs.VGPair> GraphicsPairs => new List<VSVGPairs.VGPair>();

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Plugin.Root;

        public override string HSDeviceTypeString => Invariant($"{PluginData.PluginName} Root Device");

        public override bool StatusDevice => false;

        public override IList<VSVGPairs.VSPair> StatusPairs => new List<VSVGPairs.VSPair>();

        public override void DeviceCreated(IHSApplication HS, int refID)
        {
            base.DeviceCreated(HS, refID);
            HS.set_DeviceInvalidValue(refID, false);
        }

        public IList<VSVGPairs.VSPair> GetStatusPairs(IEnumerable<DeviceCommand> commands)
        {
            var pairs = new List<VSVGPairs.VSPair>();
            int value = -100;
            int row = 1;
            int col = 1;
            foreach (var command in commands)
            {
                int statusValue = command.FixedValue ?? value++;
                commandValuesReverse.Add(command.Id, statusValue);
                commandValues.Add(statusValue, command);

                switch (command.Type)
                {
                    case DeviceCommandType.Control:
                        pairs.Add(new VSVGPairs.VSPair(ePairStatusControl.Control)
                        {
                            PairType = VSVGPairs.VSVGPairType.SingleValue,
                            Value = statusValue,
                            ControlUse = ePairControlUse.Not_Specified,
                            Status = command.Id,
                            Render = Enums.CAPIControlType.Button,
                            Render_Location = new Enums.CAPIControlLocation()
                            {
                                Row = row,
                                Column = col++,
                            }
                        });
                        break;

                    case DeviceCommandType.Status:
                        pairs.Add(new VSVGPairs.VSPair(ePairStatusControl.Status)
                        {
                            PairType = VSVGPairs.VSVGPairType.SingleValue,
                            Value = statusValue,
                            Status = command.Id,
                        });
                        break;

                    case DeviceCommandType.Both:
                        pairs.Add(new VSVGPairs.VSPair(ePairStatusControl.Both)
                        {
                            PairType = VSVGPairs.VSVGPairType.SingleValue,
                            Value = statusValue,
                            ControlUse = ePairControlUse.Not_Specified,
                            Status = command.Id,
                            Render = Enums.CAPIControlType.Button,
                            Render_Location = new Enums.CAPIControlLocation()
                            {
                                Row = row,
                                Column = col++,
                            }
                        });
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(commands));
                }

                if (col > 5)
                {
                    col = 1;
                    row++;
                }
            }

            return pairs;
        }

        public override async Task HandleCommand(DeviceControl connector, double value, CancellationToken token)
        {
            if (commandValues.TryGetValue(value, out var commandId))
            {
                await connector.ExecuteCommand(commandId, token).ConfigureAwait(false);
            }
        }

        public void UpdateRootValue(IHSApplication HS, DeviceCommand command)
        {
            if (commandValuesReverse.TryGetValue(command.Id, out var value))
            {
                HS.SetDeviceValueByRef(RefId, value, true);
            }
        }

        private readonly IDictionary<string, double> commandValuesReverse = new Dictionary<string, double>();
        private readonly IDictionary<double, DeviceCommand> commandValues = new Dictionary<double, DeviceCommand>();
    }
}