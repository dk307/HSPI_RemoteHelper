using Nito.AsyncEx;
using NullGuard;
using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using System.Globalization;
    using System.Runtime.InteropServices;
    using static System.FormattableString;

    // nvidia shield 2015
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class ADBRemoteControl : IPAddressableDeviceControl
    {
        public ADBRemoteControl(string name, IPAddress deviceIP,
                                string adbPath, TimeSpan defaultCommandDelay,
                                IConnectionProvider connectionProvider) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, adbOutofCommandDetectors)
        {
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(Invariant($"ADB Exe {adbPath} not found"), adbPath);
            }

            this.adbPath = adbPath;
            AddCommand(new ADBShellKeyEventCommand(CommandName.AudioTrack, AdbShellKeys.KEYCODE_MEDIA_AUDIO_TRACK));
            AddCommand(new ADBShellDDCommand(CommandName.CursorDown, DirectInputKeys.KEY_DOWN, DefaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorLeft, DirectInputKeys.KEY_LEFT, DefaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorRight, DirectInputKeys.KEY_RIGHT, DefaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.CursorUp, DirectInputKeys.KEY_UP, DefaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.Enter, DirectInputKeys.KEY_ENTER, DefaultKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.Home, DirectInputKeys.KEY_HOMEPAGE, MediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Info, AdbShellKeys.KEYCODE_INFO));
            AddCommand(new ADBShellDDCommand(CommandName.MediaFastForward, DirectInputKeys.KEY_FASTFORWARD, MediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaNext, AdbShellKeys.KEYCODE_MEDIA_NEXT));
            AddCommand(new ADBShellDDCommand(CommandName.MediaPlayPause, DirectInputKeys.KEY_PLAYPAUSE, MediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaPrevious, AdbShellKeys.KEYCODE_MEDIA_PREVIOUS));
            AddCommand(new ADBShellDDCommand(CommandName.MediaRewind, DirectInputKeys.KEY_REWIND, MediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipBackward, DirectInputKeys.KEY_PREVIOUSSONG, MediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaSkipForward, DirectInputKeys.KEY_NEXTSONG, MediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.MediaStop, DirectInputKeys.KEY_STOP, MediaKeyboardDevice));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOff, AdbShellKeys.KEYCODE_SLEEP));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOn, AdbShellKeys.KEYCODE_WAKEUP));
            AddCommand(new DeviceCommand(CommandName.PowerQuery));
            AddCommand(new ADBShellDDCommand(CommandName.Return, DirectInputKeys.KEY_BACK, MediaKeyboardDevice));
            AddCommand(new ADBShellDDCommand(CommandName.Subtitle, DirectInputKeys.KEY_F2, DefaultKeyboardDevice));

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
            AddCommand(new ADBShellLaunchPackageCommand(CommandName.LaunchKodi, @"com.semperpax.spmc16"));

            AddKeyboardCommands(1000);

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Screen, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.ScreenSaverRunning, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.CurrentApplication, TypeCode.String));

            StartServer();
        }

        public override bool InvalidState
        {
            get
            {
                var status = AdbServer.Instance.GetStatus();
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
            var numberKeys = new DirectInputKeys[]
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
                AddCommand(new ADBShellDDCommand(c.ToString(), numberKeys[c - '0'], DefaultKeyboardDevice, start++));
            }

            var charKeys = new DirectInputKeys[]
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
                AddCommand(new ADBShellDDCommand(c.ToString(), charKeys[c - 'a'], DefaultKeyboardDevice, start++));
            }

            for (char c = 'A'; c <= 'Z'; c++)
            {
                AddCommand(new ADBShellDDCommand(c.ToString(), charKeys[c - 'A'], DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            }

            AddCommand(new ADBShellDDCommand("Space", DirectInputKeys.KEY_SPACE, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("BackSpace", DirectInputKeys.KEY_BACKSPACE, DefaultKeyboardDevice, start++));

            AddCommand(new ADBShellDDCommand("!", DirectInputKeys.KEY_1, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(",", DirectInputKeys.KEY_COMMA, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("?", DirectInputKeys.KEY_SLASH, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(".", DirectInputKeys.KEY_DOT, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("~", DirectInputKeys.KEY_GRAVE, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(";", DirectInputKeys.KEY_SEMICOLON, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("'", DirectInputKeys.KEY_APOSTROPHE, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("^", DirectInputKeys.KEY_6, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("*", DirectInputKeys.KEY_7, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("%", DirectInputKeys.KEY_5, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("@", DirectInputKeys.KEY_2, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("&", DirectInputKeys.KEY_7, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("#", DirectInputKeys.KEY_3, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("=", DirectInputKeys.KEY_EQUAL, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("+", DirectInputKeys.KEY_EQUAL, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(":", DirectInputKeys.KEY_SEMICOLON, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("/", DirectInputKeys.KEY_SLASH, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("\\", DirectInputKeys.KEY_BACKSLASH, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("_", DirectInputKeys.KEY_MINUS, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("-", DirectInputKeys.KEY_MINUS, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("|", DirectInputKeys.KEY_BACKSLASH, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("{", DirectInputKeys.KEY_LEFTBRACE, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("}", DirectInputKeys.KEY_RIGHTBRACE, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("[", DirectInputKeys.KEY_LEFTBRACE, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("]", DirectInputKeys.KEY_RIGHTBRACE, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("(", DirectInputKeys.KEY_9, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(")", DirectInputKeys.KEY_0, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("\"", DirectInputKeys.KEY_APOSTROPHE, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("<", DirectInputKeys.KEY_COMMA, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand(">", DirectInputKeys.KEY_DOT, DefaultKeyboardDevice, start++, DirectInputKeys.KEY_LEFTSHIFT));
            AddCommand(new ADBShellDDCommand("`", DirectInputKeys.KEY_GRAVE, DefaultKeyboardDevice, start++));

            AddCommand(new ADBShellDDCommand("F1", DirectInputKeys.KEY_F1, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F2", DirectInputKeys.KEY_F2, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F3", DirectInputKeys.KEY_F3, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F4", DirectInputKeys.KEY_F4, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F5", DirectInputKeys.KEY_F5, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F6", DirectInputKeys.KEY_F6, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F7", DirectInputKeys.KEY_F7, DefaultKeyboardDevice, start++));
            AddCommand(new ADBShellDDCommand("F8", DirectInputKeys.KEY_F8, DefaultKeyboardDevice, start++));
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
            var device = adbClient?.GetDevices()?.Where((x) =>
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
            MacroStartCommandLoop(commandId, TimeSpan.FromMilliseconds(100), ref cursorCancelLoopSource);
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
            var matches = windowRegEx.Match(output);
            if (matches.Success)
            {
                var packageGroup = matches.Groups["package"];
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

                case CommandName.CursorLeftEventUp:
                case CommandName.CursorRightEventUp:
                case CommandName.CursorUpEventUp:
                case CommandName.CursorDownEventUp:
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

            Trace.WriteLine(Invariant($"Executing {command.Id} took {stopWatch.Elapsed}"));
        }

        private async Task<string> SendCommandCore(string commandData, CancellationToken token)
        {
            using (await connectionLock.LockAsync(token).ConfigureAwait(false))
            {
                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                var receiver = new ConsoleOutputReceiver()
                {
                    TrimLines = true
                };

                var device = GetOnlineDevice();

                if (device == null)
                {
                    UpdateConnectedState(false);
                    throw new DeviceException(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
                }

                // the reason we do not send cancellation token is to not break commands in between
                await adbClient.ExecuteRemoteCommandAsync(commandData, device, receiver, default(CancellationToken), 1000).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                string output = receiver.ToString();
                Trace.WriteLine(Invariant($"Feedback from ADB Device {Name}:[{output}]"));

                receiver.ThrowOnError(output);
                return output;
            }
        }

        private void StartServer()
        {
            var status = AdbServer.Instance.GetStatus();
            if (!status.IsRunning)
            {
                Trace.WriteLine(Invariant($"Starting local adb server"));
                AdbServer.Instance.StartServer(adbPath, false);
                Trace.WriteLine(Invariant($"Started local adb server"));
            }
        }

        private const int DefaultKeyboardDevice = 2;
        private const int MediaKeyboardDevice = 3;

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
        private AdbClient adbClient;
        private CancellationTokenSource cursorCancelLoopSource;
        private DeviceMonitor monitor;
        private CancellationTokenSource queryRunningApplicationTokenSource;
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
            var size = Marshal.SizeOf(inputEvent);
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
                Code = (Int16)key,
                Value = (Int32)eventValue,
            };

            return GetBytes(inputEvent);
        }

        private static string GetString(byte[] data)
        {
            StringBuilder stb = new StringBuilder();
            foreach (var b in data)
            {
                stb.AppendFormat(CultureInfo.InvariantCulture, @"\x{0:x2}", b);
            }
            return stb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private unsafe struct InputEvent
        {
            public fixed byte Timestamp[16];
            public Int16 Type;
            public Int16 Code;
            public Int32 Value;
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