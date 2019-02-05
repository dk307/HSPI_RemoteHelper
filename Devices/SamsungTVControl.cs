using Nito.AsyncEx;
using NullGuard;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class SamsungTVControl : IPAddressableDeviceControl
    {
        public SamsungTVControl(string name, IPAddress deviceIP,
                                PhysicalAddress macAddress,
                                IPAddress wolBroadCastAddress,
                                TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider)
        {
            //"KEY_0", "KEY_1", "KEY_2", "KEY_3", "KEY_4", "KEY_5", "KEY_6", "KEY_7", "KEY_8", "KEY_9",
            //"KEY_BLUE", "KEY_CH_LIST", "KEY_CHDOWN", "KEY_CHUP", "KEY_CONTENTS",
            //"KEY_DASH", "KEY_DOWN", "KEY_ENTER", "KEY_EXIT", "KEY_FF", "KEY_GREEN", "KEY_INFO", "KEY_LEFT",
            //"KEY_MENU", "KEY_MUTE", "KEY_PAUSE", "KEY_PLAY", "KEY_POWEROFF", "KEY_PRECH", "KEY_REC",
            //"KEY_RED", "KEY_RETURN", "KEY_REWIND", "KEY_RIGHT", "KEY_SOURCE", "KEY_STOP", "KEY_TOOLS",
            //"KEY_UP", "KEY_VOLDOWN", "KEY_VOLUP", "KEY_YELLOW"

            this.wolBroadCastAddress = wolBroadCastAddress;
            MacAddress = macAddress;
            AddCommand(new DeviceCommand(CommandName.PowerOn));
            AddCommand(new DeviceCommand(CommandName.PowerOff));
            AddCommand(new DeviceCommand(CommandName.PowerQuery));

            AddCommand(new DeviceCommand(CommandName.VolumeUp, "KEY_VOLUP"));
            AddCommand(new DeviceCommand(CommandName.VolumeDown, "KEY_VOLDOWN"));
            AddCommand(new DeviceCommand(CommandName.MuteToggle, "KEY_MUTE"));

            AddCommand(new DeviceCommand(CommandName.SourceSelect, "KEY_SOURCE"));

            AddCommand(new DeviceCommand(CommandName.Menu, "KEY_MENU"));
            AddCommand(new DeviceCommand(CommandName.CursorDown, "KEY_DOWN"));
            AddCommand(new DeviceCommand(CommandName.CursorUp, "KEY_UP"));
            AddCommand(new DeviceCommand(CommandName.CursorRight, "KEY_RIGHT"));
            AddCommand(new DeviceCommand(CommandName.CursorLeft, "KEY_LEFT"));
            AddCommand(new DeviceCommand(CommandName.Enter, "KEY_ENTER"));
            AddCommand(new DeviceCommand(CommandName.Exit, "KEY_EXIT"));
            AddCommand(new DeviceCommand(CommandName.TVAVRInput));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
        }

        public override bool InvalidState
        {
            get
            {
                // Never connected
                if (webSocket == null)
                {
                    return true;
                }
                return (webSocket.State != WebSocketState.Open);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public PhysicalAddress MacAddress { get; }

        public override Task Refresh(CancellationToken token)
        {
            return RefreshImpl(token);
        }

        public async Task RefreshImpl(CancellationToken token)
        {
            await ExecuteCommand(GetCommand(CommandName.PowerQuery), token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeWebSocket();
            }
            base.Dispose(disposing);
        }

        protected override Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore2(command, token);
        }

        private static T DeserializeJson<T>(string result) where T : class
        {
            using (MemoryStream memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(result)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(memoryStream) as T;
            }
        }

        private async Task Connect(CancellationToken token)
        {
            string nameBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(AppName));
            string socketsUrl = Invariant($"ws://{DeviceIP}:{TVPort}/api/v2/channels/samsung.remote.control?Name={nameBase64}");
            webSocket = new WebSocket(socketsUrl, "basic")
            {
                NoDelay = true,
                EnableAutoSendPing = true,
                AutoSendPingInterval = 1
            };
            webSocket.Opened += WebSocket_Opened;
            webSocket.MessageReceived += WebSocket_MessageReceived;
            webSocket.Error += WebSocket_Error;
            webSocket.Closed += WebSocket_Closed;
            webSocket.Open();

            await connectedSource.Task.WaitAsync(token).ConfigureAwait(false);
            Trace.WriteLine(Invariant($"Connected to Samsung TV {Name} on {DeviceIP}"));
            UpdateFeedback(FeedbackName.Power, true);
            UpdateConnectedState(true);
        }

        private void DisposeWebSocket()
        {
            if (webSocket != null)
            {
                webSocket.Close();
                webSocket.Opened -= WebSocket_Opened;
                webSocket.MessageReceived -= WebSocket_MessageReceived;
                webSocket.Error -= WebSocket_Error;
                webSocket.Closed -= WebSocket_Closed;
                webSocket.Dispose();
            }
        }

        private async Task ExecuteCommandCore2(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to Samsung TV {Name} on {DeviceIP}"));

            switch (command.Id)
            {
                case CommandName.PowerOn:
                    //var task1 = NetworkHelper.SendWolAsync(new IPEndPoint(wolBroadCastAddress, 9), MacAddress, token);
                    var task2 = SendIRCommandCore("Samsung TV - POWER ON", token);
                    await Task.WhenAll(task2).ConfigureAwait(false);
                    break;

                case CommandName.PowerOff:
                    await SendIRCommandCore("Samsung TV - POWER OFF", token).ConfigureAwait(false);
                    shutdownTime = DateTimeOffset.Now;
                    break;

                case CommandName.PowerQuery:
                    await UpdatePowerFeedbackState(token).ConfigureAwait(false);
                    break;

                case CommandName.TVAVRInput:
                    await SendIRCommandCore("Samsung TV - INPUT HDMI 4", token).ConfigureAwait(false);
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }
        }

        private async Task<bool> IsPoweredOn(CancellationToken token)
        {
            // TV keeps reponding to Pings for 7s after it has been turned off
            if (shutdownTime.HasValue)
            {
                TimeSpan wait = DateTimeOffset.Now - shutdownTime.Value;
                if (wait <= TimeSpan.FromSeconds(7))
                {
                    return false;
                }
            }
            else
            {
                //first time start
                return false;
            }

            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(750);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitAsync(token).ConfigureAwait(false);
        }

        private async Task SendCommandCore(string commandData, CancellationToken token)
        {
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                string commandJson = Invariant($"{{\"method\":\"ms.remote.control\",\"params\":{{\"Cmd\":\"Click\",\"DataOfCmd\":\"{commandData}\",\"Option\":\"false\",\"TypeOfRemote\":\"SendRemoteKey\"}}}}");
                webSocket.Send(commandJson);
            }
        }

        private async Task SendIRCommandCore(string commandId, CancellationToken token)
        {
            Connector.IDeviceCommandHandler connector = ConnectionProvider.GetCommandHandler(DeviceType.IP2IR);
            await connector.HandleCommand(commandId, token).ConfigureAwait(false);
        }

        private async Task UpdatePowerFeedbackState(CancellationToken token = default(CancellationToken))
        {
            UpdateFeedback(FeedbackName.Power, await IsPoweredOn(token).ConfigureAwait(false));
        }

        private void WebSocket_Closed(object sender, EventArgs e)
        {
            Trace.WriteLine(Invariant($"Connection to Samsung TV {Name} on {DeviceIP} Closed"));
            UpdateConnectedState(false);
            Task.Run(() => UpdatePowerFeedbackState());
        }

        private void WebSocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            Trace.WriteLine(Invariant($"Connection to Samsung TV {Name} on {DeviceIP} Errored with {e.Exception.Message}"));
            UpdateConnectedState(false);
            Task.Run(() => UpdatePowerFeedbackState());
        }

        private void WebSocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            string message = e.Message;
            ConnectionResponse response = DeserializeJson<ConnectionResponse>(message);

            switch (response.Event)
            {
                case "ms.channel.connect":
                    connectedSource.TrySetResult(true);
                    break;

                case "ms.channel.clientDisconnect":
                    UpdateConnectedState(false);
                    UpdateFeedback(FeedbackName.Power, false);
                    Task.Run(() => DisposeWebSocket());
                    connectedSource.TrySetException(new DeviceException(Invariant($"Connection Closed with {response.Event}")));
                    break;

                case "ms.channel.timeOut":
                case "ms.channel.unauthorized":
                default:
                    connectedSource.TrySetException(new DeviceException(Invariant($"Connection Failed with {response.Event}")));
                    break;
            }
        }

        private void WebSocket_Opened(object sender, EventArgs e)
        {
            Trace.WriteLine(Invariant($"Initial Reply from Samsung TV {Name} on {DeviceIP}"));
        }

        private const int TVPort = 8001;
        private readonly string AppName = "HomeSeer";
        private readonly TaskCompletionSource<bool> connectedSource = new TaskCompletionSource<bool>();
        private readonly AsyncLock connectionLock = new AsyncLock();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private readonly IPAddress wolBroadCastAddress;

        private static DateTimeOffset? shutdownTime;
        private WebSocket webSocket;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
        [DataContract]
        private class ConnectionResponse
        {
            [DataMember(Name = "event")]
            public string Event;
        }
    }
}