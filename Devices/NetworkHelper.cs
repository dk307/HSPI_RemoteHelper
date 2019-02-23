using Nito.AsyncEx;
using NullGuard;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal static class NetworkHelper
    {
        public static void CloseConnection(this TcpClient tcpClient)
        {
            tcpClient.GetStream().Close();
            tcpClient.Close();
        }

        /// <summary>
        /// Using IOControl code to configue socket KeepAliveValues for line disconnection detection(because default is too slow)
        /// </summary>
        /// <param name="tcpc">TcpClient</param>
        /// <param name="KeepAliveTime">The keep alive time. (ms)</param>
        /// <param name="KeepAliveInterval">The keep alive interval. (ms)</param>
        public static void SetSocketKeepAliveValues(this TcpClient tcpc, int KeepAliveTime, int KeepAliveInterval)
        {
            //KeepAliveTime: default value is 2hr
            //KeepAliveInterval: default value is 1s and Detect 5 times

            uint dummy = 0; //length = 4
            byte[] inOptionValues = new byte[Marshal.SizeOf(dummy) * 3]; //size = lenth * 3 = 12
            bool OnOff = true;

            BitConverter.GetBytes((uint)(OnOff ? 1 : 0)).CopyTo(inOptionValues, 0);
            BitConverter.GetBytes((uint)KeepAliveTime).CopyTo(inOptionValues, Marshal.SizeOf(dummy));
            BitConverter.GetBytes((uint)KeepAliveInterval).CopyTo(inOptionValues, Marshal.SizeOf(dummy) * 2);

            tcpc.Client.IOControl(IOControlCode.KeepAliveValues, inOptionValues, null);
        }

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
                //Uses the Socket returned by Client to set an option that is not available using UdpClient.
                cl.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
                cl.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, 1);

                var sendTask = cl.SendAsync(packet, packet.Length, target);
                await sendTask.WaitAsync(token).ConfigureAwait(false);
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