using HomeSeerAPI;
using Hspi.Connector;
using Hspi.DeviceData;
using Hspi.Exceptions;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Hspi
{
    using System.Collections.ObjectModel;
    using static System.FormattableString;

    /// <summary>
    /// Plugin class
    /// </summary>
    /// <seealso cref="Hspi.HspiBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class Plugin : HspiBase
    {
        public Plugin()
            : base(PluginData.PluginName)
        {
        }

        public override string InitIO(string port)
        {
            string result = string.Empty;
            try
            {
                pluginConfig = new PluginConfig(HS);
                configPage = new ConfigPage(HS, pluginConfig);
                LogInfo("Starting Plugin");
#if DEBUG
                pluginConfig.DebugLogging = true;
#endif

                pluginConfig.ConfigChanged += PluginConfig_ConfigChanged;

                RegisterConfigPage();

                RestartConnections();

                LogDebug("Plugin Started");
            }
            catch (Exception ex)
            {
                result = Invariant($"Failed to initialize PlugIn With {ex.GetFullMessage()}");
                LogError(result);
            }

            return result;
        }

        private void PluginConfig_ConfigChanged(object sender, EventArgs e)
        {
            RestartConnections();
        }

        public override void LogDebug(string message)
        {
            if (pluginConfig.DebugLogging)
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
            foreach (var control in colSend)
            {
                try
                {
                    int refId = control.Ref;
                    DeviceClass deviceClass = (DeviceClass)HS.GetDeviceByRef(refId);

                    DeviceIdentifier deviceIdentifier = null;
                    var deviceId = DeviceIdentifier.IdentifyRoot(deviceClass);

                    if (deviceId == null)
                    {
                        deviceIdentifier = DeviceIdentifier.Identify(deviceClass);
                        deviceId = deviceIdentifier?.DeviceId;
                    }

                    lock (connectorManagerLock)
                    {
                        if (connectorManager.TryGetValue(deviceId.Value, out var connector))
                        {
                            connector.HandleCommand(deviceIdentifier, control.ControlValue).Wait();
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
            lock (connectorManagerLock)
            {
                bool changed = false;
                // Update changed or new
                foreach (var device in pluginConfig.Devices)
                {
                    if (connectorManager.TryGetValue(device.Key, out var oldConnectorBase))
                    {
                        var oldConnector = oldConnectorBase as DeviceControlManager;
                        if (!device.Value.Equals(oldConnector.DeviceConfig))
                        {
                            changed = true;
                            oldConnector.Cancel();
                            oldConnector.Dispose();
                            if (device.Value.Enabled)
                            {
                                connectorManager[device.Key] =
                                        new DeviceControlManager(HS, device.Value, this as ILogger, ShutdownCancellationToken);
                            }
                        }
                    }
                    else
                    {
                        if (device.Value.Enabled)
                        {
                            changed = true;
                            connectorManager[device.Key] =
                                    new DeviceControlManager(HS, device.Value, this as ILogger, ShutdownCancellationToken);
                        }
                    }
                }

                if (changed)
                {
                    connectorManager.Remove(DeviceType.GlobalMacros);

                    connectorManager[DeviceType.GlobalMacros] =
                           new GlobalMacrosDeviceControlManager(HS,
                                                                this as ILogger,
                                                                new Dictionary<DeviceType, DeviceControlManagerBase>(connectorManager),
                                                                ShutdownCancellationToken);
                }
            }
        }

        private CancellationTokenSource cancellationTokenSourceForUpdateDevice = new CancellationTokenSource();
        private readonly object connectorManagerLock = new object();

        private readonly Dictionary<DeviceType, DeviceControlManagerBase> connectorManager
                                = new Dictionary<DeviceType, DeviceControlManagerBase>();

        private ConfigPage configPage;
        private PluginConfig pluginConfig;
        private bool disposedValue = false;
    }
}