using NullGuard;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class PS3FakeControlDevice : DeviceControl
    {
        public PS3FakeControlDevice(string name,
                                IConnectionProvider connectionProvider) :
            base(name, connectionProvider)
        {
            AddCommand(new DeviceCommand(CommandName.PowerOn, fixedValue: -200));
            AddCommand(new DeviceCommand(CommandName.PowerOff, fixedValue: -199));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -198));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
        }

        public override bool InvalidState => false;

        public override Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to PS3 {Name}"));

            switch (command.Id)
            {
                case CommandName.PowerQuery:
                    UpdateFeedback(FeedbackName.Power, true);
                    break;
            }

            return Task.Delay(0);
        }
    }
}