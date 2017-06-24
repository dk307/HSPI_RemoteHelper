using NullGuard;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class IPAddressableDeviceControl : DeviceControl
    {
        protected IPAddressableDeviceControl(string name, IPAddress deviceIP, TimeSpan defaultCommandDelay) :
            base(name)
        {
            DefaultCommandDelay = defaultCommandDelay;
            DeviceIP = deviceIP;
        }

        public TimeSpan DefaultCommandDelay { get; }

        public IPAddress DeviceIP { get; }

        protected static void MacroStopCommandLoop(ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
                cancelSource = null;
            }
        }

        protected void MacroStartCommandLoop(string commandId, TimeSpan commandDelay,
                                             ref CancellationTokenSource cancelSource)
        {
            MacroStartCommandLoop(GetCommand(commandId), commandDelay, ref cancelSource);
        }

        protected void MacroStartCommandLoop(DeviceCommand command, TimeSpan commandDelay,
                                             ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
            }
            cancelSource = new CancellationTokenSource();

            var token = cancelSource.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await ExecuteCommand(command, token).ConfigureAwait(false);
                    await Task.Delay(DefaultCommandDelay.Add(commandDelay), token).ConfigureAwait(false);
                }
            });
        }
    }
}