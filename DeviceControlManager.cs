using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System;
using System.Threading;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceControlManager : DeviceControlManagerCore, IDeviceCommandHandler
    {
        public DeviceControlManager(IHSApplication HS,
                                    DeviceControlConfig deviceConfig,
                                    IConnectionProvider connectionProvider,
                                    CancellationToken shutdownToken) :
            base(HS, deviceConfig.Name, deviceConfig.DeviceType, shutdownToken)
        {
            this.connectionProvider = connectionProvider;
            DeviceConfig = deviceConfig;
        }

        public DeviceControlConfig DeviceConfig { get; }
        public TimeSpan DefaultCommandDelay => DeviceConfig.DefaultCommandDelay;
        public TimeSpan PowerOnDelay => DeviceConfig.PowerOnDelay;

        public override DeviceControl Create()
        {
            return DeviceConfig.Create(connectionProvider);
        }

        private readonly IConnectionProvider connectionProvider;
    }
}