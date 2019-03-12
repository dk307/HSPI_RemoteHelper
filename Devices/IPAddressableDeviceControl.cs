using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class IPAddressableDeviceControl : DeviceControl
    {
        protected IPAddressableDeviceControl(string name, IPAddress deviceIP,
                                             TimeSpan defaultCommandDelay,
                                             IConnectionProvider connectionProvider,
                                             AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                             AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue,
                                             IList<OutofOrderCommandDetector> outofCommandDetectors = null) :
            base(name, connectionProvider, commandQueue, feedbackQueue)
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
                return Task.CompletedTask;
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

        protected void MacroStartCommandLoop(string commandId,
                                             [AllowNull]ref CancellationTokenSource cancelSource)
        {
            MacroStartCommandLoop(GetCommand(commandId), ref cancelSource);
        }

        protected void MacroStartCommandLoop(DeviceCommand command,
                                             [AllowNull]ref CancellationTokenSource cancelSource)
        {
            if (cancelSource != null)
            {
                cancelSource.Cancel();
            }
            cancelSource = new CancellationTokenSource();

            CancellationToken cancelToken = cancelSource.Token;

            StartCommandLoop(command, DefaultCommandDelay, cancelToken);
        }

        protected void StartCommandLoop(DeviceCommand command, TimeSpan commandDelay, CancellationToken cancelToken)
        {
            Task.Run(async () =>
            {
                CancellationToken token = default(CancellationToken);
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
            foreach (OutofOrderCommandDetector outofCommandDetector in outofCommandDetectors)
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