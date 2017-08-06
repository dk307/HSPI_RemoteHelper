using HomeSeerAPI;
using Hspi.Devices;
using Hspi.Exceptions;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceRootDeviceManager
    {
        public DeviceRootDeviceManager(string deviceName, DeviceType deviceType, IHSApplication HS, ILogger logger)
        {
            this.deviceName = deviceName;
            this.logger = logger;
            this.HS = HS;
            this.deviceType = deviceType;
            GetCurrentDevices();
        }

        public void CreateOrUpdateDevices(IEnumerable<DeviceCommand> commands,
                                          IEnumerable<DeviceFeedback> feedbacks)
        {
            // create root
            if (parentDeviceData == null)
            {
                string parentAddress = DeviceIdentifier.CreateRootAddress(deviceType);
                parentDeviceData = new RootDeviceData();
                CreateDevice(null, deviceName, parentAddress, parentDeviceData);
            }

            // update commands in Root
            var pairs = parentDeviceData.GetStatusPairs(commands);
            HS.DeviceVSP_ClearAll(parentDeviceData.RefId, true);
            foreach (var pair in pairs)
            {
                HS.DeviceVSP_AddPair(parentDeviceData.RefId, pair);
            }

            foreach (var feedback in feedbacks)
            {
                var deviceIdentifier = new DeviceIdentifier(deviceType, feedback.Id);
                string address = deviceIdentifier.Address;
                if (!currentChildDevices.ContainsKey(address))
                {
                    CreateFeedbackDevice(feedback, deviceIdentifier);
                }
            }
        }

        public async Task HandleCommand([AllowNull]DeviceIdentifier deviceIdentifier, DeviceControl connector,
                                         double value, CancellationToken token)
        {
            if (deviceIdentifier == null)
            {
                await parentDeviceData.HandleCommand(connector, value, token).ConfigureAwait(false);
            }
            else
            {
                if (currentChildDevices.TryGetValue(deviceIdentifier.Address, out var device))
                {
                    await device.HandleCommand(connector, value, token).ConfigureAwait(false);
                }
            }
        }

        public void ProcessFeedback(FeedbackValue feedbackData)
        {
            var deviceIdentifier = new DeviceIdentifier(deviceType, feedbackData.Feedback.Id);
            string address = deviceIdentifier.Address;
            if (currentChildDevices.TryGetValue(address, out var feedbackDevice))
            {
                feedbackDevice.UpdateValue(HS, feedbackData.Value);
            }
        }

        private static FeedbackDeviceData GetDevice(DeviceFeedback feedback, int? refId)
        {
            switch (feedback)
            {
                case SettableRangedDeviceFeedback rangeFeedback:
                    return new SettableRangedFeedbackDeviceData(refId, rangeFeedback);

                default:
                    switch (feedback.Typecode)
                    {
                        case TypeCode.SByte:
                        case TypeCode.Byte:
                        case TypeCode.Int16:
                        case TypeCode.UInt16:
                        case TypeCode.Int32:
                        case TypeCode.UInt32:
                        case TypeCode.Int64:
                        case TypeCode.UInt64:
                        case TypeCode.Single:
                        case TypeCode.Double:
                        case TypeCode.Decimal:
                            return new DoubleFeedbackDeviceData(refId);

                        case TypeCode.Boolean:
                            return new BoolFeedbackDeviceData(refId);

                        case TypeCode.Empty:
                        case TypeCode.Object:
                        case TypeCode.DBNull:
                        case TypeCode.Char:
                        case TypeCode.DateTime:
                        case TypeCode.String:
                            return new StringFeedbackDeviceData(refId);

                        default:
                            throw new ArgumentOutOfRangeException(nameof(feedback));
                    }
            }
        }

        /// <summary>
        /// Creates the HS device.
        /// </summary>
        /// <param name="optionalParentRefId">The optional parent reference identifier.</param>
        /// <param name="name">The name of device</param>
        /// <param name="deviceAddress">The device address.</param>
        /// <param name="deviceData">The device data.</param>
        /// <returns>
        /// New Device
        /// </returns>
        private DeviceClass CreateDevice(int? optionalParentRefId, string name, string deviceAddress, DeviceDataBase deviceData)
        {
            logger.LogDebug(Invariant($"Creating Device with Address:{deviceAddress}"));

            DeviceClass device = null;
            int refId = HS.NewDeviceRef(name);
            if (refId > 0)
            {
                device = (DeviceClass)HS.GetDeviceByRef(refId);
                string address = deviceAddress;
                device.set_Address(HS, address);
                device.set_Device_Type_String(HS, deviceData.HSDeviceTypeString);
                var hsDeviceType = new DeviceTypeInfo_m.DeviceTypeInfo();
                hsDeviceType.Device_API = deviceData.DeviceAPI;
                hsDeviceType.Device_Type = deviceData.HSDeviceType;

                device.set_DeviceType_Set(HS, hsDeviceType);
                device.set_Interface(HS, PluginData.PluginName);
                device.set_InterfaceInstance(HS, string.Empty);
                device.set_Last_Change(HS, DateTime.Now);
                device.set_Location(HS, PluginData.PluginName);

                device.MISC_Set(HS, Enums.dvMISC.SHOW_VALUES);
                device.MISC_Set(HS, Enums.dvMISC.SET_DOES_NOT_CHANGE_LAST_CHANGE);
                if (deviceData.StatusDevice)
                {
                    device.MISC_Set(HS, Enums.dvMISC.STATUS_ONLY);
                    device.MISC_Clear(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                    device.set_Status_Support(HS, false);
                }
                else
                {
                    device.MISC_Set(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                    device.set_Status_Support(HS, true);
                }

                var pairs = deviceData.StatusPairs;
                foreach (var pair in pairs)
                {
                    HS.DeviceVSP_AddPair(refId, pair);
                }

                var gPairs = deviceData.GraphicsPairs;
                foreach (var gpair in gPairs)
                {
                    HS.DeviceVGP_AddPair(refId, gpair);
                }

                DeviceClass parent = null;
                if (optionalParentRefId.HasValue)
                {
                    parent = (DeviceClass)HS.GetDeviceByRef(optionalParentRefId.Value);
                }

                if (parent != null)
                {
                    parent.set_Relationship(HS, Enums.eRelationship.Parent_Root);
                    device.set_Relationship(HS, Enums.eRelationship.Child);
                    device.AssociatedDevice_Add(HS, parent.get_Ref(HS));
                    parent.AssociatedDevice_Add(HS, device.get_Ref(HS));
                }

                deviceData.DeviceCreated(HS, refId);
            }

            return device;
        }

        internal void ProcessCommand(DeviceCommand command)
        {
            if (parentDeviceData != null)
            {
                parentDeviceData.UpdateRootValue(HS, command);
            }
        }

        private void CreateFeedbackDevice(DeviceFeedback feedback, DeviceIdentifier deviceIdentifier)
        {
            string address = deviceIdentifier.Address;
            var childDevice = GetDevice(feedback, null);
            string childDeviceName = Invariant($"{Invariant($"{deviceName} {deviceIdentifier.FeedbackName}")}");
            var childHSDevice = CreateDevice(parentDeviceData.RefId, childDeviceName, address, childDevice);

            var data = new PlugExtraData.clsPlugExtraData();

            byte[] byteData;
            IFormatter formatter = new BinaryFormatter();

            using (MemoryStream stream = new MemoryStream())
            {
                formatter.Serialize(stream, feedback);
                byteData = stream.ToArray();
            }

            data.AddUnNamed(byteData);

            childHSDevice.set_PlugExtraData_Set(HS, data);
            currentChildDevices[address] = childDevice;
        }

        private void GetCurrentDevices()
        {
            var deviceEnumerator = HS.GetDeviceEnumerator() as clsDeviceEnumeration;

            if (deviceEnumerator == null)
            {
                throw new HspiException(Invariant($"{PluginData.PluginName} failed to get a device enumerator from HomeSeer."));
            }

            string parentAddress = DeviceIdentifier.CreateRootAddress(deviceType);
            do
            {
                DeviceClass device = deviceEnumerator.GetNext();
                if ((device != null) &&
                    (device.get_Interface(HS) != null) &&
                    (device.get_Interface(HS).Trim() == PluginData.PluginName))
                {
                    string address = device.get_Address(HS);
                    if (address == parentAddress)
                    {
                        parentDeviceData = new RootDeviceData(device.get_Ref(null));
                    }
                    else if (address.StartsWith(parentAddress, StringComparison.Ordinal))
                    {
                        FeedbackDeviceData childDeviceData = GetDeviceData(device);
                        if (childDeviceData != null)
                        {
                            currentChildDevices.Add(address, childDeviceData);
                        }
                    }
                }
            } while (!deviceEnumerator.Finished);
        }

        private static FeedbackDeviceData GetDeviceData(DeviceClass hsDevice)
        {
            var id = DeviceIdentifier.Identify(hsDevice);
            if (id == null)
            {
                return null;
            }

            var deviceData = hsDevice.get_PlugExtraData_Get(null);
            object data = deviceData.GetUnNamed(0);

            using (MemoryStream stream = new MemoryStream((byte[])data))
            {
                IFormatter formatter = new BinaryFormatter();
                DeviceFeedback feedback = (DeviceFeedback)formatter.Deserialize(stream);

                var device = GetDevice(feedback, hsDevice.get_Ref(null));
                return device;
            }
        }

        private readonly IDictionary<string, FeedbackDeviceData> currentChildDevices = new Dictionary<string, FeedbackDeviceData>();
        private readonly string deviceName;
        private readonly IHSApplication HS;
        private readonly ILogger logger;
        private readonly DeviceType deviceType;
        private RootDeviceData parentDeviceData;
    };
}