using Scheduler.Classes;
using System;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    internal class DeviceIdentifier
    {
        public DeviceIdentifier(DeviceType deviceId, string feedbackName)
        {
            FeedbackName = feedbackName;
            DeviceId = deviceId;
        }

        public string Address => Invariant($"{RootDeviceAddress}{AddressSeparator}{FeedbackName}");
        public DeviceType DeviceId { get; }
        public string FeedbackName { get; }

        public string RootDeviceAddress => CreateRootAddress(DeviceId);

        public static string CreateRootAddress(DeviceType deviceId) => Invariant($"{PluginData.PluginName}{AddressSeparator}{deviceId}");

        public static DeviceIdentifier Identify(DeviceClass hsDevice)
        {
            var childAddress = hsDevice.get_Address(null);

            var parts = childAddress.Split(AddressSeparator);

            if (parts.Length != 3)
            {
                return null;
            }

            if (Enum.TryParse<DeviceType>(parts[1], out var deviceType))
            {
                return new DeviceIdentifier(deviceType, parts[2]);
            }

            return null;
        }

        public static DeviceType? IdentifyRoot(DeviceClass hsDevice)
        {
            var childAddress = hsDevice.get_Address(null);

            var parts = childAddress.Split(AddressSeparator);

            if (parts.Length != 2)
            {
                return null;
            }

            if (Enum.TryParse<DeviceType>(parts[1], out var deviceType))
            {
                return deviceType;
            }
            return null;
        }

        private const char AddressSeparator = '.';
    }
}