using Nito.AsyncEx;
using NullGuard;
using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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
                                int defaultKeyboardDevice, int mediaKeyboardDevice,
                                IConnectionProvider connectionProvider) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, adbOutofCommandDetectors)
        {
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(Invariant($"ADB Exe {adbPath} not found"), adbPath);
            }

            this.adbPath = adbPath;
            this.defaultKeyboardDevice = defaultKeyboardDevice;
            this.mediaKeyboardDevice = mediaKeyboardDevice;
            AddCommand(new ADBShellKeyEventCommand(CommandName.AudioTrack, AdbShellKeys.KEYCODE_MEDIA_AUDIO_TRACK));
            AddCommand(new ADBShellDDCommand(CommandName.CursorDown, DirectInputKeys.KEY_DOWN, defaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorLeft, DirectInputKeys.KEY_LEFT, defaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorRight, DirectInputKeys.KEY_RIGHT, defaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorUp, DirectInputKeys.KEY_UP, defaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.Enter, DirectInputKeys.KEY_ENTER, defaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.Home, DirectInputKeys.KEY_HOMEPAGE, this.mediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Info, AdbShellKeys.KEYCODE_INFO));
            AddCommand(new ADBShellDDCommand(CommandName.MediaFastForward, DirectInputKeys.KEY_FASTFORWARD, mediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaNext, AdbShellKeys.KEYCODE_MEDIA_NEXT));
            AddCommand(new ADBShellDDCommand(CommandName.MediaPlayPause, DirectInputKeys.KEY_PLAYPAUSE, mediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaPrevious, AdbShellKeys.KEYCODE_MEDIA_PREVIOUS));
            AddCommand(new ADBShellDDCommand(CommandName.MediaRewind, DirectInputKeys.KEY_REWIND, mediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipBackward, DirectInputKeys.KEY_PREVIOUSSONG, mediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipForward, DirectInputKeys.KEY_NEXTSONG, mediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaStop, DirectInputKeys.KEY_STOP, mediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOff, AdbShellKeys.KEYCODE_SLEEP));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOn, AdbShellKeys.KEYCODE_WAKEUP));
            AddCommand(new DeviceCommand(CommandName.PowerQuery));
            AddCommand(new ADBShellDDCommand(CommandName.Return, DirectInputKeys.KEY_BACK, mediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Subtitle, AdbShellKeys.KEYCODE_CAPTIONS));

            AddCommand(new DeviceCommand(CommandName.ScreenQuery));
            AddCommand(new DeviceCommand(CommandName.ScreenSaveRunningQuery));
            AddCommand(new DeviceCommand(CommandName.CurrentApplicationQuery));

            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchNetflix, @"com.netflix.ninja"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchYoutube, @"com.google.android.youtube.tv"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchPlex, @"com.plexapp.android"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchAmazonVideo,
                                                 @"com.amazon.amazonvideo.livingroom.nvidia", @"com.amazon.ignition.IgnitionActivity"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchPBSKids, @"org.pbskids.video"));

            AddCommand(new DeviceCommand(CommandName.CursorUpEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorUpEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorDownEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorRightEventUp));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventDown));
            AddCommand(new DeviceCommand(CommandName.CursorLeftEventUp));

            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchSling, @"com.sling"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchKodi, @"org.xbmc.kodi"));

            AddCommand(new DeviceCommand(CommandName.BackspaceEventUp));
            AddCommand(new DeviceCommand(CommandName.BackspaceEventDown));

            AddKeyboardCommands(1000);

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Screen, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.ScreenSaverRunning, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.CurrentApplication, TypeCode.String));

            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchYouTubeKids, @"com.google.android.youtube.tvkids"));
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchHulu, @"com.hulu.livingroomplus"));
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

        public override Task Refresh(CancellationToken token)
        {
            return RefreshImpl(token);
        }

        public async Task RefreshImpl(CancellationToken token)
        {
            await ExecuteCommand(GetCommand(CommandName.CurrentApplicationQuery), token).ConfigureAwait(false);
            await ExecuteCommand(GetCommand(CommandName.ScreenSaveRunningQuery), token).ConfigureAwait(false);
            await ExecuteCommand(GetCommand(CommandName.ScreenQuery), token).ConfigureAwait(false);
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

        protected override Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            try
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Andriod Device {Name} on {DeviceIP}"));
                return SendCommand(command, token);
            }
            catch
            {
                DisposeConnection();
                throw;
            }
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
                AddCommand(new ADBShellDDCommand(c.ToString(), numberKeys[c - '0'], defaultKeyboardDevice, start++));
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
                AddCommand(new ADBShellDDCommand(c.ToString(), charKeys[c - 'a'], defaultKeyboardDevice, start++));
            }

            for (char c = 'A'; c <= 'Z'; c++)
            {
                AddCommand(new ADBShellDDCommand(c.ToString(), charKeys[c - 'A'], defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            }

            AddCommand(new ADBShellDDCommand("Space", DirectInputKeys.KEY_SPACE, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand(CommandName.Backspace, DirectInputKeys.KEY_BACKSPACE, defaultKeyboardDevice, start++));

            AddCommand(new ADBShellDDCommand("!", DirectInputKeys.KEY_1, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(",", DirectInputKeys.KEY_COMMA, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("?", DirectInputKeys.KEY_SLASH, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(".", DirectInputKeys.KEY_DOT, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("~", DirectInputKeys.KEY_GRAVE, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(";", DirectInputKeys.KEY_SEMICOLON, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("'", DirectInputKeys.KEY_APOSTROPHE, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("^", DirectInputKeys.KEY_6, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("*", DirectInputKeys.KEY_7, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("%", DirectInputKeys.KEY_5, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("@", DirectInputKeys.KEY_2, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("&", DirectInputKeys.KEY_7, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("#", DirectInputKeys.KEY_3, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("=", DirectInputKeys.KEY_EQUAL, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("+", DirectInputKeys.KEY_EQUAL, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(":", DirectInputKeys.KEY_SEMICOLON, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("/", DirectInputKeys.KEY_SLASH, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("\\", DirectInputKeys.KEY_BACKSLASH, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("_", DirectInputKeys.KEY_MINUS, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("-", DirectInputKeys.KEY_MINUS, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("|", DirectInputKeys.KEY_BACKSLASH, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("{", DirectInputKeys.KEY_LEFTBRACE, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("}", DirectInputKeys.KEY_RIGHTBRACE, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("[", DirectInputKeys.KEY_LEFTBRACE, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("]", DirectInputKeys.KEY_RIGHTBRACE, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("(", DirectInputKeys.KEY_9, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(")", DirectInputKeys.KEY_0, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("\"", DirectInputKeys.KEY_APOSTROPHE, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("<", DirectInputKeys.KEY_COMMA, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(">", DirectInputKeys.KEY_DOT, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("`", DirectInputKeys.KEY_GRAVE, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("$", DirectInputKeys.KEY_4, defaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellCharCommand("€", '€', start++));
            AddCommand(new ADBShellCharCommand("£", '£', start++));
            AddCommand(new ADBShellCharCommand("¥", '¥', start++));

            AddCommand(new ADBShellDDCommand("F1", DirectInputKeys.KEY_F1, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F2", DirectInputKeys.KEY_F2, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F3", DirectInputKeys.KEY_F3, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F4", DirectInputKeys.KEY_F4, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F5", DirectInputKeys.KEY_F5, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F6", DirectInputKeys.KEY_F6, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F7", DirectInputKeys.KEY_F7, defaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F8", DirectInputKeys.KEY_F8, defaultKeyboardDevice, start++));
        }

        private async Task<bool> CheckScreenOn(CancellationToken token)
        {
            string output = await SendCommandCore("dumpsys power | grep \"Display Power\"", token).ConfigureAwait(false);
            return output.Contains("state=ON");
        }

        private async Task<SharpAdbClient.DeviceData> Connect(CancellationToken token)
        {
            if (!await IsPoweredOn(token).ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"Andriod Device {Name} on {DeviceIP} not powered On");
            }
            StartServer();

            adbClient = new AdbClient();

            monitor = new DeviceMonitor(new AdbSocket(adbClient.EndPoint));
            monitor.DeviceDisconnected += Monitor_DeviceDisconnected;
            monitor.Start();

            adbClient.Connect(DeviceIP);

            SharpAdbClient.DeviceData device = GetOnlineDevice();

            int retries = 10;
            while ((device == null) && (retries > 0))
            {
                await Task.Delay(50).ConfigureAwait(false);
                device = GetOnlineDevice();
                retries--;
            }

            UpdateConnectedState(true);
            return device;
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

        private SharpAdbClient.DeviceData GetOnlineDevice()
        {
            SharpAdbClient.DeviceData device = adbClient?.GetDevices()?.Where((x) =>
                            x.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal)).SingleOrDefault();

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

        private void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
        {
            if (e.Device.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal))
            {
                Trace.WriteLine(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
                UpdateConnectedState(false);
            }
        }

        private async Task QueryCurrentApplication(CancellationToken token)
        {
            string output = await SendCommandCore("dumpsys window windows | grep mCurrentFocus", token).ConfigureAwait(false);

            bool found = false;
            Match matches = windowRegEx.Match(output);
            if (matches.Success)
            {
                Group packageGroup = matches.Groups["package"];
                if (packageGroup.Success)
                {
                    found = true;
                    UpdateFeedback(FeedbackName.CurrentApplication, packageGroup.Value);
                }
            }
            if (!found)
            {
                UpdateFeedback(FeedbackName.CurrentApplication, string.Empty);
            }
        }

        private async Task SendCommand(DeviceCommand command, CancellationToken token)
        {
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
                        UpdateFeedback(FeedbackName.Power, false);
                        break;
                    }
                    UpdateFeedback(FeedbackName.Power, await CheckScreenOn(token).ConfigureAwait(false));
                    break;

                case CommandName.PowerOff:
                    // some apps keep streaming when sleep, so try back and home screen
                    await SendCommandCore(GetCommand(CommandName.Return).Data, token).ConfigureAwait(false);
                    await Task.Delay(DefaultCommandDelay).ConfigureAwait(false);
                    await SendCommandCore(GetCommand(CommandName.Home).Data, token).ConfigureAwait(false);
                    await Task.Delay(DefaultCommandDelay).ConfigureAwait(false);

                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;

                case CommandName.ScreenQuery:
                    UpdateFeedback(FeedbackName.Screen, await CheckScreenOn(token).ConfigureAwait(false));
                    break;

                case CommandName.ScreenSaveRunningQuery:
                    output = await SendCommandCore("dumpsys power | grep \"mWakefulness\"", token).ConfigureAwait(false);
                    UpdateFeedback(FeedbackName.ScreenSaverRunning, !output.Contains("Awake"));
                    break;

                case CommandName.CurrentApplicationQuery:
                    await QueryCurrentApplication(token).ConfigureAwait(false);
                    break;

                case CommandName.Home:
                case CommandName.LaunchAmazonVideo:
                case CommandName.LaunchNetflix:
                case CommandName.LaunchPBSKids:
                case CommandName.LaunchPlex:
                case CommandName.LaunchYoutube:
                case CommandName.LaunchKodi:
                case CommandName.LaunchYouTubeKids:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);

                    // set a loop to update current application
                    queryRunningApplicationTokenSource?.Cancel();
                    queryRunningApplicationTokenSource = new CancellationTokenSource();
                    queryRunningApplicationTokenSource.CancelAfter(10000);
                    StartCommandLoop(GetCommand(CommandName.CurrentApplicationQuery),
                                         TimeSpan.FromSeconds(1), queryRunningApplicationTokenSource.Token);
                    break;

                default:
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    break;
            }

            Trace.TraceInformation(Invariant($"Executing {command.Id} took {stopWatch.Elapsed}"));
        }

        private async Task<string> SendCommandCore(string commandData, CancellationToken token)
        {
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
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
                    UpdateConnectedState(false);
                    throw new DeviceException(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
                }

                using (CancellationTokenSource timedCancel = new CancellationTokenSource())
                {
                    timedCancel.CancelAfter(TimeSpan.FromSeconds(30));
                    await adbClient.ExecuteRemoteCommandAsync(commandData, device, receiver, timedCancel.Token, 1000).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                string output = receiver.ToString();
                Trace.WriteLine(Invariant($"Feedback from ADB Device {Name}:[{output}]"));

                receiver.ThrowOnError(output);
                return output;
            }
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

        private static readonly List<OutofOrderCommandDetector> adbOutofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.CursorDownEventDown, CommandName.CursorDownEventUp),
            new OutofOrderCommandDetector(CommandName.CursorUpEventDown, CommandName.CursorUpEventUp),
            new OutofOrderCommandDetector(CommandName.CursorRightEventDown, CommandName.CursorRightEventUp),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventDown, CommandName.CursorLeftEventUp),
        };

        private static readonly Regex windowRegEx = new Regex(@"mCurrentFocus=Window{(?<id>.+?) (?<user>.+) (?<package>.+?)(?:\/(?<activity>.+?))?}",
                                                                      RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private readonly string adbPath;
        private readonly AsyncLock connectionLock = new AsyncLock();
        private readonly int defaultKeyboardDevice;
        private readonly int mediaKeyboardDevice;
        private AdbClient adbClient;
        private CancellationTokenSource cursorCancelLoopSource;
        private DeviceMonitor monitor;
        private CancellationTokenSource queryRunningApplicationTokenSource;
    }

    internal class ADBShellCharCommand : DeviceCommand
    {
        public ADBShellCharCommand(string id, char key, int? fixedValue = null)
            : base(id, Invariant($"input text \"{key}\""), fixedValue: fixedValue)
        {
        }
    }

    internal class ADBShellDDCommand : DeviceCommand
    {
        public ADBShellDDCommand(string id, DirectInputKeys key, int eventDeviceId, int? fixedValue = null, DirectInputKeys? modifier = null)
            : base(id, BuildCommand(key, modifier, eventDeviceId), fixedValue: fixedValue)
        {
        }

        private enum EventValue
        {
            KeyDown = 1,
            KeyUp = 0,
        }

        private static string BuildCommand(DirectInputKeys key, DirectInputKeys? modifier, int eventDeviceId)
        {
            StringBuilder stb = new StringBuilder();
            stb.Append("echo -e -n '");

            if (modifier.HasValue)
            {
                stb.Append(GetString(GetEventBytes(modifier.Value, EventValue.KeyDown)));
                stb.Append(GetString(GetBytes(new InputEvent())));
            }

            stb.Append(GetString(GetEventBytes(key, EventValue.KeyDown)));
            stb.Append(GetString(GetBytes(new InputEvent())));

            stb.Append(GetString(GetEventBytes(key, EventValue.KeyUp)));
            stb.Append(GetString(GetBytes(new InputEvent())));

            if (modifier.HasValue)
            {
                stb.Append(GetString(GetEventBytes(modifier.Value, EventValue.KeyUp)));
                stb.Append(GetString(GetBytes(new InputEvent())));
            }

            stb.Append(Invariant($@"' | dd of=/dev/input/event{eventDeviceId}"));
            return stb.ToString();
        }

        private static byte[] GetBytes(InputEvent inputEvent)
        {
            int size = Marshal.SizeOf(inputEvent);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(inputEvent, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        private static byte[] GetEventBytes(DirectInputKeys key, EventValue eventValue)
        {
            InputEvent inputEvent = new InputEvent()
            {
                Type = 1,
                Code = (short)key,
                Value = (int)eventValue,
            };

            return GetBytes(inputEvent);
        }

        private static string GetString(byte[] data)
        {
            StringBuilder stb = new StringBuilder();
            foreach (byte b in data)
            {
                stb.AppendFormat(CultureInfo.InvariantCulture, @"\x{0:x2}", b);
            }
            return stb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private unsafe struct InputEvent
        {
            public fixed byte Timestamp[16];
            public short Type;
            public short Code;
            public int Value;
        }
    }

    internal class ADBShellKeyEventCommand : DeviceCommand
    {
        public ADBShellKeyEventCommand(string id, AdbShellKeys key, int? fixedValue = null)
            : base(id, Invariant($@"input keyevent {(int)key}"), fixedValue: fixedValue)
        {
        }
    }

    internal class ADBShellLaunchPackageCommand : DeviceCommand
    {
        public ADBShellLaunchPackageCommand(string id, string packageName, string activityName)
           : base(id, Invariant($@"am start -n {packageName}/{activityName}"))
        {
        }

        public ADBShellLaunchPackageCommand(string id, string packageName)
            : base(id, Invariant($@"monkey -p {packageName} -c android.intent.category.LAUNCHER 1"))
        {
        }
    }
}