using HomeSeerAPI;
using Hspi.DeviceData;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Hspi.Connector
{
    using Hspi.Devices;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceControlManagerBase : IDisposable
    {
        public string Name { get; }

        public DeviceControlManagerBase(IHSApplication HS, ILogger logger, string name,
                                        DeviceType deviceType, CancellationToken shutdownToken)
        {
            Name = name;
            DeviceType = deviceType;
            this.HS = HS;
            this.logger = logger;
            rootDeviceData = new DeviceRootDeviceManager(name, deviceType, this.HS, logger);
            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, instanceCancellationSource.Token);
        }

        public void Start()
        {
            Task.Factory.StartNew(UpdateDevices, ShutdownToken, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
        }

        private CancellationToken ShutdownToken => combinedCancellationSource.Token;
        public DeviceType DeviceType { get; }

        public void Cancel()
        {
            instanceCancellationSource.Cancel();
        }

        public abstract DeviceControl Create();

        public void Dispose()
        {
            Dispose(true);
        }

        public object GetFeedbackValue(string feedbackName)
        {
            if (feedbackValues.TryGetValue(feedbackName, out var value))
            {
                return value;
            }

            return null;
        }

        public async Task HandleCommand([AllowNull]DeviceIdentifier deviceIdentifier, double value)
        {
            await deviceActionLock.WaitAsync(ShutdownToken);
            try
            {
                CheckConnection();

                await rootDeviceData.HandleCommand(deviceIdentifier, connector, value, ShutdownToken);
            }
            finally
            {
                deviceActionLock.Release();
            }
        }

        public async Task HandleCommand(string commandId, CancellationToken token)
        {
            var finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;

            await deviceActionLock.WaitAsync(finalToken);
            try
            {
                CheckConnection();

                var command = connector.GetCommand(commandId);
                await connector.ExecuteCommand(command, finalToken);
            }
            finally
            {
                deviceActionLock.Release();
            }
        }

        public async Task HandleCommand(string feedbackName, object value, CancellationToken token)
        {
            var finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;

            await deviceActionLock.WaitAsync(finalToken);
            try
            {
                CheckConnection();

                var feedback = connector.GetFeedback(feedbackName);
                await connector.ExecuteCommand(new FeedbackValue(feedback, value), finalToken);
            }
            finally
            {
                deviceActionLock.Release();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    instanceCancellationSource.Cancel();
                    instanceCancellationSource.Dispose();
                    combinedCancellationSource.Dispose();
                    changedFeedbacks.Dispose();
                    changedCommands.Dispose();

                    DisposeConnector();
                    deviceActionLock.Dispose();
                }

                disposedValue = true;
            }
        }

        private void CheckConnection()
        {
            if (connector != null)
            {
                if (connector.InvalidState)
                {
                    DestroyConnection();
                }
            }

            if (connector == null)
            {
                connector = Create();
                connector.FeedbackChanged += Connector_FeedbackChanged;
                connector.CommandChanged += Connector_CommandChanged;
            }
        }

        private void Connector_CommandChanged(object sender, DeviceCommand command)
        {
            changedCommands.Add(command);
        }

        private void Connector_FeedbackChanged(object sender, FeedbackValue changedFeedback)
        {
            changedFeedbacks.Add(changedFeedback);
            feedbackValues[changedFeedback.Feedback.Id] = changedFeedback.Value;
        }

        private void DestroyConnection()
        {
            try
            {
                DisposeConnector();
            }
            catch (Exception) { }
        }

        private void DisposeConnector()
        {
            if (connector != null)
            {
                connector.CommandChanged -= Connector_CommandChanged;
                connector.FeedbackChanged -= Connector_FeedbackChanged;
                connector.Dispose();
                connector = null;

                changedCommands.Add(DeviceControl.NotConnectedCommand);
            }
        }

        private async Task UpdateDevices()
        {
            await deviceActionLock.WaitAsync(ShutdownToken).ConfigureAwait(false);
            try
            {
                using (var deviceControl = Create())
                {
                    rootDeviceData.CreateOrUpdateDevices(deviceControl.Commands,
                                                         deviceControl.Feedbacks);
                }

                changedCommands.Add(DeviceControl.NotConnectedCommand);
            }
            finally
            {
                deviceActionLock.Release();
            }

            Task.
            Task.Factory.StartNew(ProcessFeedbacks, ShutdownToken, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
            Task.Factory.StartNew(ProcessCommands, ShutdownToken, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
        }

        private void ProcessFeedbacks()
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                if (changedFeedbacks.TryTake(out var feedbackData, -1, ShutdownToken))
                {
                    try
                    {
                        rootDeviceData.ProcessFeedback(feedbackData);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(Invariant($"Failed to update Feedback {feedbackData.Feedback.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
                    }
                }
            }
        }

        private void ProcessCommands()
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                if (changedCommands.TryTake(out var command, -1, ShutdownToken))
                {
                    try
                    {
                        rootDeviceData.ProcessCommand(command);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(Invariant($"Failed to update Command {command.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
                    }
                }
            }
        }

        private readonly BlockingCollection<DeviceCommand> changedCommands = new BlockingCollection<DeviceCommand>();
        private readonly BlockingCollection<FeedbackValue> changedFeedbacks = new BlockingCollection<FeedbackValue>();
        private readonly CancellationTokenSource combinedCancellationSource;
        private readonly SemaphoreSlim deviceActionLock = new SemaphoreSlim(1);
        private readonly ConcurrentDictionary<string, object> feedbackValues = new ConcurrentDictionary<string, object>();
        private readonly IHSApplication HS;
        private readonly CancellationTokenSource instanceCancellationSource = new CancellationTokenSource();
        private readonly ILogger logger;
        private readonly DeviceRootDeviceManager rootDeviceData;
        private DeviceControl connector;

        private bool disposedValue = false; // To detect redundant calls
    }
}