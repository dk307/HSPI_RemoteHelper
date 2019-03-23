using static System.FormattableString;

namespace Hspi.Devices
{
    internal class ADBShellLaunchPackageCommand : DeviceCommand
    {
        public ADBShellLaunchPackageCommand(string id, string packageName, string activityName)
           : base(id, Invariant($@"am start -n {packageName}/{activityName}"))
        {
        }

        public ADBShellLaunchPackageCommand(string id, string packageName)
            : base(id, Invariant($@"monkey -p {packageName} -c android.intent.category.LAUNCHER 1"))
        {
        }
    }
}