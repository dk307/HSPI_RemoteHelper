using HomeSeerAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Class to store PlugIn Configuration
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    internal class PluginConfig : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        /// <param name="HS">The homeseer application.</param>
        public PluginConfig(IHSApplication HS)
        {
            this.HS = HS;

            debugLogging = GetValue(DebugLoggingKey, false);

            LoadDeviceConfig(DeviceType.SamsungTV);
            LoadDeviceConfig(DeviceType.DenonAVR);
            LoadDeviceConfig(DeviceType.ADBRemoteControl);
        }

        public event EventHandler<EventArgs> ConfigChanged;

        /// <summary>
        /// Gets or sets a value indicating whether debug logging is enabled.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [debug logging]; otherwise, <c>false</c>.
        /// </value>
        public bool DebugLogging
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return debugLogging;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }

            set
            {
                configLock.EnterWriteLock();
                try
                {
                    debugLogging = value;
                    SetValue(DebugLoggingKey, value);
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public IReadOnlyDictionary<DeviceType, DeviceControlConfig> Devices
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return new Dictionary<DeviceType, DeviceControlConfig>(devices);
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Fires event that configuration changed.
        /// </summary>
        public void FireConfigChanged()
        {
            if (ConfigChanged != null)
            {
                var ConfigChangedCopy = ConfigChanged;
                ConfigChangedCopy(this, EventArgs.Empty);
            }
        }

        public void UpdateDevice(DeviceControlConfig deviceConfig)
        {
            configLock.EnterWriteLock();
            try
            {
                devices[deviceConfig.DeviceType] = deviceConfig;
                SaveDeviceConfig(deviceConfig);
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    configLock.Dispose();
                }
                disposedValue = true;
            }
        }

        private T GetValue<T>(string key, T defaultValue)
        {
            return GetValue(key, defaultValue, DefaultSection);
        }

        private T GetValue<T>(string key, T defaultValue, string section)
        {
            string stringValue = HS.GetINISetting(section, key, null, FileName);

            if (stringValue != null)
            {
                try
                {
                    T result = (T)System.Convert.ChangeType(stringValue, typeof(T), CultureInfo.InvariantCulture);
                    return result;
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private void LoadDeviceConfig(DeviceType deviceType)
        {
            string deviceId = deviceType.ToString();

            string name = GetValue(DeviceNameId, string.Empty, deviceId);
            string ipAddressString = GetValue(DeviceIPId, string.Empty, deviceId);
            bool enabled = GetValue(EnabledId, false, deviceId);
            IPAddress.TryParse(ipAddressString, out var deviceIP);

            var additionalValues = new Dictionary<string, string>();
            foreach (var key in DeviceControlConfig.GetRequiredAdditionalValues(deviceType))
            {
                string value = GetValue(key, string.Empty, deviceId);
                additionalValues.Add(key, value);
            }
            var config = new DeviceControlConfig(deviceType, name, deviceIP ?? IPAddress.Any, additionalValues, enabled);
            devices.Add(deviceType, config);
        }

        private void SaveDeviceConfig(DeviceControlConfig deviceConfig)
        {
            string deviceId = deviceConfig.DeviceType.ToString();

            SetValue(DeviceNameId, deviceConfig.Name, deviceId);
            SetValue(DeviceIPId, deviceConfig.DeviceIP.ToString(), deviceId);
            SetValue(EnabledId, deviceConfig.Enabled, deviceId);

            foreach (var key in deviceConfig.AdditionalValues)
            {
                SetValue(key.Key, key.Value, deviceId);
            }
        }

        private void SetValue<T>(string key, T value)
        {
            SetValue<T>(key, value, DefaultSection);
        }

        private void SetValue<T>(string key, T value, string section)
        {
            string stringValue = System.Convert.ToString(value, CultureInfo.InvariantCulture);
            HS.SaveINISetting(section, key, stringValue, FileName);
        }

        private const string DebugLoggingKey = "DebugLogging";
        private const string DefaultSection = "Settings";
        private const string DeviceIPId = "DeviceIP";
        private const string DeviceNameId = "Name";
        private const string EnabledId = "Enabled";
        private readonly static string FileName = Invariant($"{Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location)}.ini");
        private readonly ReaderWriterLockSlim configLock = new ReaderWriterLockSlim();
        private readonly IDictionary<DeviceType, DeviceControlConfig> devices = new Dictionary<DeviceType, DeviceControlConfig>();
        private readonly IHSApplication HS;
        private bool debugLogging;
        private bool disposedValue = false;
    };
}