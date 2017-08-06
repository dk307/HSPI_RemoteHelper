using Hspi.DeviceData;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Connector
{
    internal interface IDeviceCommandHandler
    {
        string Name { get; }
        TimeSpan DefaultCommandDelay { get; }
        TimeSpan PowerOnDelay { get; }
        DeviceType DeviceType { get; }

        Task HandleCommand(DeviceIdentifier deviceIdentifier, double value);

        Task HandleCommand(string commandId, CancellationToken token);

        Task HandleCommand(string feedbackName, object value, CancellationToken token);
    }

    internal interface IDeviceFeedbackProvider
    {
        string Name { get; }

        object GetFeedbackValue(string feedbackName);
    }
}