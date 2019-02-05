using HomeSeerAPI;
using Hspi.Roku;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using static System.FormattableString;

namespace Hspi
{
    internal sealed class EmulatorRokuConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmulatorRokuConfig"/> class.
        /// </summary>
        /// <param name="HS">The homeseer application.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1806:DoNotIgnoreMethodResults", MessageId = "System.Net.IPAddress.TryParse(System.String,System.Net.IPAddress@)")]
        public EmulatorRokuConfig(IHSApplication HS)
        {
            this.HS = HS;

            IPAddress.TryParse(GetValue(SsdpAdvertiseKey, string.Empty), out ssdpAdvertiseAddress);
            string deviceIdsConcatString = GetValue(DeviceIds, string.Empty);
            string[] deviceIds = deviceIdsConcatString.Split(DeviceIdsSeparator);

            minWaitForKeyPress = TimeSpan.FromMilliseconds(GetValue(MinWaitForKeyPressKey, 0));
            commandMappingFile = GetValue(CommandMappingFileKey, string.Empty);

            foreach (string deviceIdString in deviceIds)
            {
                try
                {
                    if (!Guid.TryParse(deviceIdString, out Guid deviceId))
                    {
                        continue;
                    }

                    string name = GetValue(nameof(EmulatedRokuSettings.Name), string.Empty, deviceIdString);
                    var serialNumber = GetValue(nameof(EmulatedRokuSettings.SerialNumber), string.Empty, deviceIdString);

                    string rokuAddressAddress = GetValue(nameof(EmulatedRokuSettings.RokuAddress) + nameof(IPEndPoint.Address), string.Empty, deviceIdString);
                    int rokuAddressPort = GetValue(nameof(EmulatedRokuSettings.RokuAddress) + nameof(IPEndPoint.Port), 0, deviceIdString);
                    string advertiseAddress = GetValue(nameof(EmulatedRokuSettings.RokuAddress) + nameof(IPEndPoint.Address), string.Empty, deviceIdString);
                    int advertisePort = GetValue(nameof(EmulatedRokuSettings.AdvertiseAddress) + nameof(IPEndPoint.Port), 0, deviceIdString);

                    devices.Add(deviceId, new EmulatedRokuSettings(deviceId,
                                                                   name,
                                                                   serialNumber,
                                                                   new IPEndPoint(IPAddress.Parse(rokuAddressAddress), rokuAddressPort),
                                                                   new IPEndPoint(IPAddress.Parse(advertiseAddress), advertisePort)));
                }
                catch (Exception ex)
                {
                    Trace.TraceError(Invariant($"Failed to load info for {deviceIdString} with {ex.GetFullMessage()}"));
                }
            }
        }

        public event EventHandler<EventArgs> ConfigChanged;

        /// <summary>
        /// Gets or sets the devices
        /// </summary>
        /// <value>
        /// The API key.
        /// </value>
        public ImmutableDictionary<Guid, EmulatedRokuSettings> Devices
        {
            get
            {
                using (var sync = configLock.ReaderLock())
                {
                    return devices.ToImmutableDictionary();
                }
            }
        }

        public IPAddress SSDAdvertiseAddress
        {
            get
            {
                using (var sync = configLock.ReaderLock())
                {
                    return ssdpAdvertiseAddress;
                }
            }

            set
            {
                using (var sync = configLock.WriterLock())
                {
                    SetValue(SsdpAdvertiseKey, value);
                    ssdpAdvertiseAddress = value;
                }
            }
        }

        public string CommandMappingFile
        {
            get
            {
                using (var sync = configLock.ReaderLock())
                {
                    return commandMappingFile;
                }
            }

            set
            {
                using (var sync = configLock.WriterLock())
                {
                    SetValue(CommandMappingFileKey, value);
                    commandMappingFile = value;
                }
            }
        }

        public TimeSpan MinWaitForKeyPress
        {
            get
            {
                using (var sync = configLock.ReaderLock())
                {
                    return minWaitForKeyPress;
                }
            }

            set
            {
                using (var sync = configLock.WriterLock())
                {
                    SetValue(MinWaitForKeyPressKey, value.TotalMilliseconds);
                    minWaitForKeyPress = value;
                }
            }
        }

        public void AddDevice(EmulatedRokuSettings device)
        {
            using (var sync = configLock.WriterLock())
            {
                devices[device.Id] = device;

                SetValue(nameof(EmulatedRokuSettings.Name), device.Name, device.Id.ToString());
                SetValue(nameof(EmulatedRokuSettings.SerialNumber), device.SerialNumber, device.Id.ToString());
                SetValue(nameof(EmulatedRokuSettings.RokuAddress) + nameof(IPEndPoint.Address), device.RokuAddress.Address, device.Id.ToString());
                SetValue(nameof(EmulatedRokuSettings.RokuAddress) + nameof(IPEndPoint.Port), device.RokuAddress.Port, device.Id.ToString());
                SetValue(nameof(EmulatedRokuSettings.AdvertiseAddress) + nameof(IPEndPoint.Address), device.AdvertiseAddress.Address, device.Id.ToString());
                SetValue(nameof(EmulatedRokuSettings.AdvertiseAddress) + nameof(IPEndPoint.Port), device.AdvertiseAddress.Port, device.Id.ToString());

                SetValue(DeviceIds, devices.Keys.Aggregate<Guid, string>(string.Empty,
                    (x, y) => x + DeviceIdsSeparator + y.ToString()));
            }
        }

        /// <summary>
        /// Fires event that configuration changed.
        /// </summary>
        public void FireConfigChanged()
        {
            if (ConfigChanged != null)
            {
                EventHandler<EventArgs> ConfigChangedCopy = ConfigChanged;
                ConfigChangedCopy(this, EventArgs.Empty);
            }
        }

        public void RemoveDevice(Guid deviceId)
        {
            using (var sync = configLock.WriterLock())
            {
                devices.Remove(deviceId);
                if (devices.Count > 0)
                {
                    SetValue(DeviceIds, devices.Keys.Aggregate<Guid, string>(string.Empty,
                        (x, y) => x + DeviceIdsSeparator + y.ToString()));
                }
                else
                {
                    SetValue(DeviceIds, string.Empty);
                }
                HS.ClearINISection(deviceId.ToString(), FileName);
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
        private const string DeviceIds = "DevicesIds";
        private const char DeviceIdsSeparator = '|';
        private const string SsdpAdvertiseKey = "SsdpAdvertiseKey";
        private const string MinWaitForKeyPressKey = "MinWaitForKeyPress";
        private const string CommandMappingFileKey = "CommandMappingFile";
        private static readonly string FileName = "HSPI_EmulatedRoku.exe.ini";
        private readonly AsyncReaderWriterLock configLock = new AsyncReaderWriterLock();
        private readonly Dictionary<Guid, EmulatedRokuSettings> devices = new Dictionary<Guid, EmulatedRokuSettings>();
        private readonly IHSApplication HS;
        private IPAddress ssdpAdvertiseAddress;
        private TimeSpan minWaitForKeyPress;
        private string commandMappingFile;
    };
}