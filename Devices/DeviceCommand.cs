namespace Hspi.Devices
{
    internal enum DeviceCommandType
    {
        Control,
        Status,
        Both
    }

    internal class DeviceCommand
    {
        public DeviceCommand(string id, string data = null,
                             DeviceCommandType type = DeviceCommandType.Control, int? fixedValue = null)
        {
            Data = data;
            Id = id;
            Type = type;
            FixedValue = fixedValue;
        }

        public string Data { get; }
        public int? FixedValue { get; }
        public string Id { get; }
        public DeviceCommandType Type { get; }
    }
}