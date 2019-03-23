using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class SonyBluRayControl : IPAddressableDeviceControl
    {
        public SonyBluRayControl(string name, IPAddress deviceIP,
                                PhysicalAddress macAddress,
                                IPAddress wolBroadCastAddress,
                                TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider,
                                AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue, outofCommandDetectors)
        {
            this.wolBroadCastAddress = wolBroadCastAddress;
            MacAddress = macAddress;
            AddCommand(new DeviceCommand(CommandName.PowerOn, fixedValue: -200));
            AddCommand(new SonyBluRayCommand(CommandName.PowerOff, "AAAAAwAAHFoAAAAVAw==", -199));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue: -198));

            AddCommand(new SonyBluRayCommand(CommandName.CursorDown, "AAAAAwAAHFoAAAA6Aw==", -197));
            AddCommand(new SonyBluRayCommand(CommandName.CursorUp, "AAAAAwAAHFoAAAA5Aw==", -196));
            AddCommand(new SonyBluRayCommand(CommandName.CursorRight, "AAAAAwAAHFoAAAA8Aw==", -195));
            AddCommand(new SonyBluRayCommand(CommandName.CursorLeft, "AAAAAwAAHFoAAAA7Aw==", -194));
            AddCommand(new SonyBluRayCommand(CommandName.Enter, "AAAAAwAAHFoAAAA9Aw==", -193));
            AddCommand(new SonyBluRayCommand(CommandName.Return, "AAAAAwAAHFoAAABDAw==", -192));
            AddCommand(new SonyBluRayCommand(CommandName.MediaPlay, "AAAAAwAAHFoAAAAaAw==", -191));
            AddCommand(new SonyBluRayCommand(CommandName.MediaStop, "AAAAAwAAHFoAAAAYAw==", -190));
            AddCommand(new SonyBluRayCommand(CommandName.MediaRewind, "AAAAAwAAHFoAAAAbAw==", -189));
            AddCommand(new SonyBluRayCommand(CommandName.MediaFastForward, "AAAAAwAAHFoAAAAcAw==", -188));
            AddCommand(new SonyBluRayCommand(CommandName.MediaStepForward, "AAAAAwAAHFoAAABWAw==", -187));
            AddCommand(new SonyBluRayCommand(CommandName.MediaStepBackward, "AAAAAwAAHFoAAABXAw==", -186));
            AddCommand(new SonyBluRayCommand(CommandName.Home, "AAAAAwAAHFoAAABCAw==", -185));
            AddCommand(new SonyBluRayCommand(CommandName.Info, "AAAAAwAAHFoAAABBAw==", -183));
            AddCommand(new SonyBluRayCommand(CommandName.AudioTrack, "AAAAAwAAHFoAAABkAw==", -182));
            AddCommand(new SonyBluRayCommand(CommandName.Subtitle, "AAAAAwAAHFoAAABjAw==", -181));
            AddCommand(new SonyBluRayCommand(CommandName.Options, "AAAAAwAAHFoAAAA/Aw==", -180));
            AddCommand(new SonyBluRayCommand(CommandName.Eject, "AAAAAwAAHFoAAAAWAw==", -179));
            AddCommand(new SonyBluRayCommand(CommandName.MediaSkipBackward, "AAAAAwAAHFoAAAB2Aw==", -178));
            AddCommand(new SonyBluRayCommand(CommandName.MediaSkipForward, "AAAAAwAAHFoAAAB1Aw==", -177));
            AddCommand(new SonyBluRayCommand(CommandName.Menu, "AAAAAwAAHFoAAAApAw==", -176));
            AddCommand(new SonyBluRayCommand(CommandName.MediaPause, "AAAAAwAAHFoAAAAZAw==", -175));
            AddCommand(new SonyBluRayCommand(CommandName.PopupMenu, "AAAAAwAAHFoAAAApAw==", -174));
            AddCommand(new SonyBluRayCommand(CommandName.MediaPlayPause, "AAAAAwAAHFoAAAAZAw==", -173));  // same as pause

            string[] digitCommands = new string[10]
            {
                "AAAAAwAAHFoAAAAJAw==",
                "AAAAAwAAHFoAAAAAAw==",
                "AAAAAwAAHFoAAAABAw==",
                "AAAAAwAAHFoAAAACAw==",
                "AAAAAwAAHFoAAAADAw==",
                "AAAAAwAAHFoAAAAEAw==",
                "AAAAAwAAHFoAAAAFAw==",
                "AAAAAwAAHFoAAAAGAw==",
                "AAAAAwAAHFoAAAAHAw==",
                "AAAAAwAAHFoAAAAIAw==",
            };

            for (int i = 0; i <= 9; i++)
            {
                AddCommand(new SonyBluRayCommand(i.ToString(CultureInfo.InvariantCulture), digitCommands[i], -1000 + i));
            }

            AddCommand(new DeviceCommand(CommandName.CursorUpEventDown, fixedValue: 2000));
            AddCommand(new DeviceCommand(CommandName.CursorUpEventUp, fixedValue: 2001));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventDown, fixedValue: 2002));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventUp, fixedValue: 2003));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventDown, fixedValue: 2004));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventUp, fixedValue: 2005));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventDown, fixedValue: 2006));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventUp, fixedValue: 2007));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));

            UriBuilder uriBuilder = new UriBuilder
            {
                Host = deviceIP.ToString(),
                Port = Port,
                Scheme = "http"
            };

            client.BaseAddress = uriBuilder.Uri;
        }

        public override bool InvalidState => false;
        public PhysicalAddress MacAddress { get; }

        public override async Task Refresh(CancellationToken token)
        {
            await ExecuteCommand(GetCommand(CommandName.PowerQuery), token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cursorCancelLoopSource?.Cancel();
                client.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override async Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to Sony Blu Ray {Name} on {DeviceIP}"));

            switch (command.Id)
            {
                case CommandName.PowerOn:
                    await NetworkHelper.SendWolAsync(new IPEndPoint(wolBroadCastAddress, 9), MacAddress, token).ConfigureAwait(false);
                    break;

                case CommandName.PowerQuery:
                    await UpdatePowerFeedbackState(token).ConfigureAwait(false);
                    break;

                case CommandName.CursorDownEventDown:
                    MacroStartCommandLoop(CommandName.CursorDown);
                    break;

                case CommandName.CursorLeftEventDown:
                    MacroStartCommandLoop(CommandName.CursorLeft);
                    break;

                case CommandName.CursorRightEventDown:
                    MacroStartCommandLoop(CommandName.CursorRight);
                    break;

                case CommandName.CursorUpEventDown:
                    MacroStartCommandLoop(CommandName.CursorUp);
                    break;

                case CommandName.CursorLeftEventUp:
                case CommandName.CursorRightEventUp:
                case CommandName.CursorUpEventUp:
                case CommandName.CursorDownEventUp:
                    MacroStopCommandLoop(ref cursorCancelLoopSource);
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsPoweredOn(token).ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"Sony Blu Ray {Name} on {DeviceIP} not powered On");
            }

            await UpdateFeedback(FeedbackName.Power, true, token).ConfigureAwait(false);
            Trace.WriteLine(Invariant($"Connected to Sony Blu Ray {Name} on {DeviceIP}"));
            await UpdateConnectedState(true, token).ConfigureAwait(false);
        }

        private async Task<bool> IsPoweredOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingHost(DeviceIP, Port, networkPingTimeout, token).ConfigureAwait(false);
        }

        private void MacroStartCommandLoop(string commandId)
        {
            MacroStartCommandLoop(commandId, ref cursorCancelLoopSource);
        }

        private async Task SendCommandCore(string commandData, CancellationToken token)
        {
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/upnp/control/IRCC?"))
                {
                    request.Content = new StringContent(commandData, Encoding.UTF8, "text/xml");//CONTENT-TYPE header
                    request.Headers.Add("SOAPACTION", "\"urn:schemas-sony-com:service:IRCC:1#X_SendIRCC\"");

                    HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        string output = Encoding.UTF8.GetString(responseBytes, 0, responseBytes.Length - 1);
                        Trace.WriteLine(Invariant($"Feedback from ADB Device {Name}:[{output}]"));
                    }
                    else
                    {
                        throw new DeviceException(Invariant($"Failed command to {Name} with [{response.StatusCode}] "));
                    }
                }
            }
        }

        private async Task UpdatePowerFeedbackState(CancellationToken token)
        {
            await UpdateFeedback(FeedbackName.Power,
                           await IsPoweredOn(token).ConfigureAwait(false),
                           token).ConfigureAwait(false);
        }

        private const int Port = 50001;

        private static readonly List<OutofOrderCommandDetector> outofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.CursorDownEventDown, CommandName.CursorDownEventUp),
            new OutofOrderCommandDetector(CommandName.CursorUpEventDown, CommandName.CursorUpEventUp),
            new OutofOrderCommandDetector(CommandName.CursorRightEventDown, CommandName.CursorRightEventUp),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventDown, CommandName.CursorLeftEventUp),
        };

        private readonly HttpClient client = new HttpClient();
        private readonly AsyncLock connectionLock = new AsyncLock();
        private readonly IPAddress wolBroadCastAddress;
        private CancellationTokenSource cursorCancelLoopSource;

        private class SonyBluRayCommand : DeviceCommand
        {
            public SonyBluRayCommand(string id, string command, int? fixedValue)
                : base(id, MakeCommand(command), DeviceCommandType.Control, fixedValue)
            {
            }

            private static string MakeCommand(string command)
            {
                StringBuilder stb = new StringBuilder();
                stb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                stb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
                stb.Append("<s:Body>");
                stb.Append("<u:X_SendIRCC xmlns:u=\"urn:schemas-sony-com:service:IRCC:1\">");
                stb.AppendFormat(CultureInfo.InvariantCulture, "<IRCCCode>{0}</IRCCCode>", command);
                stb.Append("</u:X_SendIRCC>");
                stb.Append("</s:Body>");
                stb.Append("</s:Envelope>");
                return stb.ToString();
            }
        }
    }
}