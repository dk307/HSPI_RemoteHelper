using NullGuard;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ADBShellCharCommand : DeviceCommand
    {
        public ADBShellCharCommand(string id, char key, int? fixedValue = null)
            : base(id, Invariant($"input text \"{key}\""), fixedValue: fixedValue)
        {
        }
    }
}