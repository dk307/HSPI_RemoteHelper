using NullGuard;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ADBShellKeyEventCommand : DeviceCommand
    {
        public ADBShellKeyEventCommand(string id, AdbShellKeys key, int? fixedValue = null)
            : base(id, Invariant($@"input keyevent {(int)key}"), fixedValue: fixedValue)
        {
        }
    }
}