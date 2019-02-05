using System;
using System.Collections.Generic;

namespace Hspi
{
    [Serializable]
    internal sealed class KeyPressedTrigger
    {
        public KeyPressedTrigger(Guid deviceId, string key)
        {
            DeviceId = deviceId;
            Key = key;
        }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Key) &&
                   DeviceId != Guid.Empty;
        }

        public readonly Guid DeviceId;
        public readonly string Key;

        public class EqualityComparer : IEqualityComparer<KeyPressedTrigger>
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "1")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0")]
            public bool Equals(KeyPressedTrigger x, KeyPressedTrigger y)
            {
                return x.DeviceId == y.DeviceId &&
                    string.Compare(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) == 0;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0")]
            public int GetHashCode(KeyPressedTrigger x)
            {
                int code = x.DeviceId.GetHashCode();
                if (x.Key != null)
                {
                    code ^= x.Key.GetHashCode();
                }
                return code;
            }
        }
    }
}