using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace Hspi.Devices
{
    using static System.FormattableString;

    internal sealed class DenonAVRControl : IPAddressableDeviceControl
    {
        public DenonAVRControl(string name, IPAddress deviceIP) :
            base(name, deviceIP)
        {
            AddCommand(new DeviceCommand(CommandName.InputStatusQuery, "SI?"));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeOff, "PSDIL OFF"));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeOn, "PSDIL ON"));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeQuery, "PSDIL ?"));
            AddCommand(new DeviceCommand(CommandName.PowerOff, "PWSTANDBY"));
            AddCommand(new DeviceCommand(CommandName.PowerOn, "PWON"));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, "PW?"));
            AddCommand(new DeviceCommand(CommandName.SoundModeQuery, "MS?"));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustOff, "PSSWL OFF"));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustOn, "PSSWL ON"));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustQuery, "PSSWL ?"));
            AddCommand(new DeviceCommand(CommandName.VolumeDown, "MVDOWN"));
            AddCommand(new DeviceCommand(CommandName.VolumeUp, "MVUP"));
            AddCommand(new DeviceCommand(CommandName.VolumeQuery, "MV?"));
            AddCommand(new DeviceCommand(CommandName.MuteOn, "MUON"));
            AddCommand(new DeviceCommand(CommandName.MuteOff, "MUOFF"));
            AddCommand(new DeviceCommand(CommandName.MuteQuery, "MU?"));
            AddCommand(new DeviceCommand(CommandName.AudysseyQuery, "PSMULTEQ: ?"));
            AddCommand(new DeviceCommand(CommandName.ChangeInputMPLAY, "SIMPLAY"));
            //AddCommand(new DeviceCommand(CommandName.TesT, "PSCES OFF"));

            AddCommand(new DeviceCommand(CommandName.AllStatusQuery, string.Empty));

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Mute, TypeCode.Boolean));
            AddFeedback(new SettableRangedDeviceFeedback(FeedbackName.Volume, 0, 98, 1));
            AddFeedback(new DeviceFeedback(FeedbackName.Input, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.SoundMode, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.DialogEnhancementMode, TypeCode.Boolean));
            AddFeedback(new SettableRangedDeviceFeedback(FeedbackName.DialogEnhancementLevel, 38.5, 62, 1));
            AddFeedback(new DeviceFeedback(FeedbackName.SubwooferAdjustMode, TypeCode.Boolean));
            AddFeedback(new SettableRangedDeviceFeedback(FeedbackName.SubwooferAdjustLevel, 38.5, 62, 1));
            AddFeedback(new DeviceFeedback(FeedbackName.Audyssey, TypeCode.String));
        }

        public static TimeSpan DefaultCommandDelay => TimeSpan.FromMilliseconds(100);

        public override bool InvalidState
        {
            get
            {
                if (client != null)
                {
                    return !client.Connected;
                }
                return false;
            }
        }

        public override async Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Sending {command.Id} to Denon AVR {Name} on {DeviceIP}"));

            switch (command.Id)
            {
                case CommandName.PowerQuery:
                    if (!await IsNetworkOn(token).ConfigureAwait(false))
                    {
                        UpdateFeedback(FeedbackName.Power, false);
                        return;
                    }
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;

                case CommandName.AllStatusQuery:
                    string[] commandsQuery = { CommandName.InputStatusQuery,
                                               CommandName.SoundModeQuery,
                                               CommandName.DialogEnhancerModeQuery,
                                               CommandName.SubWooferLevelAdjustQuery,
                                               CommandName.AudysseyQuery,
                                               CommandName.PowerQuery,
                                               CommandName.MuteQuery,
                                               CommandName.VolumeQuery,
                    };

                    foreach (string macroCommand in commandsQuery)
                    {
                        await SendCommandForId(macroCommand, token).ConfigureAwait(false);
                        await Task.Delay(DefaultCommandDelay, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }
        }

        public override async Task ExecuteCommand(FeedbackValue value, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Setting {value.Feedback.Id} to Denon AVR {Name} on {DeviceIP} with value {value.Value}"));

            string commandData;
            switch (value.Feedback.Id)
            {
                case FeedbackName.Volume:
                    commandData = GetCommandForVolume("MV", value);
                    break;

                case FeedbackName.DialogEnhancementLevel:
                    commandData = GetCommandForVolume("PSDIL ", value);
                    break;

                case FeedbackName.SubwooferAdjustLevel:
                    commandData = GetCommandForVolume("PSSWL ", value);
                    break;

                default:
                    throw new DeviceException(Invariant($"Unknown {value.Feedback.Id} to Denon AVR {Name} on {DeviceIP} with value {value.Value}"));
            }

            await SendCommandCore(commandData, token).ConfigureAwait(false);
        }

        public async Task<bool> IsNetworkOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingHost(DeviceIP, 80, networkPingTimeout, token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            stopTokenSource?.Cancel();
            if (disposing)
            {
                DisposeConnection();
                clientWriteLock.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string GetCommandForVolume(string commandData, FeedbackValue value)
        {
            double doubleValue = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);

            // Round to 0.5
            doubleValue = Math.Round(doubleValue * 2, MidpointRounding.ToEven) / 2;

            if (Math.Truncate(doubleValue) == doubleValue)
            {
                return Invariant($"{commandData}{doubleValue:00}");
            }
            else
            {
                doubleValue *= 10;
                return Invariant($"{commandData}{doubleValue:000}");
            }
        }

        private static async Task<string> ReadLineAsync(StreamReader reader)
        {
            var sb = new StringBuilder();
            var buffer = new char[1];
            while (!reader.EndOfStream)
            {
                await reader.ReadAsync(buffer, 0, 1);

                if (buffer[0] == Seperator)
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(buffer[0]);
                }
            }

            return sb.ToString();
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsNetworkOn(token))
            {
                throw new DevicePoweredOffException($"Denon AVR {Name} on {DeviceIP} not powered On");
            }

            client = new TcpClient()
            {
                NoDelay = true,
            };

            stopTokenSource = new CancellationTokenSource();
            combinedStopTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stopTokenSource.Token, token);
            CancellationToken combinedToken = combinedStopTokenSource.Token;

            await client.ConnectAsync(DeviceIP.ToString(), AVRPort);
            UpdateConnectedState(true);

            stream = client.GetStream();
            Task readTask = Task.Factory.StartNew(() => ProcessRead(combinedToken),
                                 combinedToken,
                                 TaskCreationOptions.RunContinuationsAsynchronously,
                                 TaskScheduler.Current);
        }

        private void DisposeConnection()
        {
            if (client != null)
            {
                client.Dispose();
                client = null;
            }
        }

        private void ProcessFeedback(string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
            {
                return;
            }

            Trace.WriteLine(Invariant($"Feedback from Denon AVR {Name}:{feedback}"));

            string feedbackU = feedback.ToUpperInvariant();

            switch (feedbackU)
            {
                case "PWON":
                    UpdateFeedback(FeedbackName.Power, true);
                    break;

                case "PWOFF":
                case "PWSTANDBY":
                    UpdateFeedback(FeedbackName.Power, false);
                    break;

                case "MUON":
                    UpdateFeedback(FeedbackName.Mute, true);
                    break;

                case "MUOFF":
                    UpdateFeedback(FeedbackName.Mute, false);
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("MVMAX", StringComparison.Ordinal):
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("MV", StringComparison.Ordinal):
                    string volString = feedbackU.Substring(2);
                    UpdateFeedbackForVolumeString(FeedbackName.Volume, volString);
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("SI", StringComparison.Ordinal):
                    UpdateFeedback(FeedbackName.Input, feedback.Substring(2));
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("MS", StringComparison.Ordinal):
                    UpdateFeedback(FeedbackName.SoundMode, feedback.Substring(2));
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("PSMULTEQ:", StringComparison.Ordinal):
                    UpdateFeedback(FeedbackName.Audyssey, feedback.Substring(9));
                    break;

                case var feedbackU2 when feedbackU2.StartsWith("PSDIL", StringComparison.Ordinal):
                    string dialogEnhancementStatus = feedbackU2.Substring(5).Trim();
                    switch (dialogEnhancementStatus)
                    {
                        case "ON":
                            UpdateFeedback(FeedbackName.DialogEnhancementMode, true);
                            break;

                        case "OFF":
                            UpdateFeedback(FeedbackName.DialogEnhancementMode, false);
                            break;

                        default:
                            UpdateFeedbackForVolumeString(FeedbackName.DialogEnhancementLevel, dialogEnhancementStatus);
                            break;
                    }

                    break;

                case var feedbackU2 when feedbackU2.StartsWith("PSSWL", StringComparison.Ordinal):
                    string subwooferAdjustStatus = feedbackU2.Substring(5).Trim();
                    switch (subwooferAdjustStatus)
                    {
                        case "ON":
                            UpdateFeedback(FeedbackName.SubwooferAdjustMode, true);
                            break;

                        case "OFF":
                            UpdateFeedback(FeedbackName.SubwooferAdjustMode, false);
                            break;

                        default:
                            UpdateFeedbackForVolumeString(FeedbackName.SubwooferAdjustLevel, subwooferAdjustStatus);
                            break;
                    }
                    break;
            }
        }

        private async Task ProcessRead(CancellationToken combinedToken)
        {
            try
            {
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                {
                    while (!combinedToken.IsCancellationRequested)
                    {
                        string feedback = await ReadLineAsync(reader).ConfigureAwait(false);
                        ProcessFeedback(feedback);
                    }
                }
            }
            catch (IOException ex)
            {
                Trace.WriteLine(Invariant($"Connection to Denon AVR {Name} on {DeviceIP} dropped with {ExceptionHelper.GetFullMessage(ex)}"));
                UpdateConnectedState(false);
            }
            catch (ObjectDisposedException) { }
        }

        private async Task SendCommandCore(string commandData, CancellationToken token)
        {
            if (!Connected)
            {
                await Connect(token).ConfigureAwait(false);
            }

            byte[] bytesCommand = encoding.GetBytes(Invariant($"{commandData}{Seperator}"));
            await clientWriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(bytesCommand, 0, bytesCommand.Length, token).ConfigureAwait(false);
            }
            finally
            {
                clientWriteLock.Release();
            }
        }

        private async Task SendCommandForId(string commandId, CancellationToken token)
        {
            var command = GetCommand(commandId);
            await SendCommandCore(command.Data, token).ConfigureAwait(false);
        }

        private void UpdateFeedbackForVolumeString(string feedbackName, string volString)
        {
            while (volString.Length < 3)
            {
                volString += "0";
            }
            if (double.TryParse(volString, NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
            {
                volume = volume / 10;
                UpdateFeedback(feedbackName, volume);
            }
        }

        private const int AVRPort = 23;
        private const char Seperator = '\r';
        private readonly SemaphoreSlim clientWriteLock = new SemaphoreSlim(1);
        private readonly Encoding encoding = Encoding.ASCII;
        private TcpClient client;
        private CancellationTokenSource combinedStopTokenSource;
        private CancellationTokenSource stopTokenSource;
        private NetworkStream stream;
    }
}