using HomeSeerAPI;
using NullGuard;
using System.Threading;
using Hspi.Devices;
using System.Collections.Generic;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class GlobalMacrosDeviceControlManager : DeviceControlManagerBase
    {
        public GlobalMacrosDeviceControlManager(IHSApplication HS, ILogger logger,
                    IReadOnlyDictionary<DeviceType, DeviceControlManagerBase> connection, CancellationToken shutdownToken) :
            base(HS, logger, Name, DeviceType.GlobalMacros, shutdownToken)
        {
            this.connection = connection;
        }

        public override DeviceControl Create()
        {
            return new GlobalMacros(Name, connection);
        }

        private const string Name = "Global Macros";
        private readonly IReadOnlyDictionary<DeviceType, DeviceControlManagerBase> connection;
    }
}