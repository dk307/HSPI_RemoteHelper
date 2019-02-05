using Rssdp.Infrastructure;
using System;
using System.Xml;

namespace Rssdp
{
    /// <summary>
    /// Base class representing the common details of a (root or embedded) device, either to be published or that has been located.
    /// </summary>
    /// <remarks>
    /// <para>Do not derive new types directly from this class. New device classes should derive from either <see cref="SsdpRootDevice"/> or <see cref="SsdpEmbeddedDevice"/>.</para>
    /// </remarks>
    /// <seealso cref="SsdpRootDevice"/>
    /// <seealso cref="SsdpEmbeddedDevice"/>
    internal abstract class SsdpDevice
    {
        #region Fields

        private string _Udn;
        private string _DeviceType;
        private readonly string deviceDescriptionXml;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Deserialisation constructor.
        /// </summary>
        /// <remarks><para>Uses the provided XML string and parent device properties to set the properties of the object. The XML provided must be a valid UPnP device description document.</para></remarks>
        /// <param name="deviceDescriptionXml">A UPnP device description XML document.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if the <paramref name="deviceDescriptionXml"/> argument is null.</exception>
        /// <exception cref="System.ArgumentException">Thrown if the <paramref name="deviceDescriptionXml"/> argument is empty.</exception>
        protected SsdpDevice(string deviceDescriptionXml)
        {
            if (deviceDescriptionXml == null) throw new ArgumentNullException("deviceDescriptionXml");
            if (deviceDescriptionXml.Length == 0) throw new ArgumentException("deviceDescriptionXml cannot be an empty string.", "deviceDescriptionXml");

            _DeviceType = SsdpConstants.UpnpDeviceTypeBasicDevice;

            this.deviceDescriptionXml = deviceDescriptionXml;

            using (var ms = new System.IO.MemoryStream(System.Text.UTF8Encoding.UTF8.GetBytes(deviceDescriptionXml)))
            {
                var reader = XmlReader.Create(ms);
                LoadDeviceProperties(reader, this);
            }
        }

        #endregion Constructors

        #region Public Properties

        #region UPnP Device Description Properties

        /// <summary>
        /// Sets or returns the core device type (not including namespace, version etc.). Required.
        /// </summary>
        /// <remarks><para>Defaults to the UPnP basic device type.</para></remarks>
        /// <seealso cref="DeviceTypeNamespace"/>
        /// <seealso cref="DeviceVersion"/>
        /// <seealso cref="FullDeviceType"/>
        public string DeviceType
        {
            get
            {
                return _DeviceType;
            }
            set
            {
                _DeviceType = value;
            }
        }

        /// <summary>
        /// Returns the full device type string.
        /// </summary>
        /// <remarks>
        /// <para>The format used is urn:<see cref="DeviceTypeNamespace"/>:device:<see cref="DeviceType"/>:<see cref="DeviceVersion"/></para>
        /// </remarks>
        public string FullDeviceType
        {
            get
            {
                return DeviceType;
            }
        }

        /// <summary>
        /// Sets or returns the universally unique identifier for this device (without the uuid: prefix). Required.
        /// </summary>
        /// <remarks>
        /// <para>Must be the same over time for a specific device instance (i.e. must survive reboots).</para>
        /// <para>For UPnP 1.0 this can be any unique string. For UPnP 1.1 this should be a 128 bit number formatted in a specific way, preferably generated using the time and MAC based algorithm. See section 1.1.4 of http://upnp.org/specs/arch/UPnP-arch-DeviceArchitecture-v1.1.pdf for details.</para>
        /// <para>Technically this library implements UPnP 1.0, so any value is allowed, but we advise using UPnP 1.1 compatible values for good behaviour and forward compatibility with future versions.</para>
        /// </remarks>
        public string Uuid { get; set; }

        /// <summary>
        /// Returns (or sets*) a unique device name for this device. Optional, not recommended to be explicitly set.
        /// </summary>
        /// <remarks>
        /// <para>* In general you should not explicitly set this property. If it is not set (or set to null/empty string) the property will return a UDN value that is correct as per the UPnP specification, based on the other device properties.</para>
        /// <para>The setter is provided to allow for devices that do not correctly follow the specification (when we discover them), rather than to intentionally deviate from the specification.</para>
        /// <para>If a value is explicitly set, it is used verbatim, and so any prefix (such as uuid:) must be provided in the value.</para>
        /// </remarks>
        public string Udn
        {
            get
            {
                if (string.IsNullOrEmpty(_Udn) && !string.IsNullOrEmpty(Uuid))
                    return "uuid:" + Uuid;
                else
                    return _Udn;
            }
            set
            {
                _Udn = value;
            }
        }

        /// <summary>
        /// Sets or returns the serial number for this device. Recommended.
        /// </summary>
        public string SerialNumber { get; set; }

        #endregion UPnP Device Description Properties

        #endregion Public Properties

        #region Public Methods

        /// <summary>
        /// Writes this device to the specified <see cref="System.Xml.XmlWriter"/> as a device node and it's content.
        /// </summary>
        /// <param name="writer">The <see cref="System.Xml.XmlWriter"/> to output to.</param>
        /// <param name="device">The <see cref="SsdpDevice"/> to write out.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if the <paramref name="writer"/> or <paramref name="device"/> argument is null.</exception>
        protected virtual void WriteDeviceDescriptionXml(XmlWriter writer, SsdpDevice device)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (device == null) throw new ArgumentNullException("device");

            writer.WriteRaw(deviceDescriptionXml);
        }

        #endregion Public Methods

        #region Private Methods

        #region Deserialisation Methods

        private static void LoadDeviceProperties(XmlReader reader, SsdpDevice device)
        {
            ReadUntilDeviceNode(reader);

            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "device")
                {
                    reader.Read();
                    break;
                }

                if (!SetPropertyFromReader(reader, device))
                    reader.Read();
            }
        }

        private static void ReadUntilDeviceNode(XmlReader reader)
        {
            while (!reader.EOF && (reader.LocalName != "device" || reader.NodeType != XmlNodeType.Element))
            {
                reader.Read();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Yes, there is a large switch statement, not it's not really complex and doesn't really need to be rewritten at this point.")]
        private static bool SetPropertyFromReader(XmlReader reader, SsdpDevice device)
        {
            switch (reader.LocalName)
            {
                case "serialNumber":
                    device.SerialNumber = reader.ReadElementContentAsString();
                    break;

                case "UDN":
                    device.Udn = reader.ReadElementContentAsString();
                    SetUuidFromUdn(device);
                    break;

                case "deviceType":
                    device.DeviceType = reader.ReadElementContentAsString();
                    break;

                default:
                    return false;
            }
            return true;
        }

        private static void SetUuidFromUdn(SsdpDevice device)
        {
            if (device.Udn != null && device.Udn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
                device.Uuid = device.Udn.Substring(5).Trim();
            else
                device.Uuid = device.Udn;
        }

        #endregion Deserialisation Methods

        #endregion Private Methods
    }
}