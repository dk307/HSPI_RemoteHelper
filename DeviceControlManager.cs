using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System;
using System.Threading;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceControlManager : DeviceControlManagerBase
    {
        public DeviceControlManager(IHSApplication HS, DeviceControlConfig deviceConfig, ILogger logger, CancellationToken shutdownToken) :
            base(HS, logger, deviceConfig.Name, deviceConfig.DeviceType, shutdownToken)
        {
            DeviceConfig = deviceConfig;
        }

        public TimeSpan PowerOnDelay => DeviceConfig.PowerOnDelay;
        public TimeSpan DefaultCommandDelay => DeviceConfig.DefaultCommandDelay;

        public DeviceControlConfig DeviceConfig { get; }

        public override DeviceControl Create()
        {
            return DeviceConfig.Create();
        }
    }
}