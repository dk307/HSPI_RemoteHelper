using Hspi.Devices;
using Nito.AsyncEx;
using NullGuard;
using Scheduler;
using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    // nvidia shield 2015
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class ADBRemoteControl : IPAddressableDeviceControl
    {
        public ADBRemoteControl(string name, IPAddress deviceIP,
                                string adbPath, TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider,
                                AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue, adbOutofCommandDetectors)
        {
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(Invariant($"ADB Exe {adbPath} not found"), adbPath);
            }

            this.adbPath = adbPath;
            AddCommand(new ADBShellKeyEventCommand(CommandName.AudioTrack, AdbShellKeys.KEYCODE_MEDIA_AUDIO_TRACK, -100));
            AddCommand(new ADBShellDDCommand(CommandName.CursorDown, DirectInputKeys.KEY_DOWN, -99));
            AddCommand(new ADBShellDDCommand(CommandName.CursorLeft, DirectInputKeys.KEY_LEFT, -98));
            AddCommand(new ADBShellDDCommand(CommandName.CursorRight, DirectInputKeys.KEY_RIGHT, -5));
            AddCommand(new ADBShellDDCommand(CommandName.CursorUp, DirectInputKeys.KEY_UP, -96));
            AddCommand(new ADBShellDDCommand(CommandName.Enter, DirectInputKeys.KEY_ENTER, -95));
            AddCommand(new ADBShellDDCommand(CommandName.Home, DirectInputKeys.KEY_HOMEPAGE, -94));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Info, AdbShellKeys.KEYCODE_INFO, -93));
            AddCommand(new ADBShellDDCommand(CommandName.MediaFastForward, DirectInputKeys.KEY_FASTFORWARD, -92));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaNext, AdbShellKeys.KEYCODE_MEDIA_NEXT, -91));
            AddCommand(new ADBShellDDCommand(CommandName.MediaPlayPause, DirectInputKeys.KEY_PLAYPAUSE, -90));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaPrevious, AdbShellKeys.KEYCODE_MEDIA_PREVIOUS, -89));
            AddCommand(new ADBShellDDCommand(CommandName.MediaRewind, DirectInputKeys.KEY_REWIND, -88));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipBackward, DirectInputKeys.KEY_PREVIOUSSONG, -87));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipForward, DirectInputKeys.KEY_NEXTSONG, -86));
            AddCommand(new ADBShellDDCommand(CommandName.MediaStop, DirectInputKeys.KEY_STOP, -85));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOff, AdbShellKeys.KEYCODE_SLEEP, -84));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOn, AdbShellKeys.KEYCODE_WAKEUP, -83));
            AddCommand(new DeviceCommand(CommandName.PowerQuery, fixedValue:-82));
            AddCommand(new ADBShellDDCommand(CommandName.Return, DirectInputKeys.KEY_BACK, -81));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Subtitle, AdbShellKeys.KEYCODE_CAPTIONS, -80));

            AddCommand(new DeviceCommand(CommandName.ScreenQuery, fixedValue: -79));
            AddCommand(new DeviceCommand(CommandName.ScreenSaveRunningQuery, fixedValue: -78));
            AddCommand(new DeviceCommand(CommandName.CurrentStatusQuery, fixedValue: -77));

            AddCommand(new DeviceCommand(CommandName.CursorUpEventDown, fixedValue: -71));
            AddCommand(new DeviceCommand(CommandName.CursorUpEventUp, fixedValue: -70));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventDown, fixedValue: -69));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventUp, fixedValue: -68));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventDown, fixedValue: -67));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventUp, fixedValue: -66));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventDown, fixedValue: -65));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventUp, fixedValue: -64));

            AddCommand(new DeviceCommand(CommandName.BackspaceEventUp, fixedValue: -61));
            AddCommand(new DeviceCommand(CommandName.BackspaceEventDown, fixedValue: -60));

            AddKeyboardCommands(1000);

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Screen, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.ScreenSaverRunning, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.CurrentApplication, TypeCode.String));
            AddFeedback(new MediaStateDeviceFeedback(FeedbackName.MediaState));
            AddFeedback(new DeviceFeedback(FeedbackName.MediaTitle, TypeCode.String));
 
            StartServer();
        }

        public override bool InvalidState
        {
            get
            {
                AdbServerStatus status = AdbServer.Instance.GetStatus();
                if (!status.IsRunning)
                {
                    return true;
                }

                if (adbClient != null)
                {
                    return GetOnlineDevice() == null;
                }

                return false;
            }
        }

        public override async Task Refresh(CancellationToken token)
        {
            await ExecuteCommand(GetCommand(CommandName.CurrentStatusQuery), token).ConfigureAwait(false);
            await ExecuteCommand(GetCommand(CommandName.ScreenSaveRunningQuery), token).ConfigureAwait(false);
            await ExecuteCommand(GetCommand(CommandName.ScreenQuery), token).ConfigureAwait(false);
            await UpdateDirectKeyDevices(true, token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cursorCancelLoopSource?.Cancel();
                DisposeConnection();
            }

            base.Dispose(disposing);
        }

        protected override async Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            try
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Andriod Device {Name} on {DeviceIP}"));
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                string output;
                switch (command.Id)
                {
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

                    case CommandName.BackspaceEventDown:
                        MacroStartCommandLoop(CommandName.Backspace);
                        break;

                    case CommandName.CursorLeftEventUp:
                    case CommandName.CursorRightEventUp:
                    case CommandName.CursorUpEventUp:
                    case CommandName.CursorDownEventUp:
                    case CommandName.BackspaceEventUp:
                        MacroStopCommandLoop(ref cursorCancelLoopSource);
                        break;

                    case CommandName.PowerQuery:
                        if (!await IsPoweredOn(token).ConfigureAwait(false))
                        {
                            await UpdateFeedback(FeedbackName.Power, false, token).ConfigureAwait(false);
                            break;
                        }
                        await UpdateFeedback(FeedbackName.Power, await CheckScreenOn(token).ConfigureAwait(false), token).ConfigureAwait(false);
                        break;

                    case CommandName.PowerOff:
                        await SendCommandCore(command.Data, token).ConfigureAwait(false);
                        break;

                    case CommandName.ScreenQuery:
                        await UpdateFeedback(FeedbackName.Screen,
                                             await CheckScreenOn(token).ConfigureAwait(false),
                                             token).ConfigureAwait(false);
                        break;

                    case CommandName.ScreenSaveRunningQuery:
                        output = await SendCommandCore("dumpsys power | grep \"mWakefulness\"", token).ConfigureAwait(false);
                        await UpdateFeedback(FeedbackName.ScreenSaverRunning, !output.Contains("Awake"), token).ConfigureAwait(false);
                        break;

                    case CommandName.CurrentStatusQuery:
                        await QueryCurrentStatus(token).ConfigureAwait(false);
                        break;

                    case CommandName.MediaPlay:
                    case CommandName.MediaPause:
                    case CommandName.MediaPlayPause:
                    case CommandName.Home:
                        await SendCommandCore(command, token).ConfigureAwait(false);

                        // set a loop to update current application
                        UpdateStatusInLoop();
                        break;


                    default:
                        await SendCommandCore(command, token).ConfigureAwait(false);
                        break;
                }

                Trace.WriteLine(Invariant($"Executing {command.Id} took {stopWatch.Elapsed} on Andriod Device {Name} on {DeviceIP}"));
            }
            catch
            {
                DisposeConnection();
                throw;
            }
        }

        private void UpdateStatusInLoop()
        {
            queryRunningApplicationTokenSource?.Cancel();
            queryRunningApplicationTokenSource = new CancellationTokenSource();
            queryRunningApplicationTokenSource.CancelAfter(10000);
            StartCommandLoop(GetCommand(CommandName.CurrentStatusQuery),
                                 TimeSpan.FromSeconds(1), queryRunningApplicationTokenSource.Token);
        }

        protected override async Task UpdateConnectedState(bool value, CancellationToken token)
        {
            if (!value)
            {
                directKeysDevices = null;
            }

            await base.UpdateConnectedState(value, token).ConfigureAwait(false);
        }

        private void AddKeyboardCommands(int start)
        {
            DirectInputKeys[] numberKeys = new DirectInputKeys[]
            {
                DirectInputKeys.KEY_0,
                DirectInputKeys.KEY_1,
                DirectInputKeys.KEY_2,
                DirectInputKeys.KEY_3,
                DirectInputKeys.KEY_4,
                DirectInputKeys.KEY_5,
                DirectInputKeys.KEY_6,
                DirectInputKeys.KEY_7,
                DirectInputKeys.KEY_8,
                DirectInputKeys.KEY_9,
            };

            for (char c = '0'; c <= '9'; c++)
            {
                AddCommand(new ADBShellDDCommand(c.ToString(CultureInfo.InvariantCulture), numberKeys[c - '0'], start++));
            }

            DirectInputKeys[] charKeys = new DirectInputKeys[]
           {
                DirectInputKeys.KEY_A,
                DirectInputKeys.KEY_B,
                DirectInputKeys.KEY_C,
                DirectInputKeys.KEY_D,
                DirectInputKeys.KEY_E,
                DirectInputKeys.KEY_F,
                DirectInputKeys.KEY_G,
                DirectInputKeys.KEY_H,
                DirectInputKeys.KEY_I,
                DirectInputKeys.KEY_J,
                DirectInputKeys.KEY_K,
                DirectInputKeys.KEY_L,
                DirectInputKeys.KEY_M,
                DirectInputKeys.KEY_N,
                DirectInputKeys.KEY_O,
                DirectInputKeys.KEY_P,
                DirectInputKeys.KEY_Q,
                DirectInputKeys.KEY_R,
                DirectInputKeys.KEY_S,
                DirectInputKeys.KEY_T,
                DirectInputKeys.KEY_U,
                DirectInputKeys.KEY_V,
                DirectInputKeys.KEY_W,
                DirectInputKeys.KEY_X,
                DirectInputKeys.KEY_Y,
                DirectInputKeys.KEY_Z,
           };

            for (char c = 'a'; c <= 'z'; c++)
            {
                AddCommand(new ADBShellDDCommand(c.ToString(CultureInfo.InvariantCulture), charKeys[c - 'a'], start++));
            }

            for (char c = 'A'; c <= 'Z'; c++)
            {
                AddCommand(new ADBShellDDCommand(c.ToString(CultureInfo.InvariantCulture), charKeys[c - 'A'], start++, DirectInputKeys.KEY_LEFTSHIFT));
            }

            AddCommand(new ADBShellDDCommand("Space", DirectInputKeys.KEY_SPACE, start++));
            AddCommand(new ADBShellDDCommand(CommandName.Backspace, DirectInputKeys.KEY_BACKSPACE, start++));

            AddCommand(new ADBShellDDCommand("!", DirectInputKeys.KEY_1, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(",", DirectInputKeys.KEY_COMMA, start++));
            AddCommand(new ADBShellDDCommand("?", DirectInputKeys.KEY_SLASH, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(".", DirectInputKeys.KEY_DOT, start++));
            AddCommand(new ADBShellDDCommand("~", DirectInputKeys.KEY_GRAVE, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(";", DirectInputKeys.KEY_SEMICOLON, start++));
            AddCommand(new ADBShellDDCommand("'", DirectInputKeys.KEY_APOSTROPHE, start++));
            AddCommand(new ADBShellDDCommand("^", DirectInputKeys.KEY_6, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("*", DirectInputKeys.KEY_7, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("%", DirectInputKeys.KEY_5, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("@", DirectInputKeys.KEY_2, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("&", DirectInputKeys.KEY_7, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("#", DirectInputKeys.KEY_3, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("=", DirectInputKeys.KEY_EQUAL, start++));
            AddCommand(new ADBShellDDCommand("+", DirectInputKeys.KEY_EQUAL, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(":", DirectInputKeys.KEY_SEMICOLON, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("/", DirectInputKeys.KEY_SLASH, start++));
            AddCommand(new ADBShellDDCommand("\\", DirectInputKeys.KEY_BACKSLASH, start++));
            AddCommand(new ADBShellDDCommand("_", DirectInputKeys.KEY_MINUS, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("-", DirectInputKeys.KEY_MINUS, start++));
            AddCommand(new ADBShellDDCommand("|", DirectInputKeys.KEY_BACKSLASH, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("{", DirectInputKeys.KEY_LEFTBRACE, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("}", DirectInputKeys.KEY_RIGHTBRACE, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("[", DirectInputKeys.KEY_LEFTBRACE, start++));
            AddCommand(new ADBShellDDCommand("]", DirectInputKeys.KEY_RIGHTBRACE, start++));
            AddCommand(new ADBShellDDCommand("(", DirectInputKeys.KEY_9, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(")", DirectInputKeys.KEY_0, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("\"", DirectInputKeys.KEY_APOSTROPHE, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("<", DirectInputKeys.KEY_COMMA, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(">", DirectInputKeys.KEY_DOT, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("`", DirectInputKeys.KEY_GRAVE, start++));
            AddCommand(new ADBShellDDCommand("$", DirectInputKeys.KEY_4, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellCharCommand("€", '€', start++));
            AddCommand(new ADBShellCharCommand("£", '£', start++));
            AddCommand(new ADBShellCharCommand("¥", '¥', start++));

            AddCommand(new ADBShellDDCommand("F1", DirectInputKeys.KEY_F1, start++));
            AddCommand(new ADBShellDDCommand("F2", DirectInputKeys.KEY_F2, start++));
            AddCommand(new ADBShellDDCommand("F3", DirectInputKeys.KEY_F3, start++));
            AddCommand(new ADBShellDDCommand("F4", DirectInputKeys.KEY_F4, start++));
            AddCommand(new ADBShellDDCommand("F5", DirectInputKeys.KEY_F5, start++));
            AddCommand(new ADBShellDDCommand("F6", DirectInputKeys.KEY_F6, start++));
            AddCommand(new ADBShellDDCommand("F7", DirectInputKeys.KEY_F7, start++));
            AddCommand(new ADBShellDDCommand("F8", DirectInputKeys.KEY_F8, start++));
        }

        private async Task<bool> CheckScreenOn(CancellationToken token)
        {
            string output = await SendCommandCore("dumpsys power | grep \"Display Power\"", token).ConfigureAwait(false);
            return output.Contains("state=ON");
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsPoweredOn(token).ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"Andriod Device {Name} on {DeviceIP} not powered On");
            }
            StartServer();

            adbClient = new AdbClient();

#pragma warning disable CA2000 // Dispose objects before losing scope
            monitor = new DeviceMonitor(new AdbSocket(adbClient.EndPoint));
#pragma warning restore CA2000 // Dispose objects before losing scope
            monitor.DeviceDisconnected += Monitor_DeviceDisconnected;
            monitor.DeviceChanged += Monitor_DeviceChanged;
            monitor.Start();

            adbClient.Connect(DeviceIP);

            var device = GetOnlineDevice();

            int retries = 20;
            while ((device == null) && (retries > 0))
            {
                await Task.Delay(50, token).ConfigureAwait(false);
                device = GetOnlineDevice();
                retries--;
            }

            await UpdateConnectedState(device != null, token).ConfigureAwait(false);
            if (device == null)
            {
                Trace.TraceWarning(Invariant($"Failed to connect to Andriod Device {Name} on {DeviceIP}"));
            }
        }

        private void DisposeConnection()
        {
            queryRunningApplicationTokenSource?.Cancel();
            if (adbClient != null)
            {
                monitor?.Dispose();
                //do not set null
            }
        }

        private SharpAdbClient.DeviceData GetDevice()
        {
            return adbClient?.GetDevices()?.Where((x) =>
                            x.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal)).SingleOrDefault();
        }

        private SharpAdbClient.DeviceData GetOnlineDevice()
        {
            SharpAdbClient.DeviceData device = GetDevice();

            if (device != null)
            {
                if (device.State == DeviceState.Online)
                {
                    return device;
                }
            }

            return null;
        }

        private async Task<bool> IsPoweredOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitAsync(token).ConfigureAwait(false);
        }

        private void MacroStartCommandLoop(string commandId)
        {
            MacroStartCommandLoop(commandId, ref cursorCancelLoopSource);
        }

        private async void Monitor_DeviceChanged(object sender, DeviceDataEventArgs e)
        {
            if (e.Device.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal))
            {
                var device = GetDevice();
                if (device.State != DeviceState.Online)
                {
                    Trace.TraceInformation(Invariant($"State for Connection to Andriod Device {Name} on {DeviceIP} changed to {device.State}"));
                    await UpdateConnectedState(false, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
        {
            if (e.Device.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal))
            {
                Trace.TraceInformation(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
                await UpdateConnectedState(false, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task QueryCurrentStatus(CancellationToken token)
        {

            string[] commands = new string[] { @"CURRENT_APP=$(dumpsys window windows | grep mCurrentFocus) && CURRENT_APP=${CURRENT_APP#*{* * } && CURRENT_APP=${CURRENT_APP%%/*} && echo $CURRENT_APP",
                                               @"dumpsys media_session | grep -A 100 'Sessions Stack' | grep -A 100 $CURRENT_APP | grep -m 1 'state=PlaybackState {'",
                                               @"dumpsys media_session | grep -A 100 'Sessions Stack' | grep -A 100 $CURRENT_APP | grep -m 1 'metadata:'" };

            string output = await SendCommandCore(string.Join( @" && ", commands), token).ConfigureAwait(false);

            var lines = output.Split( new char[] {'\r'});

            string application = string.Empty;
            string applicationName = null;
            if (lines.Length > 0)
            {
                application = lines[0].Trim();
                if (!androidApplicationNames.TryGetValue(application, out applicationName))
                {
                    applicationName = application;
                }
        
            }

            await UpdateFeedback(FeedbackName.CurrentApplication, applicationName, token).ConfigureAwait(false);

            string input = (lines.Length > 1) ? lines[1] : string.Empty;
            var mediaState = DetermineMediaState(application, input);
            await UpdateFeedback(FeedbackName.MediaState, (double)mediaState, token).ConfigureAwait(false);


            string title = string.Empty;
            string titleReturn = (lines.Length > 2) ? lines[2] : string.Empty;

            var matches = getMediaTitleRegEx.Match(titleReturn);
            if (matches.Success)
            {
                var value = matches.Groups["description"];
                if (value.Success)
                {
                    title = value.Value != "null" ? value.Value : string.Empty;
                }
            }

            await UpdateFeedback(FeedbackName.MediaTitle, title, token).ConfigureAwait(false);
        }

        private static MediaStateDeviceFeedback.State DetermineMediaState(string application, 
                                                                          string input)
        {
            switch (application)
            {
                case launcherApplication:
                case settingApplication:
                    return MediaStateDeviceFeedback.State.Stopped;  
            }

            var mediaState = Hspi.Devices.MediaStateDeviceFeedback.State.Unknown;
            var matches = getMediaStateRegEx.Match(input);

            if (matches.Success)
            {
                var stateGroup = matches.Groups["state"];
                if (stateGroup.Success)
                {
                    if (int.TryParse(stateGroup.Value, out int state))
                    {
                        switch (application)
                        {
                            case netflixApplication:
                            case kodiApplication:
                            case youtubeApplication:
                            case amazonVideoApplication:
                            case disneyPlusApplication:
                            case hotstarApplication:
                            switch (state)
                            {
                                case 2: mediaState = MediaStateDeviceFeedback.State.Paused; break;
                                case 3: mediaState = MediaStateDeviceFeedback.State.Playing; break;
                                default:
                                    mediaState = MediaStateDeviceFeedback.State.Stopped; break;
                            }
                            break;
                        }
                    }
                }
            }

            return mediaState;
        }

        private async Task SendCommandCore(DeviceCommand command, CancellationToken token)
        {
            var adbShellDDCommand = command as ADBShellDDCommand;
            if (adbShellDDCommand != null)
            {
                Func<CancellationToken, Task<string>> getCommand = async (token2) =>
                {
                    if (directKeysDevices == null)
                    {
                        await UpdateDirectKeyDevices(false, token2).ConfigureAwait(false);
                    }

                    if (directKeysDevices != null &&
                        directKeysDevices.TryGetValue((int)adbShellDDCommand.DirectInputKey, out int deviceId))
                    {
                        return command.Data + deviceId.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        throw new DeviceException(Invariant($"No device found for {command.Id}"));
                    }
                };

                await SendCommandCore(getCommand, token).ConfigureAwait(false);
            }
            else
            {
                await SendCommandCore(command.Data, token).ConfigureAwait(false);
            }
        }

        private async Task<string> SendCommandCore(string commandData, CancellationToken token)
        {
            return await SendCommandCore((x) => Task.FromResult(commandData), token).ConfigureAwait(false);
        }

        private async Task<string> SendCommandCore(Func<CancellationToken, Task<string>> commandData,
                                                   CancellationToken token)
        {
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                return await SendCommandCoreNoLock(commandData, token).ConfigureAwait(false);
            }
        }

        private async Task<string> SendCommandCoreNoLock(Func<CancellationToken, Task<string>> commandData,
                                                         CancellationToken token)
        {
            if (!Connected)
            {
                await Connect(token).ConfigureAwait(false);
            }

            ConsoleOutputReceiver receiver = new ConsoleOutputReceiver()
            {
                TrimLines = true
            };

            SharpAdbClient.DeviceData device = GetOnlineDevice();

            if (device == null)
            {
                await UpdateConnectedState(false, token).ConfigureAwait(false);
                throw new DeviceException(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
            }

            using (CancellationTokenSource timedCancel = new CancellationTokenSource())
            {
                timedCancel.CancelAfter(TimeSpan.FromSeconds(10));
                string command = await commandData(token).ConfigureAwait(false);
                Trace.WriteLine(Invariant($"Sending {command} to Andriod Device {Name}"));

                await adbClient.ExecuteRemoteCommandAsync(command, device, receiver, timedCancel.Token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            string output = receiver.ToString();
            Trace.WriteLine(Invariant($"Feedback from ADB Device {Name}:[{output}]"));

            receiver.ThrowOnError(output);
            return output;
        }

        private void StartServer()
        {
            AdbServerStatus status = AdbServer.Instance.GetStatus();
            if (!status.IsRunning)
            {
                Trace.WriteLine(Invariant($"Starting local adb server"));
                StartServerResult result = AdbServer.Instance.StartServer(adbPath, true);
                Trace.WriteLine(Invariant($"Started local adb server with result: {result}"));
            }
        }

        private async Task UpdateDirectKeyDevices(bool takeLock, CancellationToken token)
        {
            const string getEventCommand = "getevent -i";
            string output =
                takeLock ? await SendCommandCore(getEventCommand, token).ConfigureAwait(false)
                : await SendCommandCoreNoLock((x) => Task.FromResult(getEventCommand), token).ConfigureAwait(false);

            var deviceKeys = new Dictionary<int, StringBuilder>();

            var lines = output.Split(new[] { Environment.NewLine, "\n", "\r" },
                                           StringSplitOptions.RemoveEmptyEntries);

            StringBuilder builder = null;
            foreach (var line in lines)
            {
                var matches = getEventAddDeviceRegEx.Match(line);
                if (matches.Success)
                {
                    var packageGroup = matches.Groups["inputId"];
                    if (packageGroup.Success)
                    {
                        if (int.TryParse(packageGroup.Value, out int deviceId))
                        {
                            builder = new StringBuilder();
                            deviceKeys.Add(deviceId, builder);
                        }
                    }
                }

                if (builder != null)
                {
                    builder.Append(line);
                }
            }

            var deviceKeys2 = new Dictionary<int, int>();

            var devices = deviceKeys.Keys.ToList();
            devices.Sort();

            foreach (var device in devices)
            {
                var deviceValue = deviceKeys[device].ToString();

                var keysSet = new HashSet<int>();
                var matches = getEventKeysRegEx.Match(deviceValue);
                if (matches.Success)
                {
                    var packageGroup = matches.Groups["keys"];
                    if (packageGroup.Success)
                    {
                        string keysFullString = packageGroup.Value;

                        var keysString = keysFullString.Split(new[] { " ", "\t" },
                                                              StringSplitOptions.RemoveEmptyEntries);

                        foreach (var keyString in keysString)
                        {
                            if (int.TryParse(keyString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var key))
                            {
                                if (!deviceKeys2.ContainsKey(key))
                                {
                                    deviceKeys2.Add(key, device);
                                }
                            }
                        }
                    }
                }
            }

            directKeysDevices = deviceKeys2.ToImmutableSortedDictionary();
        }

        private const string netflixApplication = "com.netflix.ninja";
        private const string kodiApplication = "org.xbmc.kodi";
        private const string launcherApplication = "com.google.android.tvlauncher";
        private const string settingApplication = "com.android.tv.settings";
        private const string youtubeApplication = "com.google.android.youtube.tv";
        private const string amazonVideoApplication = "com.amazon.amazonvideo.livingroom";
        private const string disneyPlusApplication = "com.disney.disneyplus";
        private const string hotstarApplication = "in.startv.hotstar";
        private static readonly List<OutofOrderCommandDetector> adbOutofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.CursorDownEventDown, CommandName.CursorDownEventUp),
            new OutofOrderCommandDetector(CommandName.CursorUpEventDown, CommandName.CursorUpEventUp),
            new OutofOrderCommandDetector(CommandName.CursorRightEventDown, CommandName.CursorRightEventUp),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventDown, CommandName.CursorLeftEventUp),
        };

        private static readonly Regex getEventAddDeviceRegEx = new Regex(@"add device \d+: /dev/input/event(?<inputId>\d+)",
                                                              RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex getEventKeysRegEx = new Regex(@"^.*events:\s*KEY\s*\(\d*\):(?<keys>[0-9|A-F|\s]*).*$",
                                                            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex getMediaStateRegEx = new Regex(@"state=(?<state>[0-9]+)",
                                                            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

        private static readonly Regex getMediaTitleRegEx = new Regex(@", description=(?<description>.*),.*,.*$",
                                                            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

        private readonly string adbPath;
        private readonly AsyncLock connectionLock = new AsyncLock();
        private AdbClient adbClient;
        private CancellationTokenSource cursorCancelLoopSource;
        private volatile ImmutableSortedDictionary<int, int> directKeysDevices;
        private DeviceMonitor monitor;
#pragma warning disable CA2213 // Disposable fields should be disposed
        private CancellationTokenSource queryRunningApplicationTokenSource;
#pragma warning restore CA2213 // Disposable fields should be disposed
        private readonly ImmutableDictionary<string, string> androidApplicationNames = new Dictionary<string, string>() { 
                {launcherApplication, "Launcher"},
                {settingApplication, "Settings"},
                {"com.google.android.apps.mediashell", "Google Cast"},
                {"com.hulu.plus", "Hulu"},
                {kodiApplication, "Kodi"},
                {netflixApplication, "Netflix"},
                {"com.plexapp.android", "Plex"},
                {"org.videolan.vlc", "VLC"},
                {youtubeApplication, "Youtube"},
                {hotstarApplication, "Hotstar"},
                {disneyPlusApplication, "Disney+"},
                {"com.google.android.youtube.tvkids", "Youtube Kids"},
                {amazonVideoApplication, "Amazon Prime"},
        }.ToImmutableDictionary();

    }
}