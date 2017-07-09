using NullGuard;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using System.Collections.Generic;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class IPAddressableDeviceControl : DeviceControl
    {
        protected IPAddressableDeviceControl(string name, IPAddress deviceIP,
                                             TimeSpan defaultCommandDelay,
                                             IConnectionProvider connectionProvider,
                                             IList<OutofOrderCommandDetector> outofCommandDetectors = null) :
            base(name, connectionProvider)
        {
            DefaultCommandDelay = defaultCommandDelay;
            DeviceIP = deviceIP;
            this.outofCommandDetectors = outofCommandDetectors ?? new List<OutofOrderCommandDetector>();
        }

        public TimeSpan DefaultCommandDelay { get; }

        public IPAddress DeviceIP { get; }

        public override Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore(command, true, token);
        }

        public Task ExecuteCommandCore(DeviceCommand command, bool canIgnore, CancellationToken token)
        {
            if (canIgnore && ShouldIgnoreCommand(command.Id))
            {
                Trace.WriteLine(Invariant($"Ignoring Command for IP2IR {Name} {command.Id} as it is out of order"));
                return Task.FromResult(true);
            }

            return ExecuteCommandCore(command, token);
        }

        protected static void MacroStopCommandLoop([AllowNull]ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
                cancelSource = null;
            }
        }

        protected abstract Task ExecuteCommandCore(DeviceCommand command, CancellationToken token);

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

        private bool ShouldIgnoreCommand(string commandId)
        {
            foreach (var outofCommandDetector in outofCommandDetectors)
            {
                if (outofCommandDetector.ShouldIgnore(commandId))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly IList<OutofOrderCommandDetector> outofCommandDetectors;
    }
}