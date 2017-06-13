using HomeSeerAPI;
using Hspi.DeviceData;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Connector
{
    using Hspi.Devices;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceControlManager : IDisposable
    {
        public DeviceControlManager(IHSApplication HS, DeviceControlConfig deviceConfig, ILogger logger, CancellationToken shutdownToken)
        {
            this.HS = HS;
            this.logger = logger;
            this.DeviceConfig = deviceConfig;
            rootDeviceData = new DeviceRootDeviceManager(deviceConfig.Name, deviceConfig.DeviceType, this.HS, logger);
            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, instanceCancellationSource.Token);
            Task.Factory.StartNew(UpdateDevices, Token, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current);
        }

        public DeviceControlConfig DeviceConfig { get; }

        private CancellationToken Token => combinedCancellationSource.Token;

        public void Cancel()
        {
            instanceCancellationSource.Cancel();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public async Task HandleCommand([AllowNull]DeviceIdentifier deviceIdentifier, double value)
        {
            await deviceActionLock.WaitAsync(Token);
            try
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
                    connector = DeviceConfig.Create();
                    connector.FeedbackChanged += Connector_FeedbackChanged;
                }

                await rootDeviceData.HandleCommand(deviceIdentifier, connector, value, Token);
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

                    DisposeConnector();
                    deviceActionLock.Dispose();
                }

                disposedValue = true;
            }
        }

        private void Connector_FeedbackChanged(object sender, FeedbackValue changedFeedback)
        {
            changedFeedbacks.Add(changedFeedback);
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
                connector.FeedbackChanged -= Connector_FeedbackChanged;
                connector.Dispose();
                connector = null;
                rootDeviceData.UpdateConnectionStatus(false);
            }
        }

        private async Task UpdateDevices()
        {
            await deviceActionLock.WaitAsync(Token).ConfigureAwait(false);
            try
            {
                using (var deviceControl = DeviceConfig.Create())
                {
                    rootDeviceData.CreateOrUpdateDevices(deviceControl.Commands,
                                                         deviceControl.Feedbacks);
                }

                rootDeviceData.UpdateConnectionStatus(false);
            }
            finally
            {
                deviceActionLock.Release();
            }

            while (!Token.IsCancellationRequested)
            {
                if (changedFeedbacks.TryTake(out var feedbackData, -1, Token))
                {
                    await deviceActionLock.WaitAsync(Token).ConfigureAwait(false);
                    try
                    {
                        rootDeviceData.ProcessFeedback(feedbackData);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(Invariant($"Failed to update Feedback {feedbackData.Feedback.Id} on {DeviceConfig.DeviceIP} with {ExceptionHelper.GetFullMessage(ex)}"));
                    }
                    finally
                    {
                        deviceActionLock.Release();
                    }
                }
            }
        }

        private readonly BlockingCollection<FeedbackValue> changedFeedbacks = new BlockingCollection<FeedbackValue>();
        private readonly CancellationTokenSource combinedCancellationSource;
        private readonly SemaphoreSlim deviceActionLock = new SemaphoreSlim(1);
        private readonly IHSApplication HS;
        private readonly CancellationTokenSource instanceCancellationSource = new CancellationTokenSource();
        private readonly ILogger logger;
        private readonly DeviceRootDeviceManager rootDeviceData;
        private DeviceControl connector;
        private bool disposedValue = false; // To detect redundant calls
    }
}