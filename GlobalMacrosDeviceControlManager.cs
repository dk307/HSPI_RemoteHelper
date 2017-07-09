using HomeSeerAPI;
using NullGuard;
using System.Threading;
using Hspi.Devices;
using System.Collections.Generic;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class GlobalMacrosDeviceControlManager : DeviceControlManagerCore
    {
        public GlobalMacrosDeviceControlManager(IHSApplication HS, ILogger logger,
                    IConnectionProvider connectionProvider, CancellationToken shutdownToken) :
            base(HS, logger, "Global Macros", DeviceType.GlobalMacros, shutdownToken)
        {
            this.connectionProvider = connectionProvider;
        }

        public override DeviceControl Create()
        {
            return new GlobalMacros(Name, connectionProvider);
        }

        private readonly IConnectionProvider connectionProvider;
    }
}