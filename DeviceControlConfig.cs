﻿using Hspi.Devices;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace Hspi
{
    internal enum DeviceType
    {
        SamsungTV,
        ADBRemoteControl,
        DenonAVR,
        GlobalMacros,
        IP2IR,
        XboxOne,
        SonyBluRay
    }

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceControlConfig : IEquatable<DeviceControlConfig>
    {
        public DeviceControlConfig(DeviceType deviceType, string name, IPAddress deviceIP,
                                    IReadOnlyDictionary<string, string> additionalValues, bool enabled)
        {
            Enabled = enabled;
            Name = name;
            DeviceIP = deviceIP;
            DeviceType = deviceType;
            AdditionalValues = additionalValues;
            powerOnDelay = new Lazy<TimeSpan>(() => TimeSpan.FromMilliseconds(uint.Parse(AdditionalValues[DefaultPowerOnDelayId], CultureInfo.InvariantCulture)));
            defaultCommandDelay = new Lazy<TimeSpan>(() => TimeSpan.FromMilliseconds(uint.Parse(AdditionalValues[DefaultCommandDelayId], CultureInfo.InvariantCulture)));
        }

        public TimeSpan DefaultCommandDelay => defaultCommandDelay.Value;
        public IPAddress DeviceIP { get; }
        public DeviceType DeviceType { get; }
        public bool Enabled { get; }
        public string Name { get; }

        public TimeSpan PowerOnDelay => powerOnDelay.Value;

        public static IEnumerable<string> GetRequiredAdditionalValues(DeviceType deviceType)
        {
            switch (deviceType)
            {
                case DeviceType.SamsungTV:
                    return new string[] { PhysicalAddressId, DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.ADBRemoteControl:
                    return new string[] { ADBPathId, DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.DenonAVR:
                    return new string[] { DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.GlobalMacros:
                    return new string[] { };

                case DeviceType.IP2IR:
                    return new string[] { DefaultCommandDelayId, IP2IRFileNameId };

                case DeviceType.XboxOne:
                    return new string[] { DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.SonyBluRay:
                    return new string[] { PhysicalAddressId, DefaultCommandDelayId, DefaultPowerOnDelayId };
            }

            throw new KeyNotFoundException();
        }

        public DeviceControl Create(IConnectionProvider connectionProvider)
        {
            switch (DeviceType)
            {
                case DeviceType.SamsungTV:
                    return new SamsungTVControl(Name, DeviceIP,
                                                PhysicalAddress.Parse(AdditionalValues[PhysicalAddressId]),
                                                DefaultCommandDelay,
                                                connectionProvider);

                case DeviceType.ADBRemoteControl:
                    return new ADBRemoteControl(Name, DeviceIP,
                                                AdditionalValues[ADBPathId],
                                                DefaultCommandDelay,
                                                connectionProvider);

                case DeviceType.DenonAVR:
                    return new DenonAVRControl(Name, DeviceIP, DefaultCommandDelay, connectionProvider);

                case DeviceType.IP2IR:
                    return new IP2IRDeviceControl(Name, DeviceIP,
                                                  DefaultCommandDelay,
                                                  AdditionalValues[IP2IRFileNameId],
                                                  connectionProvider);

                case DeviceType.XboxOne:
                    return new XBoxIRControl(Name, DeviceIP,
                                                   DefaultCommandDelay,
                                                   connectionProvider);

                case DeviceType.SonyBluRay:
                    return new SonyBluRayControl(Name, DeviceIP,
                                                PhysicalAddress.Parse(AdditionalValues[PhysicalAddressId]),
                                                DefaultCommandDelay,
                                                connectionProvider);
            }

            throw new KeyNotFoundException();
        }

        public bool Equals(DeviceControlConfig other)
        {
            if (this == other)
            {
                return true;
            }

            bool same = DeviceType == other.DeviceType &&
               Name == other.Name &&
               DeviceIP == other.DeviceIP &&
               Enabled == other.Enabled &&
               AdditionalValues.Count == other.AdditionalValues.Count &&
               !AdditionalValues.Except(other.AdditionalValues).Any();
            return same;
        }

        public const string ADBPathId = "ADBPath";
        public const string DefaultCommandDelayId = "CommandDelay(ms)";
        public const string DefaultPowerOnDelayId = "PowerOnDelay(ms)";
        public const string PhysicalAddressId = "PhysicalAddress";
        public const string IP2IRFileNameId = "IP2IRFileName";
        public IReadOnlyDictionary<string, string> AdditionalValues;
        private Lazy<TimeSpan> defaultCommandDelay;
        private Lazy<TimeSpan> powerOnDelay;
    }
}