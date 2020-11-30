using Nito.AsyncEx;
using NullGuard;
using Q42.HueApi;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class PhilipsHueControl : IPAddressableDeviceControl
    {
        public PhilipsHueControl(string name, IPAddress deviceIP,
                                  string userName,
                                  IEnumerable<string> devices,
                                  TimeSpan defaultCommandDelay,
                                  IConnectionProvider connectionProvider,
                                  AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                  AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue)
        {
            this.userName = userName;
            this.devices = devices.ToImmutableList();

            AddCommand(new DeviceCommand(CommandName.PowerOff, fixedValue: -96));
            AddCommand(new DeviceCommand(CommandName.PowerOn, fixedValue: -95));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -94));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
        }

        public override bool InvalidState
        {
            get
            {
                return false;
            }
        }

        public override Task Refresh(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override async Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            using (var _ = await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Hue {Name} on {DeviceIP}"));

                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                switch (command.Id)
                {
                    case CommandName.PowerOff:
                        {
                            var hueCommand = new LightCommand();
                            hueCommand.TurnOff();
                            hueCommand.Brightness = 100;
                            await client.SendCommandAsync(hueCommand, this.devices).ConfigureAwait(false);
                            await UpdatePowerStatus(token).ConfigureAwait(false);
                        }

                        break;

                    case CommandName.PowerOn:
                        {
                            var hueCommand = new LightCommand();
                            hueCommand.TurnOn();
                            await client.SendCommandAsync(hueCommand, this.devices).ConfigureAwait(false);
                            await UpdatePowerStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.PowerQuery:
                        {
                            await UpdatePowerStatus(token).ConfigureAwait(false);
                        }
                        break;
                }
            }
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsNetworkOn().ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"Hue {Name} on {DeviceIP} not powered On");
            }

            client = new LocalHueClient(DeviceIP.ToString());
            client.Initialize(this.userName);
            await UpdateConnectedState(true, token).ConfigureAwait(false);
        }

        private async Task<bool> IsNetworkOn()
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).ConfigureAwait(false);
        }

        private async Task UpdatePowerStatus(CancellationToken token)
        {
            bool isOff = false;
            foreach (var device in this.devices)
            {
                var light = await client.GetLightAsync(device).ConfigureAwait(false);
                if (!light.State.On)
                {
                    // all need to be off
                    isOff = true;
                    break;
                }
                token.ThrowIfCancellationRequested();
            }

            await UpdateFeedback(FeedbackName.Power, !isOff, token).ConfigureAwait(false);
        }

        private readonly AsyncLock connectionLock = new AsyncLock();
        private readonly IReadOnlyList<string> devices;
        private readonly string userName;
        private LocalHueClient client;
    }
}