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
                    IReadOnlyDictionary<DeviceType, DeviceControlManager> connection, CancellationToken shutdownToken) :
            base(HS, logger, "Global Macros", DeviceType.GlobalMacros, shutdownToken)
        {
            this.connections = connection;
        }

        public override DeviceControl Create()
        {
            return new GlobalMacros(Name, connections);
        }

        private readonly IReadOnlyDictionary<DeviceType, DeviceControlManager> connections;
    }
}