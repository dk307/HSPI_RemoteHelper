using Rssdp.Infrastructure;
using System.Net;

namespace Rssdp
{
    /// <summary>
    /// Allows publishing devices both as notification and responses to search requests.
    /// </summary>
    /// <remarks>
    /// This is  the 'server' part of the system. You add your devices to an instance of this class so clients can find them.
    /// </remarks>
    internal class SsdpDevicePublisher : SsdpDevicePublisherBase
    {
        #region Constructors

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <remarks>
        /// <para>Uses the default <see cref="ISsdpCommunicationsServer"/> implementation and network settings for Windows and the SSDP specification.</para>
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "No way to do this here, and we don't want to dispose it except in the (rare) case of an exception anyway.")]
        public SsdpDevicePublisher()
            : this(new SsdpCommunicationsServer(new SocketFactory(null)))
        {
        }

        /// <summary>
        /// Partial constructor.
        /// </summary>
        /// <remarks>
        /// <para>Allows the caller to specify their own <see cref="ISsdpCommunicationsServer"/> implementation for full control over the networking, or for mocking/testing purposes..</para>
        /// </remarks>
        public SsdpDevicePublisher(ISsdpCommunicationsServer communicationsServer)
            : base(communicationsServer, new SsdpTraceLogger())
        {
        }

        /// <summary>
        /// Partial constructor.
        /// </summary>
        /// <param name="ipAddress">The IP address of the local network adapter to bind sockets to.
        /// Null or empty string will use <see cref="IPAddress.Any"/>.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "No way to do this here, and we don't want to dispose it except in the (rare) case of an exception anyway.")]
        public SsdpDevicePublisher(string ipAddress)
            : this(new SsdpCommunicationsServer(new SocketFactory(ipAddress)))
        {
        }

        #endregion Constructors
    }
}