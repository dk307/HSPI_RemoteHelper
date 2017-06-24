using NullGuard;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class FeedbackValue
    {
        public FeedbackValue(DeviceFeedback feedback, object value)
        {
            Value = value;
            Feedback = feedback;
        }

        public DeviceFeedback Feedback { get; }
        public object Value { get; }
    }
}