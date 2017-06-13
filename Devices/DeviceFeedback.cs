using System;
using System.Runtime.Serialization;
using NullGuard;

namespace Hspi.Devices
{
    [Serializable]
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceFeedback : ISerializable
    {
        public DeviceFeedback(string id, TypeCode typecode)
        {
            Typecode = typecode;
            this.Id = id;
        }

        protected DeviceFeedback(SerializationInfo info, StreamingContext context)
        {
            Id = info.GetString(nameof(Id));
            this.Typecode = (TypeCode)info.GetInt32(nameof(Typecode));
        }

        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Id), Id);
            info.AddValue(nameof(this.Typecode), (int)Typecode);
        }

        public string Id { get; }
        public TypeCode Typecode { get; }
    }
}