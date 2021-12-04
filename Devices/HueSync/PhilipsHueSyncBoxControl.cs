using InnerCore.Api.HueSync;
using InnerCore.Api.HueSync.Models.Command;
using InnerCore.Api.HueSync.Models.Enum;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class PhilipsHueSyncBoxControl : IPAddressableDeviceControl
    {
        public PhilipsHueSyncBoxControl(string name, IPAddress deviceIP,
                                  string userName,
                                  TimeSpan defaultCommandDelay,
                                  IConnectionProvider connectionProvider,
                                  AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                  AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue)
        {
            this.userName = userName;

            AddCommand(new DeviceCommand(CommandName.AllStatusQuery, fixedValue: -100));
            AddCommand(new DeviceCommand(CommandName.PowerOff, fixedValue: -98));
            AddCommand(new DeviceCommand(CommandName.StartSyncModeVideo, fixedValue: -97));
            AddCommand(new DeviceCommand(CommandName.StartSyncModeGame, fixedValue: -96));
            AddCommand(new DeviceCommand(CommandName.PassThrough, fixedValue: -95));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -94));
            AddCommand(new DeviceCommand(CommandName.RestartDevice, fixedValue: -93));

            AddFeedback(new DeviceFeedback(FeedbackName.SyncActiveStatus, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Input, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.HdmiActive, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Mode, TypeCode.String));
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
            return ExecuteCommand(GetCommand(CommandName.PowerQuery), token);
        }

        protected override async Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            using (var _ = await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Hue sync box {Name} on {DeviceIP}"));

                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                switch (command.Id)
                {
                    case CommandName.PowerOff:
                        {
                            var syncBoxCommand = new ExecutionCommand()
                            {
                                SyncActive = false,
                                Mode = Mode.PowerSave,
                            };
                            await client.ApplyExecutionCommandAsync(syncBoxCommand).ConfigureAwait(false);
                            await UpdateStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.PassThrough:
                        {
                            var syncBoxCommand = new ExecutionCommand()
                            {
                                SyncActive = false,
                                HdmiSource = HdmiSource.Input2,
                                Mode = Mode.Passthrough,
                            };
                            await client.ApplyExecutionCommandAsync(syncBoxCommand).ConfigureAwait(false);
                            await UpdateStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.StartSyncModeVideo:
                        {
                            var syncBoxCommand = new ExecutionCommand()
                            {
                                SyncActive = true,
                                HdmiSource = HdmiSource.Input2,
                                HdmiActive = true,
                                Mode = Mode.Video,
                            };

                            await client.ApplyExecutionCommandAsync(syncBoxCommand).ConfigureAwait(false);
                            await UpdateStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.StartSyncModeGame:
                        {
                            var syncBoxCommand = new ExecutionCommand()
                            {
                                SyncActive = true,
                                HdmiSource = HdmiSource.Input2,
                                HdmiActive = true,
                                Mode = Mode.Game,
                            };
                            await client.ApplyExecutionCommandAsync(syncBoxCommand).ConfigureAwait(false);
                            await UpdateStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.PowerQuery:
                    case CommandName.AllStatusQuery:
                        {
                            await UpdateStatus(token).ConfigureAwait(false);
                        }
                        break;

                    case CommandName.RestartDevice:
                        {
                            var deviceCommand = new InnerCore.Api.HueSync.Models.Command.DeviceCommand()
                            {
                               Action = DeviceAction.Restart,
                            };
                            await client.ApplyDeviceCommandAsync(deviceCommand).ConfigureAwait(false);
                        }
                        break;
                }
            }
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsNetworkOn().ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"Hue Sync Box {Name} on {DeviceIP} not powered On");
            }

            client = new HueSyncBoxClient(DeviceIP.ToString());
            client.Initialize(this.userName);
            await UpdateConnectedState(true, token).ConfigureAwait(false);
        }

        private async Task<bool> IsNetworkOn()
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(1500);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).ConfigureAwait(false);
        }

        private async Task UpdateStatus(CancellationToken token)
        {
            var state = await client.GetStateAsync().ConfigureAwait(false);

            await UpdateFeedback(FeedbackName.SyncActiveStatus, state.Execution.SyncActive, token).ConfigureAwait(false);
            await UpdateFeedback(FeedbackName.Input, state.Execution.HdmiSource?.ToString(), token).ConfigureAwait(false);
            await UpdateFeedback(FeedbackName.HdmiActive, state.Execution.HdmiActive, token).ConfigureAwait(false);
            await UpdateFeedback(FeedbackName.Mode, state.Execution.Mode?.ToString(), token).ConfigureAwait(false);
            await UpdateFeedback(FeedbackName.Power, state.Execution.Mode != Mode.PowerSave, token).ConfigureAwait(false);
        }

        private readonly AsyncLock connectionLock = new AsyncLock();
        private readonly string userName;
        private HueSyncBoxClient client;
    }
}