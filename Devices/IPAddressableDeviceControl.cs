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

        public override Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore(command, true, token);
        }

        public abstract Task ExecuteCommandCore(DeviceCommand command, bool canIgnore, CancellationToken token);

        protected static void MacroStopCommandLoop([AllowNull]ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
                cancelSource = null;
            }
        }

        protected void MacroStartCommandLoop(string commandId, TimeSpan commandDelay,
                                             [AllowNull]ref CancellationTokenSource cancelSource)
        {
            MacroStartCommandLoop(GetCommand(commandId), commandDelay, ref cancelSource);
        }

        protected void MacroStartCommandLoop(DeviceCommand command, TimeSpan commandDelay,
                                             [AllowNull]ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
            }
            cancelSource = new CancellationTokenSource();

            var cancelToken = cancelSource.Token;

            StartCommandLoop(command, commandDelay, cancelToken);
        }

        protected void StartCommandLoop(DeviceCommand command, TimeSpan commandDelay, CancellationToken cancelToken)
        {
            Task.Run(async () =>
            {
                var token = default(CancellationToken);
                TimeSpan delay = DefaultCommandDelay.Add(commandDelay);
                do
                {
                    await ExecuteCommandCore(command, false, token).ConfigureAwait(false);
                    token = cancelToken;
                    if (delay != TimeSpan.Zero)
                    {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }
                } while ((!token.IsCancellationRequested));
            });
        }
    }
}