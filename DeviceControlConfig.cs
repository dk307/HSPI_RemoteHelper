using Hspi.Devices;
using NullGuard;
using System;
using System.Collections.Generic;
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
        }

        public IPAddress DeviceIP { get; }
        public DeviceType DeviceType { get; }
        public bool Enabled { get; }
        public string Name { get; }

        public DeviceControl Create()
        {
            switch (DeviceType)
            {
                case DeviceType.SamsungTV:
                    return new SamsungTVControl(Name, DeviceIP, PhysicalAddress.Parse(AdditionalValues[PhysicalAddressId]));

                case DeviceType.ADBRemoteControl:
                    return new ADBRemoteControl(Name, DeviceIP, AdditionalValues[ADBPathId]);

                case DeviceType.DenonAVR:
                    return new DenonAVRControl(Name, DeviceIP);
            }

            throw new KeyNotFoundException();
        }

        public bool Equals(DeviceControlConfig other)
        {
            if (this == other)
            {
                return true;
            }

            //TODO :: Fix this
            return DeviceType == other.DeviceType &&
                Name == other.Name &&
                DeviceIP == other.DeviceIP &&
                Enabled == other.Enabled;
        }

        public const string ADBPathId = "ADBPath";
        public const string PhysicalAddressId = "PhysicalAddress";
        public IReadOnlyDictionary<string, string> AdditionalValues;
    }
}