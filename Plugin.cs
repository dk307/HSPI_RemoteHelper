using HomeSeerAPI;
using Hspi.Connector;
using Hspi.DeviceData;
using Hspi.Exceptions;
using Hspi.Utils;
using Nito.AsyncEx;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static System.FormattableString;

namespace Hspi
{
    /// <summary>
    /// Plugin class
    /// </summary>
    /// <seealso cref="Hspi.HspiBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class Plugin : HspiBase, IConnectionProvider
    {
        public Plugin()
            : base(PluginData.PluginName)
        {
        }

        public override string InitIO(string port)
        {
            Trace.TraceInformation(Invariant($"Starting InitIO on Port {port}"));
            string result = string.Empty;
            try
            {
                pluginConfig = new PluginConfig(HS);
                configPage = new ConfigPage(HS, pluginConfig);
#if DEBUG
                pluginConfig.DebugLogging = true;
#endif

                pluginConfig.ConfigChanged += PluginConfig_ConfigChanged;

                RegisterConfigPage();

                TaskHelper.StartAsync(RestartConnections, ShutdownCancellationToken);

                Trace.TraceInformation(Invariant($"Finished InitIO on Port {port}"));
            }
            catch (Exception ex)
            {
                result = Invariant($"Failed to initialize PlugIn With {ex.GetFullMessage()}");
                Trace.TraceError(result);
            }

            return result;
        }

        private void PluginConfig_ConfigChanged(object sender, EventArgs e)
        {
            TaskHelper.StartAsync(RestartConnections, ShutdownCancellationToken);
        }

        public override void LogDebug(string message)
        {
            if ((pluginConfig != null) && pluginConfig.DebugLogging)
            {
                base.LogDebug(message);
            }
        }

        public override string GetPagePlugin(string page, [AllowNull]string user, int userRights, [AllowNull]string queryString)
        {
            if (page == ConfigPage.Name)
            {
                return configPage.GetWebPage(queryString);
            }

            return string.Empty;
        }

        public override string PostBackProc(string page, string data, [AllowNull]string user, int userRights)
        {
            if (page == ConfigPage.Name)
            {
                return configPage.PostBackProc(data, user, userRights);
            }

            return string.Empty;
        }

        public override void SetIOMulti(List<CAPI.CAPIControl> colSend)
        {
            foreach (CAPI.CAPIControl control in colSend)
            {
                try
                {
                    int refId = control.Ref;
                    DeviceClass deviceClass = (DeviceClass)HS.GetDeviceByRef(refId);

                    DeviceIdentifier deviceIdentifier = null;
                    DeviceType? deviceId = DeviceIdentifier.IdentifyRoot(deviceClass);

                    if (deviceId == null)
                    {
                        deviceIdentifier = DeviceIdentifier.Identify(deviceClass);
                        deviceId = deviceIdentifier?.DeviceId;
                    }

                    using (connectorManagerLock.Lock())
                    {
                        if (connectorManagers.TryGetValue(deviceId.Value, out DeviceControlManagerCore connector))
                        {
                            CancellationTokenSource combinedCancel = CancellationTokenSource.CreateLinkedTokenSource(ShutdownCancellationToken);
                            combinedCancel.CancelAfter(TimeSpan.FromMinutes(1));
                            connector.HandleCommand(deviceIdentifier, control.ControlValue, combinedCancel.Token).ResultForSync();
                        }
                        else
                        {
                            throw new HspiException(Invariant($"{refId} Device Not Found for processing."));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError(Invariant($"Failed With {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        private void RegisterConfigPage()
        {
            string link = ConfigPage.Name;
            HS.RegisterPage(link, Name, string.Empty);

            HomeSeerAPI.WebPageDesc wpd = new HomeSeerAPI.WebPageDesc()
            {
                plugInName = Name,
                link = link,
                linktext = "Configuration",
                page_title = Invariant($"{Name} Configuration"),
            };
            Callback.RegisterConfigLink(wpd);
            Callback.RegisterLink(wpd);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                foreach (KeyValuePair<DeviceType, DeviceControlManagerCore> connection in connectorManagers)
                {
                    connection.Value.Dispose();
                }

                if (pluginConfig != null)
                {
                    pluginConfig.ConfigChanged -= PluginConfig_ConfigChanged;
                }

                cancellationTokenSourceForUpdateDevice.Dispose();
                if (configPage != null)
                {
                    configPage.Dispose();
                }

                if (pluginConfig != null)
                {
                    pluginConfig.Dispose();
                }

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private void RestartConnections()
        {
            using (connectorManagerLock.Lock())
            {
                bool changed = false;
                // Update changed or new
                foreach (KeyValuePair<DeviceType, DeviceControlConfig> device in pluginConfig.Devices)
                {
                    if (connectorManagers.TryGetValue(device.Key, out DeviceControlManagerCore oldConnectorBase))
                    {
                        DeviceControlManager oldConnector = oldConnectorBase as DeviceControlManager;
                        if (oldConnector == null)
                        {
                            continue;
                        }
                        if (!device.Value.Equals(oldConnector.DeviceConfig))
                        {
                            changed = true;
                            oldConnector.Cancel();
                            oldConnector.Dispose();
                            if (device.Value.Enabled)
                            {
                                DeviceControlManager connection = new DeviceControlManager(HS,
                                                                          device.Value,
                                                                          this as IConnectionProvider,
                                                                          ShutdownCancellationToken);
                                connectorManagers[device.Key] = connection;
                                connection.Start();
                            }
                        }
                    }
                    else
                    {
                        if (device.Value.Enabled)
                        {
                            changed = true;
                            DeviceControlManager connection = new DeviceControlManager(HS,
                                                                      device.Value,
                                                                      this as IConnectionProvider,
                                                                      ShutdownCancellationToken);
                            connectorManagers[device.Key] = connection;
                            connection.Start();
                        }
                    }
                }

                if (changed)
                {
                    GlobalMacrosDeviceControlManager connection =
                        new GlobalMacrosDeviceControlManager(HS,
                                                             this as IConnectionProvider,
                                                             ShutdownCancellationToken);
                    connectorManagers[DeviceType.GlobalMacros] = connection;
                    connection.Start();
                }
            }
        }

        IDeviceCommandHandler IConnectionProvider.GetCommandHandler(DeviceType deviceType)
        {
            if (connectorManagers.TryGetValue(deviceType, out DeviceControlManagerCore connection))
            {
                IDeviceCommandHandler connectionBase = connection as IDeviceCommandHandler;
                if (connectionBase != null)
                {
                    return connectionBase;
                }
            }
            throw new HspiException("Not Found");
        }

        IDeviceFeedbackProvider IConnectionProvider.GetFeedbackProvider(DeviceType deviceType)
        {
            if (connectorManagers.TryGetValue(deviceType, out DeviceControlManagerCore connection))
            {
                IDeviceFeedbackProvider connectionBase = connection as IDeviceFeedbackProvider;
                if (connectionBase != null)
                {
                    return connectionBase;
                }
            }
            throw new HspiException("Not Found");
        }

        private CancellationTokenSource cancellationTokenSourceForUpdateDevice = new CancellationTokenSource();
        private readonly AsyncLock connectorManagerLock = new AsyncLock();

        private readonly ConcurrentDictionary<DeviceType, DeviceControlManagerCore> connectorManagers
                                = new ConcurrentDictionary<DeviceType, DeviceControlManagerCore>();

        private ConfigPage configPage;
        private PluginConfig pluginConfig;
        private bool disposedValue = false;
    }
}