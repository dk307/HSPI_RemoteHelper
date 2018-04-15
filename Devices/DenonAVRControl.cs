using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using static System.FormattableString;

    internal sealed class DenonAVRControl : IPAddressableDeviceControl
    {
        public DenonAVRControl(string name, IPAddress deviceIP,
                                TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, avrOutofCommandDetectors)
        {
            AddDynamicVolumeCommands(-500);
            AddDialogEnhancerCommands(-400);
            AddSubwooferLevelCommands(-300);
            AddSurrondModeCommands(-200);

            AddCommand(new DeviceCommand(CommandName.InputStatusQuery, "SI?", fixedValue: -100));
            AddCommand(new DeviceCommand(CommandName.PowerOff, "PWSTANDBY", fixedValue: -96));
            AddCommand(new DeviceCommand(CommandName.PowerOn, "PWON", fixedValue: -95));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, "PW?", fixedValue: -94));

            AddVolumeCommands();

            AddCommand(new DeviceCommand(CommandName.AudysseyQuery, "PSMULTEQ: ?", fixedValue: -83));
            AddCommand(new DeviceCommand(CommandName.ChangeInputMPLAY, "SIMPLAY", fixedValue: -82));
            AddCommand(new DeviceCommand(CommandName.AllStatusQuery, string.Empty, fixedValue: -77));
            AddCommand(new DeviceCommand(CommandName.ChangeInputGAME2, "SIGAME2", fixedValue: -76));
            AddCommand(new DeviceCommand(CommandName.ChangeInputBD, "SIBD", fixedValue: -75));
            AddCommand(new DeviceCommand(CommandName.ChangeInputCD, "SICD", fixedValue: -74));

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
            AddFeedback(new DeviceFeedback(FeedbackName.DynamicVolume, TypeCode.String));
        }

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

        protected override Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore2(command, token);
        }

        private async Task ExecuteCommandCore2(DeviceCommand command, CancellationToken token)
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
                                               CommandName.DynamicVolumeQuery,
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

                case CommandName.MacroStartVolumeUpLoop:
                    MacroStartCommandLoop(CommandName.VolumeUp, ref volumeCancelSource);
                    break;

                case CommandName.MacroStartVolumeDownLoop:
                    MacroStartCommandLoop(CommandName.VolumeDown, ref volumeCancelSource);
                    break;

                case CommandName.MacroStopVolumeUpLoop:
                case CommandName.MacroStopVolumeDownLoop:
                    MacroStopCommandLoop(ref volumeCancelSource);
                    break;

                case CommandName.MacroStartSubwooferLevelDownLoop:
                    MacroStartCommandLoop(CommandName.SubWooferLevelDown, ref volumeCancelSource);
                    break;

                case CommandName.MacroStartSubwooferLevelUpLoop:
                    MacroStartCommandLoop(CommandName.SubWooferLevelUp, ref volumeCancelSource);
                    break;

                case CommandName.MacroStopSubwooferLevelDownLoop:
                case CommandName.MacroStopSubwooferLevelUpLoop:
                    MacroStopCommandLoop(ref volumeCancelSource);
                    break;

                case CommandName.MacroStartDialogEnhancerDownLoop:
                    MacroStartCommandLoop(CommandName.DialogEnhancerLevelDown, ref volumeCancelSource);
                    break;

                case CommandName.MacroStartDialogEnhancerUpLoop:
                    MacroStartCommandLoop(CommandName.DialogEnhancerLevelUp, ref volumeCancelSource);
                    break;

                case CommandName.MacroStopDialogEnhancerDownLoop:
                case CommandName.MacroStopDialogEnhancerUpLoop:
                    MacroStopCommandLoop(ref volumeCancelSource);
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            volumeCancelSource?.Cancel();
            stopTokenSource?.Cancel();
            if (disposing)
            {
                volumeCancelSource?.Dispose();
                DisposeConnection();
            }

            base.Dispose(disposing);
        }

        protected override string TranslateStringFeedback(string input)
        {
            switch (input)
            {
                case "LIT":
                    return "Low";

                case "MED":
                    return "Medium";

                case "HEV":
                    return "Heavy";

                case "NEURAL:X":
                    return "DTS Neural:X";

                case "MCH STEREO":
                    return "All Channel Stereo";

                case "MPLAY":
                    return NvidiaShieldInput;

                case "BD":
                    return BlueRayPlayerInput;

                case "AUX2":
                    return XBoxOneInput;

                case "CD":
                    return PS3Input;
            }
            return base.TranslateStringFeedback(input);
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
                await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false);

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

        private void AddDialogEnhancerCommands(int commandstart)
        {
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeOff, "PSDIL OFF", fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeOn, "PSDIL ON", fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerModeQuery, "PSDIL ?", fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerLevelUp, "PSDIL UP", fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.DialogEnhancerLevelDown, "PSDIL DOWN", fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.MacroStartDialogEnhancerUpLoop, fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.MacroStartDialogEnhancerDownLoop, fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.MacroStopDialogEnhancerUpLoop, fixedValue: commandstart++));
            AddCommand(new DeviceCommand(CommandName.MacroStopDialogEnhancerDownLoop, fixedValue: commandstart++));
        }

        private void AddDynamicVolumeCommands(int startLevel)
        {
            AddCommand(new DeviceCommand(CommandName.DynamicVolumeQuery, "PSDYNVOL ?", fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.DynamicVolumeOff, "PSDYNVOL OFF", fixedValue: startLevel++));

            var levels = new string[]
            {
                 "LIT", "MED", "HEV"
            };
            AddMultipleDeviceCommands("Dynamic Volume - ", "PSDYNVOL ", startLevel, levels);
        }

        private void AddMultipleDeviceCommands(string namePrefix, string commandPrefix, int fixedValueStart, string[] values)
        {
            foreach (var value in values)
            {
                AddCommand(new DeviceCommand(namePrefix + value, commandPrefix + value, fixedValue: fixedValueStart++));
            }
        }

        private void AddSubwooferLevelCommands(int startLevel)
        {
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustOff, "PSSWL OFF", fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustOn, "PSSWL ON", fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelAdjustQuery, "PSSWL ?", fixedValue: startLevel++));

            AddCommand(new DeviceCommand(CommandName.SubWooferLevelUp, "PSSWL UP", fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.SubWooferLevelDown, "PSSWL DOWN", fixedValue: startLevel++));

            AddCommand(new DeviceCommand(CommandName.MacroStartSubwooferLevelUpLoop, fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.MacroStartSubwooferLevelDownLoop, fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.MacroStopSubwooferLevelUpLoop, fixedValue: startLevel++));
            AddCommand(new DeviceCommand(CommandName.MacroStopSubwooferLevelDownLoop, fixedValue: startLevel++));
        }

        private void AddSurrondModeCommands(int fixedValueStart)
        {
            string namePrefix = "Surrond Mode - ";
            string commandPrefix = "MS";

            AddCommand(new DeviceCommand(CommandName.SoundModeQuery, "MS?", fixedValue: -93));

            var values = new string[]
            {
                "DIRECT", "PURE DIRECT", "STEREO", "AUTO", "DOLBY DIGITAL", "DTS SURROUND", "MCH STEREO",
                "ROCK ARENA", "JAZZ CLUB", "MONO MOVIE","MATRIX","VIDEO GAME", "VIRTUAL",
            };

            AddMultipleDeviceCommands(namePrefix, commandPrefix, fixedValueStart, values);
        }

        private void AddVolumeCommands()
        {
            AddCommand(new DeviceCommand(CommandName.VolumeDown, "MVDOWN", fixedValue: -89));
            AddCommand(new DeviceCommand(CommandName.VolumeUp, "MVUP", fixedValue: -88));
            AddCommand(new DeviceCommand(CommandName.VolumeQuery, "MV?", fixedValue: -87));
            AddCommand(new DeviceCommand(CommandName.MuteOn, "MUON", fixedValue: -86));
            AddCommand(new DeviceCommand(CommandName.MuteOff, "MUOFF", fixedValue: -85));
            AddCommand(new DeviceCommand(CommandName.MuteQuery, "MU?", fixedValue: -84));
            AddCommand(new DeviceCommand(CommandName.MacroStartVolumeUpLoop, fixedValue: -81));
            AddCommand(new DeviceCommand(CommandName.MacroStartVolumeDownLoop, fixedValue: -80));
            AddCommand(new DeviceCommand(CommandName.MacroStopVolumeUpLoop, fixedValue: -79));
            AddCommand(new DeviceCommand(CommandName.MacroStopVolumeDownLoop, fixedValue: -78));
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsNetworkOn(token).ConfigureAwait(false))
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

            await client.ConnectAsync(DeviceIP.ToString(), AVRPort).ConfigureAwait(false);
            UpdateConnectedState(true);

            client.SetSocketKeepAliveValues(10 * 1000, 1000);

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
            }
        }

        private async Task<bool> IsNetworkOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingHost(DeviceIP, 80, networkPingTimeout, token).ConfigureAwait(false);
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

                case var feedbackU2 when feedbackU2.StartsWith("PSDYNVOL ", StringComparison.Ordinal):
                    UpdateFeedback(FeedbackName.DynamicVolume, feedback.Substring(9));
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
                using (StreamReader reader = new StreamReader(stream, encoding))
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
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                byte[] bytesCommand = encoding.GetBytes(Invariant($"{commandData}{Seperator}"));

                // the reason we do not send cancellation token is to not break commands in between
                await stream.WriteAsync(bytesCommand, 0, bytesCommand.Length, default(CancellationToken)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
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
        public const string NvidiaShieldInput = "Nvidia Shield";
        public const string BlueRayPlayerInput = "Blu Ray Player";
        public const string XBoxOneInput = "XBox One";
        public const string PS3Input = "PS3";
        private const char Seperator = '\r';
        private readonly AsyncLock connectionLock = new AsyncLock();
        private readonly Encoding encoding = Encoding.ASCII;

        private static readonly List<OutofOrderCommandDetector> avrOutofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.MacroStartVolumeUpLoop, CommandName.MacroStopVolumeUpLoop),
            new OutofOrderCommandDetector(CommandName.MacroStartVolumeDownLoop, CommandName.MacroStopVolumeDownLoop),
            new OutofOrderCommandDetector(CommandName.MacroStartDialogEnhancerDownLoop, CommandName.MacroStopDialogEnhancerDownLoop),
            new OutofOrderCommandDetector(CommandName.MacroStartDialogEnhancerUpLoop, CommandName.MacroStopDialogEnhancerUpLoop),
            new OutofOrderCommandDetector(CommandName.MacroStartSubwooferLevelDownLoop, CommandName.MacroStopDialogEnhancerDownLoop),
            new OutofOrderCommandDetector(CommandName.MacroStartSubwooferLevelUpLoop, CommandName.MacroStopDialogEnhancerUpLoop),
        };

        private TcpClient client;
        private CancellationTokenSource combinedStopTokenSource;
        private CancellationTokenSource stopTokenSource;
        private NetworkStream stream;
        private CancellationTokenSource volumeCancelSource;
    }
}