using Hspi.Connector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using static System.FormattableString;

    internal sealed class GlobalMacros : DeviceControl
    {
        public GlobalMacros(string name, IReadOnlyDictionary<DeviceType, DeviceControlManagerBase> connection) :
            base(name)
        {
            this.connection = connection;
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnNvidiaShield, string.Empty));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOffEverything, string.Empty));

            AddFeedback(new DeviceFeedback(FeedbackName.MacroStatus, TypeCode.String));
        }

        public override bool InvalidState => false;

        public override async Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Executing Macro {command.Id} "));

            UpdateConnectedState(true);
            UpdateStatus(string.Empty);

            switch (command.Id)
            {
                case CommandName.MacroTurnOnNvidiaShield:
                    break;
            }

            await Task.Delay(0);
        }

        private void UpdateStatus(string value)
        {
            UpdateFeedback(FeedbackName.MacroStatus, value);
        }

        private readonly IReadOnlyDictionary<DeviceType, DeviceControlManagerBase> connection;
    }
}