using Hspi.Connector;

namespace Hspi
{
    internal interface IConnectionProvider
    {
        IDeviceCommandHandler GetCommandHandler(DeviceType deviceType);

        IDeviceFeedbackProvider GetFeedbackProvider(DeviceType deviceType);
    }
}