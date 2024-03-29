﻿using Hspi.Devices;
using Nito.AsyncEx;
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
        SonyBluRay,
        Hue,
        HueSyncBox,
    }

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class DeviceControlConfig : IEquatable<DeviceControlConfig>
    {
        public DeviceControlConfig(DeviceType deviceType, string name, IPAddress deviceIP,
                                    IReadOnlyDictionary<string, string> additionalValues, bool enabled)
        {
            Enabled = enabled;
            Name = name;
            DeviceIP = deviceIP;
            DeviceType = deviceType;
            AdditionalValues = additionalValues;
            defaultCommandDelay = new Lazy<TimeSpan>(() =>
            {
                if (AdditionalValues.TryGetValue(DefaultCommandDelayId, out string value))
                {
                    return TimeSpan.FromMilliseconds(uint.Parse(value, CultureInfo.InvariantCulture));
                }
                else
                {
                    return TimeSpan.Zero;
                }
            });
            powerOnDelay = new Lazy<TimeSpan>(() =>
            {
                if (AdditionalValues.TryGetValue(DefaultPowerOnDelayId, out string value))
                {
                    return TimeSpan.FromMilliseconds(uint.Parse(value, CultureInfo.InvariantCulture));
                }
                else
                {
                    return TimeSpan.Zero;
                }
            });
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
                    return new string[] { PhysicalAddressId, DefaultCommandDelayId, DefaultPowerOnDelayId, WolBroadCastAddressId };

                case DeviceType.ADBRemoteControl:
                    return new string[] { ADBPathId, DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.DenonAVR:
                    return new string[] { DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.GlobalMacros:
                    return Array.Empty<string>();

                case DeviceType.IP2IR:
                    return new string[] { DefaultCommandDelayId, IP2IRFileNameId };

                case DeviceType.XboxOne:
                    return new string[] { DefaultCommandDelayId, DefaultPowerOnDelayId };

                case DeviceType.SonyBluRay:
                    return new string[] { PhysicalAddressId, DefaultCommandDelayId, DefaultPowerOnDelayId, WolBroadCastAddressId };

                 case DeviceType.Hue:
                    return new string[] { UserNameId, DevicesId };

                case DeviceType.HueSyncBox:
                    return new string[] { UserNameId };
            }

            throw new KeyNotFoundException();
        }

        public DeviceControl Create(IConnectionProvider connectionProvider,
                                    AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                    AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue)
        {
            switch (DeviceType)
            {
                case DeviceType.SamsungTV:
                    return new SamsungTVControl(Name, DeviceIP,
                                                PhysicalAddress.Parse(AdditionalValues[PhysicalAddressId]),
                                                IPAddress.Parse(AdditionalValues[WolBroadCastAddressId]),
                                                DefaultCommandDelay,
                                                connectionProvider,
                                                commandQueue,
                                                feedbackQueue);

                case DeviceType.ADBRemoteControl:
                    return new ADBRemoteControl(Name, DeviceIP,
                                                AdditionalValues[ADBPathId],
                                                DefaultCommandDelay,
                                                connectionProvider,
                                                commandQueue,
                                                feedbackQueue);

                case DeviceType.DenonAVR:
                    return new DenonAVRControl(Name, DeviceIP, DefaultCommandDelay,
                                               connectionProvider,
                                               commandQueue,
                                               feedbackQueue);

                case DeviceType.IP2IR:
                    return new IP2IRDeviceControl(Name, DeviceIP,
                                                  DefaultCommandDelay,
                                                  AdditionalValues[IP2IRFileNameId],
                                                  connectionProvider,
                                                  commandQueue,
                                                  feedbackQueue);

                case DeviceType.XboxOne:
                    return new XBoxIRControl(Name, DeviceIP,
                                                   DefaultCommandDelay,
                                                   connectionProvider,
                                                   commandQueue,
                                                   feedbackQueue);

                case DeviceType.SonyBluRay:
                    return new SonyBluRayControl(Name, DeviceIP,
                                                PhysicalAddress.Parse(AdditionalValues[PhysicalAddressId]),
                                                IPAddress.Parse(AdditionalValues[WolBroadCastAddressId]),
                                                DefaultCommandDelay,
                                                connectionProvider,
                                                commandQueue,
                                                feedbackQueue);

                case DeviceType.Hue:
                    return new PhilipsHueControl(Name, DeviceIP,
                                                AdditionalValues[UserNameId],
                                                AdditionalValues[DevicesId].Split(','),
                                                DefaultCommandDelay,
                                                connectionProvider,
                                                commandQueue,
                                                feedbackQueue);

                case DeviceType.HueSyncBox:
                    return new PhilipsHueSyncBoxControl(Name, DeviceIP,
                                                AdditionalValues[UserNameId],
                                                DefaultCommandDelay,
                                                connectionProvider,
                                                commandQueue,
                                                feedbackQueue);
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

        public override bool Equals(object obj)
        {
            return Equals(obj as DeviceControlConfig);
        }

        public const string ADBPathId = "ADBPath";
        public const string DefaultCommandDelayId = "CommandDelay(ms)";
        public const string DefaultPowerOnDelayId = "PowerOnDelay(ms)";
        public const string DevicesId = "Devices";
        public const string IP2IRFileNameId = "IP2IRFileName";
        public const string PhysicalAddressId = "PhysicalAddress";
        public const string UserNameId = "Username";
        public const string WolBroadCastAddressId = "WolBroadCastAddress";
        public IReadOnlyDictionary<string, string> AdditionalValues;
        private readonly Lazy<TimeSpan> defaultCommandDelay;
        private readonly Lazy<TimeSpan> powerOnDelay;

         
    }
}