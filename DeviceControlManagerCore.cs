using HomeSeerAPI;
using Hspi.DeviceData;
using Hspi.Devices;
using Hspi.Utils;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceControlManagerCore : IDisposable, IDeviceFeedbackProvider
    {
        protected DeviceControlManagerCore(IHSApplication HS, string name,
                                        DeviceType deviceType, CancellationToken shutdownToken)
        {
            Name = name;
            DeviceType = deviceType;
            this.HS = HS;
            rootDeviceData = new DeviceRootDeviceManager(name, deviceType, this.HS);
            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, instanceCancellationSource.Token);
        }

        public DeviceType DeviceType { get; }
        public string Name { get; }
        private CancellationToken ShutdownToken => combinedCancellationSource.Token;

        public void Cancel()
        {
            instanceCancellationSource.Cancel();
        }

        public abstract DeviceControl Create(AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                             AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue);

        public void Dispose()
        {
            Dispose(true);
        }

        public object GetFeedbackValue(string feedbackName)
        {
            if (feedbackValues.TryGetValue(feedbackName, out object value))
            {
                return value;
            }

            return null;
        }

        public async Task HandleCommand([AllowNull]DeviceIdentifier deviceIdentifier, double value, CancellationToken token)
        {
            using (await deviceActionLock.LockAsync(token).ConfigureAwait(false))
            {
                CheckConnection();
                await rootDeviceData.HandleCommand(deviceIdentifier, connector, value, token).ConfigureAwait(false);
            }
        }

        public async Task HandleCommand(string commandId, CancellationToken token)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            var finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;
#pragma warning restore CA2000 // Dispose objects before losing scope

            using (await deviceActionLock.LockAsync(finalToken).ConfigureAwait(false))
            {
                CheckConnection();

                DeviceCommand command = connector.GetCommand(commandId);
                await connector.ExecuteCommand(command, finalToken).ConfigureAwait(false);
            }
        }

        public async Task HandleCommand(string feedbackName, object value, CancellationToken token)
        {
            CancellationToken finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;

            using (await deviceActionLock.LockAsync(finalToken).ConfigureAwait(false))
            {
                CheckConnection();
                DeviceFeedback feedback = connector.GetFeedback(feedbackName);
                await connector.ExecuteCommand(new FeedbackValue(feedback, value), finalToken).ConfigureAwait(false);
            }
        }

        public void Start()
        {
            MyTaskHelper.StartAsyncWithErrorChecking(Invariant($"{Name} StartAsync"),
                                                  StartAsync,
                                                  ShutdownToken);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "instanceCancellationSource")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    instanceCancellationSource.Cancel();
                    DisposeConnector();
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
                connector = Create(changedCommands, changedFeedbacks);
            }
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
                connector.Dispose();
                changedCommands.Enqueue(DeviceControl.NotConnectedCommand);
            }
        }

        private async Task ProcessCommands()
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                var command = await changedCommands.DequeueAsync(ShutdownToken).ConfigureAwait(false);
                try
                {
                    rootDeviceData.ProcessCommand(command);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(Invariant($"{Name} failed to update Command {command.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        private async Task ProcessFeedbacks()
        {
            while (!ShutdownToken.IsCancellationRequested)
            {
                var feedbackData = await changedFeedbacks.DequeueAsync(ShutdownToken).ConfigureAwait(false);

                try
                {
                    Trace.WriteLine(Invariant($"{Name} processing feedback {feedbackData.Feedback.Id} on {DeviceType}"));
                    feedbackValues[feedbackData.Feedback.Id] = feedbackData.Value;
                    rootDeviceData.ProcessFeedback(feedbackData);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(Invariant($"{Name} failed to update Feedback {feedbackData.Feedback.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        private async Task StartAsync()
        {
            try
            {
                using (await deviceActionLock.LockAsync(ShutdownToken).ConfigureAwait(false))
                {
                    await changedCommands.EnqueueAsync(DeviceControl.NotConnectedCommand).ConfigureAwait(false);
                    CheckConnection();

                    rootDeviceData.CreateOrUpdateDevices(connector.Commands, connector.Feedbacks);
                    if (connector != null)
                    {
                        await connector.Refresh(ShutdownToken).IgnoreException().ConfigureAwait(false);
                    }
                }

                var taskProcessCommands = ProcessCommands();
                var taskProcessFeedbacks = ProcessFeedbacks();

                await Task.WhenAll(taskProcessCommands, taskProcessFeedbacks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!ex.IsCancelException())
                {
                    Trace.TraceError(Invariant($"{Name} error occured for {DeviceType} StartAsync : {ex.GetFullMessage()}"));
                }
                throw;
            }
        }

        private readonly AsyncProducerConsumerQueue<DeviceCommand> changedCommands = new AsyncProducerConsumerQueue<DeviceCommand>();
        private readonly AsyncProducerConsumerQueue<FeedbackValue> changedFeedbacks = new AsyncProducerConsumerQueue<FeedbackValue>();
        private readonly CancellationTokenSource combinedCancellationSource;
        private readonly AsyncLock deviceActionLock = new AsyncLock();
        private readonly ConcurrentDictionary<string, object> feedbackValues = new ConcurrentDictionary<string, object>();
        private readonly IHSApplication HS;
        private readonly CancellationTokenSource instanceCancellationSource = new CancellationTokenSource();
        private readonly DeviceRootDeviceManager rootDeviceData;
        private DeviceControl connector;
        private bool disposedValue; // To detect redundant calls
    }
}