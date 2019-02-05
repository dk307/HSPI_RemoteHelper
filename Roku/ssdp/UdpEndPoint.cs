﻿using System.Globalization;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Cross platform representation of a UDP end point, being an IP address (either IPv4 or IPv6) and a port.
    /// </summary>
    internal sealed class UdpEndPoint
    {
        /// <summary>
        /// The IP Address of the end point.
        /// </summary>
        /// <remarks>
        /// <para>Can be either IPv4 or IPv6, up to the code using this instance to determine which was provided.</para>
        /// </remarks>
        public string IPAddress { get; set; }

        /// <summary>
        /// The port of the end point.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Returns the <see cref="IPAddress"/> and <see cref="Port"/> values separated by a colon.
        /// </summary>
        /// <returns>A string containing <see cref="IPAddress"/>:<see cref="Port"/>.</returns>
        public override string ToString()
        {
            return (IPAddress ?? string.Empty) + ":" + Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}