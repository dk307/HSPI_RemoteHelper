using Hspi.Devices;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class SamsungTVControl : IPAddressableDeviceControl
    {
        public SamsungTVControl(string name, IPAddress deviceIP,
                                PhysicalAddress macAddress,
                                IPAddress wolBroadCastAddress,
                                TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider,
                                AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue)
        {
            this.wolBroadCastAddress = wolBroadCastAddress;
            MacAddress = macAddress;
            AddCommand(new DeviceCommand(CommandName.PowerOn));
            AddCommand(new DeviceCommand(CommandName.PowerOff));
            AddCommand(new DeviceCommand(CommandName.PowerQuery));

            AddCommand(new DeviceCommand(CommandName.VolumeUp));
            AddCommand(new DeviceCommand(CommandName.VolumeDown));
            AddCommand(new DeviceCommand(CommandName.MuteToggle));

            AddCommand(new DeviceCommand(CommandName.SourceSelect));

            AddCommand(new DeviceCommand(CommandName.Menu));
            AddCommand(new DeviceCommand(CommandName.CursorDown));
            AddCommand(new DeviceCommand(CommandName.CursorUp));
            AddCommand(new DeviceCommand(CommandName.CursorRight));
            AddCommand(new DeviceCommand(CommandName.CursorLeft));
            AddCommand(new DeviceCommand(CommandName.Enter));
            AddCommand(new DeviceCommand(CommandName.Exit));
            AddCommand(new DeviceCommand(CommandName.TVAVRInput));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
        }

        public override bool InvalidState => false;
        public PhysicalAddress MacAddress { get; }

        public override async Task Refresh(CancellationToken token)
        {
            await ExecuteCommand(GetCommand(CommandName.PowerQuery), token).ConfigureAwait(false);
        }

        protected override async Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to Samsung TV {Name}"));

            switch (command.Id)
            {
                case CommandName.PowerOn:
                    var task1 = NetworkHelper.SendWolAsync(new IPEndPoint(wolBroadCastAddress, 9), MacAddress, token);
                    var task2 = SendIRCommandCore("Samsung TV - POWER ON", token);
                    await Task.WhenAll(task1, task2).ConfigureAwait(false);
                    break;

                case CommandName.PowerOff:
                    await SendIRCommandCore("Samsung TV - POWER OFF", token).ConfigureAwait(false);
                    break;

                case CommandName.PowerQuery:
                    await UpdatePowerFeedbackState(token).ConfigureAwait(false);
                    break;

                case CommandName.TVAVRInput:
                    await SendIRCommandCore("Samsung TV - INPUT HDMI 4", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorDown:
                    await SendIRCommandCore("Samsung TV - CURSOR DOWN", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorLeft:
                    await SendIRCommandCore("Samsung TV - CURSOR LEFT", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorRight:
                    await SendIRCommandCore("Samsung TV - CURSOR RIGHT", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorUp:
                    await SendIRCommandCore("Samsung TV - CURSOR UP", token).ConfigureAwait(false);
                    break;

                case CommandName.Enter:
                    await SendIRCommandCore("Samsung TV - CURSOR ENTER", token).ConfigureAwait(false);
                    break;

                case CommandName.Exit:
                    await SendIRCommandCore("Samsung TV - EXIT", token).ConfigureAwait(false);
                    break;

                case CommandName.Menu:
                    await SendIRCommandCore("Samsung TV - MENU MAIN", token).ConfigureAwait(false);
                    break;

                case CommandName.VolumeDown:
                    await SendIRCommandCore("Samsung TV - VOLUME DOWN", token).ConfigureAwait(false);
                    break;

                case CommandName.VolumeUp:
                    await SendIRCommandCore("Samsung TV - VOLUME UP", token).ConfigureAwait(false);
                    break;

                case CommandName.MuteToggle:
                    await SendIRCommandCore("Samsung TV - MUTE TOGGLE", token).ConfigureAwait(false);
                    break;

                case CommandName.SourceSelect:
                    await SendIRCommandCore("Samsung TV - MENU HOME", token).ConfigureAwait(false);
                    break;

                default:
                    Trace.WriteLine(Invariant($"Command {command.Id} to Samsung TV {Name} has no handler"));
                    break;
            }
        }

        private async Task<bool> IsPoweredOn(CancellationToken token)
        {
            // TV keeps reponding to Pings for 7s after it has been turned off
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(750);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitAsync(token).ConfigureAwait(false);
        }

        private async Task SendIRCommandCore(string commandId, CancellationToken token)
        {
            Connector.IDeviceCommandHandler connector = ConnectionProvider.GetCommandHandler(DeviceType.IP2IR);
            await connector.HandleCommand(commandId, token).ConfigureAwait(false);
        }

        private async Task UpdatePowerFeedbackState(CancellationToken token)
        {
            await UpdateFeedback(FeedbackName.Power, await IsPoweredOn(token).ConfigureAwait(false), token).ConfigureAwait(false);
        }

        private readonly IPAddress wolBroadCastAddress;
    }
}