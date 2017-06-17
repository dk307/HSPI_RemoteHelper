using HomeSeerAPI;
using Hspi.DeviceData;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Connector
{
    using Hspi.Devices;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceControlManager : DeviceControlManagerBase
    {
        public DeviceControlManager(IHSApplication HS, DeviceControlConfig deviceConfig, ILogger logger, CancellationToken shutdownToken) :
            base(HS, logger, deviceConfig.Name, deviceConfig.DeviceType, shutdownToken)
        {
            DeviceConfig = deviceConfig;
        }

        public DeviceControlConfig DeviceConfig { get; }

        public override DeviceControl Create()
        {
            return DeviceConfig.Create();
        }
    }
}