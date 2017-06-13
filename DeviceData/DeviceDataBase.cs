using HomeSeerAPI;
using Hspi.Devices;
using NullGuard;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    /// <summary>
    /// This is base class for creating and updating devices in HomeSeer.
    /// </summary>
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceDataBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceDataBase" /> class.
        /// </summary>
        /// <param name="name">Name of the Device</param>
        protected DeviceDataBase()
        {
        }

        /// <summary>
        /// Gets the status pairs for creating device.
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <returns></returns>
        public abstract IList<VSVGPairs.VSPair> StatusPairs { get; }

        /// <summary>
        /// Gets the graphics pairs for creating device
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <returns></returns>
        public abstract IList<VSVGPairs.VGPair> GraphicsPairs { get; }

        public abstract int HSDeviceType { get; }
        public virtual DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI DeviceAPI => DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI.Plug_In;
        public abstract string HSDeviceTypeString { get; }
        public abstract bool StatusDevice { get; }
        public int RefId { get; protected set; }

        /// <summary>
        /// Sets the initial data for the device.
        /// </summary>
        /// <param name="HS">The hs.</param>
        /// <param name="refId">The reference identifier.</param>
        public virtual void DeviceCreated(IHSApplication HS, int refId)
        {
            RefId = refId;
        }

        public virtual Task HandleCommand(DeviceControl connector, double value, CancellationToken token)
        {
            throw new System.NotImplementedException();
        }
    };
}