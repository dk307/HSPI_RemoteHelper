using HomeSeerAPI;
using Hspi.Devices;
using Nito.AsyncEx;
using NullGuard;
using System.Threading;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class GlobalMacrosDeviceControlManager : DeviceControlManagerCore
    {
        public GlobalMacrosDeviceControlManager(IHSApplication HS,
                    IConnectionProvider connectionProvider, CancellationToken shutdownToken) :
            base(HS, "Global Macros", DeviceType.GlobalMacros, shutdownToken)
        {
            this.connectionProvider = connectionProvider;
        }

        public override DeviceControl Create(AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                             AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue)
        {
            return new GlobalMacros(Name, connectionProvider, commandQueue, feedbackQueue);
        }

        private readonly IConnectionProvider connectionProvider;
    }
}