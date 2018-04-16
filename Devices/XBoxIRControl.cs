using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class XBoxIRControl : IPAddressableDeviceControl
    {
        public XBoxIRControl(string name, IPAddress deviceIP,
                             TimeSpan defaultCommandDelay,
                             IConnectionProvider connectionProvider) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, xboxOneOutofCommandDetectors)
        {
            AddCommand(new DeviceCommand(CommandName.PowerOn, fixedValue: -200));
            AddCommand(new DeviceCommand(CommandName.PowerOff, fixedValue: -199));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -198));

            AddCommand(new DeviceCommand(CommandName.CursorDown));
            AddCommand(new DeviceCommand(CommandName.CursorLeft));
            AddCommand(new DeviceCommand(CommandName.CursorRight));
            AddCommand(new DeviceCommand(CommandName.CursorUp));

            AddCommand(new DeviceCommand(CommandName.CursorUpEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorUpEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventUp));
            AddCommand(new DeviceCommand(CommandName.MediaPlayPause));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.String));
        }

        public override bool InvalidState => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cursorCancelLoopSource?.Cancel();
            }
            base.Dispose(disposing);
        }

        protected override Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore2(command, token);
        }

        private async Task ExecuteCommandCore2(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to  XBox {Name} on {DeviceIP}"));

            switch (command.Id)
            {
                case CommandName.CursorDownEventDown:
                    MacroStartCommandLoop(CommandName.CursorDown);
                    break;

                case CommandName.CursorLeftEventDown:
                    MacroStartCommandLoop(CommandName.CursorLeft);
                    break;

                case CommandName.CursorRightEventDown:
                    MacroStartCommandLoop(CommandName.CursorRight);
                    break;

                case CommandName.CursorUpEventDown:
                    MacroStartCommandLoop(CommandName.CursorUp);
                    break;

                case CommandName.CursorLeftEventUp:
                case CommandName.CursorRightEventUp:
                case CommandName.CursorUpEventUp:
                case CommandName.CursorDownEventUp:
                    MacroStopCommandLoop(ref cursorCancelLoopSource);
                    break;

                case CommandName.CursorLeft:
                    await SendCommandCore("XBox One - CURSOR LEFT", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorRight:
                    await SendCommandCore("XBox One - CURSOR RIGHT", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorUp:
                    await SendCommandCore("XBox One - CURSOR UP", token).ConfigureAwait(false);
                    break;

                case CommandName.CursorDown:
                    await SendCommandCore("XBox One - CURSOR DOWN", token).ConfigureAwait(false);
                    break;

                case CommandName.PowerOn:
                    await SendCommandCore("XBox One - POWER ON", token).ConfigureAwait(false);
                    break;

                case CommandName.PowerOff:
                    await SendCommandCore("XBox One - POWER OFF", token).ConfigureAwait(false);
                    break;

                case CommandName.PowerQuery:
                    UpdateFeedback(FeedbackName.Power, await IsPoweredOn(token).ConfigureAwait(false));
                    break;

                case CommandName.MediaPlayPause:
                    await SendCommandCore("XBox One - PLAY PAUSE TOGGLE", token).ConfigureAwait(false);
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }
        }

        private async Task<bool> IsPoweredOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(750);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitAsync(token).ConfigureAwait(false);
        }

        private void MacroStartCommandLoop(string commandId)
        {
            MacroStartCommandLoop(commandId, ref cursorCancelLoopSource);
        }

        private async Task SendCommandCore(string commandId, CancellationToken token)
        {
            var connector = ConnectionProvider.GetCommandHandler(DeviceType.IP2IR);
            await connector.HandleCommand(commandId, token).ConfigureAwait(false);
        }

        private static readonly List<OutofOrderCommandDetector> xboxOneOutofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.CursorDownEventDown, CommandName.CursorDownEventUp),
            new OutofOrderCommandDetector(CommandName.CursorUpEventDown, CommandName.CursorUpEventUp),
            new OutofOrderCommandDetector(CommandName.CursorRightEventDown, CommandName.CursorRightEventUp),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventDown, CommandName.CursorLeftEventUp),
        };

        private CancellationTokenSource cursorCancelLoopSource;
    }
}