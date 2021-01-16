using HomeSeerAPI;
using Hspi.Connector;
using Hspi.DeviceData;
using Hspi.Exceptions;
using Hspi.Pages;
using Hspi.Utils;
using Nito.AsyncEx;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi
{
    /// <summary>
    /// Plugin class
    /// </summary>
    /// <seealso cref="Hspi.HspiBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal partial class Plugin : HspiBase, IConnectionProvider
    {
        public Plugin()
            : base(PluginData.PluginName)
        {
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

        public override string GetPagePlugin(string page, [AllowNull]string user, int userRights, [AllowNull]string queryString)
        {
            if (page == RemoteHelperConfigPage.Name)
            {
                return configPage.GetWebPage(queryString);
            }

            if (page == EmulatedRokuConfigPage.Name)
            {
                return emulatedRokuConfigPage.GetWebPage(queryString);
            }

            return string.Empty;
        }

        public override string InitIO(string port)
        {
            Trace.TraceInformation(Invariant($"Starting InitIO on Port {port}"));
            string result = string.Empty;
            try
            {
                pluginConfig = new PluginConfig(HS);
                emulatorRokuPluginConfig = new EmulatorRokuConfig(HS);
                configPage = new RemoteHelperConfigPage(HS, pluginConfig);
                emulatedRokuConfigPage = new EmulatedRokuConfigPage(HS, emulatorRokuPluginConfig);

                pluginConfig.ConfigChanged += PluginConfig_RemoteHelperConfigChanged;
                emulatorRokuPluginConfig.ConfigChanged += EmulatorRokuPluginConfig_ConfigChanged;
                RegisterConfigPage();

                MyTaskHelper.StartAsyncWithErrorChecking("RestartConnections", RestartConnections, ShutdownCancellationToken);
                MyTaskHelper.StartAsyncWithErrorChecking("RestartRokuOperations", RestartRokuOperations, ShutdownCancellationToken);
                MyTaskHelper.StartAsyncWithErrorChecking("LoadRokuToDeviceMapping", LoadRokuToDeviceMapping, ShutdownCancellationToken);

                Trace.TraceInformation(Invariant($"Finished InitIO on Port {port}"));
            }
            catch (Exception ex)
            {
                result = Invariant($"Failed to initialize PlugIn With {ex.GetFullMessage()}");
                Trace.TraceError(result);
            }

            return result;
        }

        public override void LogDebug(string message)
        {
            if ((pluginConfig != null) && pluginConfig.DebugLogging)
            {
                base.LogDebug(message);
            }
        }

        public override string PostBackProc(string page, string data, [AllowNull]string user, int userRights)
        {
            if (page == RemoteHelperConfigPage.Name)
            {
                return configPage.PostBackProc(data, user, userRights);
            }
            if (page == EmulatedRokuConfigPage.Name)
            {
                return emulatedRokuConfigPage.PostBackProc(data, user, userRights);
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

                    DeviceType deviceType = deviceId.Value;
                    using (connectorManagerLock.Lock())
                    {
                        if (connectorManagers.TryGetValue(deviceType, out DeviceControlManagerCore connector))
                        {
                            CancellationTokenSource combinedCancel = CancellationTokenSource.CreateLinkedTokenSource(ShutdownCancellationToken);
                            combinedCancel.CancelAfter(commandMaxTime);
                            connector.HandleCommand(deviceIdentifier, control.ControlValue, combinedCancel.Token).ResultForSync();
                        }
                        else
                        {
                            throw new HspiException(Invariant($"{deviceType} Device Not Found for processing."));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(Invariant($"Command Failed With {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "emulatedRokuConfigPage")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "configPage")]
        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                using (connectorManagerLock.Lock())
                {
                    foreach (var connection in connectorManagers)
                    {
                        connection.Value.Dispose();
                    }
                }

                DisposeRokuServers();

                if (pluginConfig != null)
                {
                    pluginConfig.ConfigChanged -= PluginConfig_RemoteHelperConfigChanged;
                }

                if (emulatorRokuPluginConfig != null)
                {
                    emulatorRokuPluginConfig.ConfigChanged -= EmulatorRokuPluginConfig_ConfigChanged;
                }

                configPage?.Dispose();
                emulatedRokuConfigPage?.Dispose();
                pluginConfig.Dispose();
                publisher?.Dispose();

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        private void EmulatorRokuPluginConfig_ConfigChanged(object sender, EventArgs e)
        {
            MyTaskHelper.StartAsyncWithErrorChecking("RestartRokuOperations", RestartRokuOperations, ShutdownCancellationToken);
            MyTaskHelper.StartAsyncWithErrorChecking("LoadRokuToDeviceMapping", LoadRokuToDeviceMapping, ShutdownCancellationToken);
        }

        private async Task HandleCommand(DeviceType deviceType, string commandId)
        {
            try
            {
                using (var sync = await connectorManagerLock.LockAsync(ShutdownCancellationToken).ConfigureAwait(false))
                {
                    if (connectorManagers.TryGetValue(deviceType, out DeviceControlManagerCore connector))
                    {
#pragma warning disable CA2000 // Dispose objects before losing scope
                        CancellationTokenSource combinedCancel = CancellationTokenSource.CreateLinkedTokenSource(ShutdownCancellationToken);
#pragma warning restore CA2000 // Dispose objects before losing scope
                        combinedCancel.CancelAfter(commandMaxTime);
                        await connector.HandleCommand(commandId, combinedCancel.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        throw new HspiException(Invariant($"{deviceType} Device Not Found for processing."));
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(Invariant($"{deviceType.ToString("g")} command {commandId} failed With {ExceptionHelper.GetFullMessage(ex)}"));
            }
        }

        private void PluginConfig_RemoteHelperConfigChanged(object sender, EventArgs e)
        {
            MyTaskHelper.StartAsyncWithErrorChecking("RestartConnections", RestartConnections, ShutdownCancellationToken);
        }

        private void RegisterConfigPage()
        {
            string link = RemoteHelperConfigPage.Name;
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

            HS.RegisterPage(EmulatedRokuConfigPage.Name, Name, string.Empty);

            HomeSeerAPI.WebPageDesc rokuWpd = new HomeSeerAPI.WebPageDesc()
            {
                plugInName = Name,
                link = EmulatedRokuConfigPage.Name,
                linktext = "Roku Configuration",
                page_title = Invariant($"{Name} Roku Configuration"),
            };

            Callback.RegisterLink(rokuWpd);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private async Task RestartConnections()
        {
            using (var sync = await connectorManagerLock.LockAsync().ConfigureAwait(false))
            {
                bool changed = false;
                // Update changed or new
                foreach (var device in pluginConfig.Devices)
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

        private static readonly TimeSpan commandMaxTime = TimeSpan.FromSeconds(45);
        private readonly AsyncLock connectorManagerLock = new AsyncLock();

        private readonly ConcurrentDictionary<DeviceType, DeviceControlManagerCore> connectorManagers
                                = new ConcurrentDictionary<DeviceType, DeviceControlManagerCore>();

        private RemoteHelperConfigPage configPage;
        private bool disposedValue;
        private PluginConfig pluginConfig;
    }
}