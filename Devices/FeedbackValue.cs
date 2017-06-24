using NullGuard;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal struct FeedbackValue
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