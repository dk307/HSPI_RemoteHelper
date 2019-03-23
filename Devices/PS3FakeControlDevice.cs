using NullGuard;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using Nito.AsyncEx;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class PS3FakeControlDevice : DeviceControl
    {
        public PS3FakeControlDevice(string name,
                                IConnectionProvider connectionProvider,
                                AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, connectionProvider, commandQueue, feedbackQueue)
        {
            AddCommand(new DeviceCommand(CommandName.PowerOn, fixedValue: -200));
            AddCommand(new DeviceCommand(CommandName.PowerOff, fixedValue: -199));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -198));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
        }

        public override bool InvalidState => false;

        public override async Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to PS3 {Name}"));

            switch (command.Id)
            {
                case CommandName.PowerQuery:
                    await UpdateFeedback(FeedbackName.Power, true, token).ConfigureAwait(false);
                    break;
            }
        }
    }
}