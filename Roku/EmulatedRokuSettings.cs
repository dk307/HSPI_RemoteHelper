using System;
using System.Net;

namespace Hspi.Roku
{
    internal sealed class EmulatedRokuSettings : IEquatable<EmulatedRokuSettings>
    {
        public EmulatedRokuSettings(Guid id,
                              string name,
                              string serialNumber,
                              IPEndPoint rokuAddress,
                              IPEndPoint advertiseAddress)
        {
            Id = id;
            Name = name;
            SerialNumber = serialNumber;
            AdvertiseAddress = advertiseAddress;
            RokuAddress = rokuAddress;
        }

        public IPEndPoint AdvertiseAddress { get; }
        public Guid Id { get; }
        public string Name { get; }
        public IPEndPoint RokuAddress { get; }
        public string SerialNumber { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", MessageId = "0")]
        public bool Equals(EmulatedRokuSettings other)
        {
            if (other == this)
            {
                return true;
            }
            if (other == null)
            {
                return false;
            }
            return other.Id == Id &&
                   other.Name == Name &&
                   other.SerialNumber == SerialNumber &&
                   other.AdvertiseAddress == AdvertiseAddress &&
                   other.RokuAddress == RokuAddress;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            EmulatedRokuSettings specificObj = obj as EmulatedRokuSettings;
            if (specificObj == null)
            {
                return false;
            }
            else
            {
                return Equals(specificObj);
            }
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() ^
                   Name.GetHashCode() ^
                   SerialNumber.GetHashCode() ^
                   AdvertiseAddress.GetHashCode() ^
                   RokuAddress.GetHashCode();
        }

        public const int DefaultPort = 8060;
    }
}