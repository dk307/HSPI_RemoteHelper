﻿using HomeSeerAPI;
using Hspi.DeviceData;
using Hspi.Devices;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Connector
{
    using System.Diagnostics;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceControlManagerCore : IDisposable, IDeviceFeedbackProvider
    {
        public DeviceControlManagerCore(IHSApplication HS, string name,
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
            using (await deviceActionLock.LockAsync(ShutdownToken).ConfigureAwait(false))
            {
                CheckConnection();
                await rootDeviceData.HandleCommand(deviceIdentifier, connector, value, ShutdownToken).ConfigureAwait(false);
            }
        }

        public async Task HandleCommand(string commandId, CancellationToken token)
        {
            var finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;

            using (await deviceActionLock.LockAsync(ShutdownToken).ConfigureAwait(false))
            {
                CheckConnection();

                var command = connector.GetCommand(commandId);
                await connector.ExecuteCommand(command, finalToken).ConfigureAwait(false);
            }
        }

        public async Task HandleCommand(string feedbackName, object value, CancellationToken token)
        {
            var finalToken = CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownToken).Token;

            using (await deviceActionLock.LockAsync(ShutdownToken).ConfigureAwait(false))
            {
                CheckConnection();
                var feedback = connector.GetFeedback(feedbackName);
                await connector.ExecuteCommand(new FeedbackValue(feedback, value), finalToken);
            }
        }

        public void Start()
        {
            Task.Factory.StartNew(UpdateDevices, ShutdownToken, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
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
                connector = Create();
                connector.FeedbackChanged += Connector_FeedbackChanged;
                connector.CommandChanged += Connector_CommandChanged;
            }
        }

        private void Connector_CommandChanged(object sender, DeviceCommand command)
        {
            changedCommands.Enqueue(command);
        }

        private void Connector_FeedbackChanged(object sender, FeedbackValue changedFeedback)
        {
            changedFeedbacks.Enqueue(changedFeedback);
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
                    Trace.TraceWarning(Invariant($"Failed to update Command {command.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
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
                    rootDeviceData.ProcessFeedback(feedbackData);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(Invariant($"Failed to update Feedback {feedbackData.Feedback.Id} on {DeviceType} with {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        private async Task UpdateDevices()
        {
            using (await deviceActionLock.LockAsync(ShutdownToken).ConfigureAwait(false))
            {
                using (var deviceControl = Create())
                {
                    rootDeviceData.CreateOrUpdateDevices(deviceControl.Commands,
                                                         deviceControl.Feedbacks);
                }

                await changedCommands.EnqueueAsync(DeviceControl.NotConnectedCommand).ConfigureAwait(false);
            }

            var task1 = Task.Factory.StartNew(ProcessFeedbacks, ShutdownToken,
                                  TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
            var task2 = Task.Factory.StartNew(ProcessCommands, ShutdownToken,
                                  TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
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
        private bool disposedValue = false; // To detect redundant calls
    }
}