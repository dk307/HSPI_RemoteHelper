namespace Hspi.DeviceData
{
    internal class DoubleFeedbackDeviceData : DoubleFeedbackDeviceDataBase
    {
        public DoubleFeedbackDeviceData(int? refId) : base(refId)
        {
        }

        public override bool StatusDevice => true;
    }
}