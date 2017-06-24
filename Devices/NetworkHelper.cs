using NullGuard;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class NetworkHelper
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static async Task<bool> PingAddress(IPAddress ipAddress, TimeSpan? timeout = null)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(ipAddress,
                                                      timeout.HasValue ? (int)timeout.Value.TotalMilliseconds : 5000)
                                                      .ConfigureAwait(false);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch (PingException)
            {
                return false;
            }
        }

        public static async Task<bool> PingHost(IPAddress address, int port, TimeSpan timeout, CancellationToken token)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(address, port);

                    CancellationTokenSource source = new CancellationTokenSource();
                    source.CancelAfter(timeout);
                    var finishedTask = await Task.WhenAny(connectTask, Task.Delay(-1, source.Token));

                    if (connectTask == finishedTask)
                    {
                        return client.Connected;
                    }

                    token.ThrowIfCancellationRequested();

                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Sends a Wake On LAN signal (magic packet) to a client.</summary>
        /// <param name="target">Destination <see cref="IPEndPoint"/>.</param>
        /// <param name="macAddress">The MAC address of the designated client.</param>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="macAddress"/> is null.</exception>
        /// <returns>An asynchronous <see cref="Task"/> which sends a Wake On LAN signal (magic packet) to a client.</returns>
        public static async Task SendWolAsync(IPEndPoint target, PhysicalAddress macAddress, CancellationToken token)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (macAddress == null)
                throw new ArgumentNullException(nameof(macAddress));

            var packet = GetWolPacket(macAddress.GetAddressBytes());
            using (var cl = new UdpClient())
            {
                var sendTask = cl.SendAsync(packet, packet.Length, target);
                await sendTask.WaitOnRequestCompletion(token).ConfigureAwait(false);
            }
        }

        /// <exception cref="ArgumentNullException"><paramref name="macAddress"/> is null.</exception>
        /// <exception cref="ArgumentException">The length of the <see cref="T:System.Byte" /> array <paramref name="macAddress"/> is not 6.</exception>
        /// <exception cref="ArgumentException">The length of the <see cref="T:System.Byte" /> array <paramref name="password"/> is not 0 or 6.</exception>
        private static byte[] GetWolPacket(byte[] macAddress)
        {
            if (macAddress == null)
                throw new ArgumentNullException(nameof(macAddress));
            if (macAddress.Length != 6)
                throw new ArgumentException("Invalid Mac Address Length");

            var password = new byte[0];
            var packet = new byte[17 * 6 + password.Length];

            int offset, i;
            for (offset = 0; offset < 6; ++offset)
                packet[offset] = 0xFF;

            for (offset = 6; offset < 17 * 6; offset += 6)
                for (i = 0; i < 6; ++i)
                    packet[i + offset] = macAddress[i];

            if (password.Length > 0)
            {
                for (offset = 16 * 6 + 6; offset < (17 * 6 + password.Length); offset += 6)
                    for (i = 0; i < 6; ++i)
                        packet[i + offset] = password[i];
            }
            return packet;
        }
    }
}