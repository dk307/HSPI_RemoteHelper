namespace Hspi.Devices
{
    internal class DeviceCommand
    {
        public DeviceCommand(string id, string data = null)
        {
            Data = data;
            this.Id = id;
        }

        public string Id { get; }
        public string Data { get; }
    }
}