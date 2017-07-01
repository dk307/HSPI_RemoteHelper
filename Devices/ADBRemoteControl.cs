using Nito.AsyncEx;
using NullGuard;
using SharpAdbClient;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using System.Collections.Generic;
    using System.Text;
    using static System.FormattableString;

    internal enum AdbShellKeys
    {
        KEYCODE_CAPTIONS = 175,
        KEYCODE_DPAD_DOWN = 20,
        KEYCODE_DPAD_LEFT = 21,
        KEYCODE_DPAD_RIGHT = 22,
        KEYCODE_DPAD_UP = 19,
        KEYCODE_ENTER = 66,
        KEYCODE_ESCAPE = 111,
        KEYCODE_HOME = 3,
        KEYCODE_INFO = 165,
        KEYCODE_MEDIA_AUDIO_TRACK = 222,
        KEYCODE_MEDIA_FAST_FORWARD = 90,
        KEYCODE_MEDIA_NEXT = 87,
        KEYCODE_MEDIA_PLAY_PAUSE = 126,
        KEYCODE_MEDIA_PREVIOUS = 88,
        KEYCODE_MEDIA_REWIND = 89,
        KEYCODE_MEDIA_SKIP_BACKWARD = 273,
        KEYCODE_MEDIA_SKIP_FORWARD = 272,
        KEYCODE_MEDIA_STOP = 86,
        KEYCODE_SLEEP = 223,
        KEYCODE_WAKEUP = 224,
    };

    // nvidia shield 2015
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class ADBRemoteControl : IPAddressableDeviceControl
    {
        public ADBRemoteControl(string name, IPAddress deviceIP, string adbPath, TimeSpan defaultCommandDelay) :
            base(name, deviceIP, defaultCommandDelay)
        {
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(Invariant($"ADB Exe {adbPath} not found"), adbPath);
            }

            this.adbPath = adbPath;

            AddCommand(new ADBShellKeyEventCommand(CommandName.AudioTrack, AdbShellKeys.KEYCODE_MEDIA_AUDIO_TRACK));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorDown, 108));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorLeft, 105));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorRight, 106));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorUp, 103));
            AddCommand(new ADBShellSendEventCommand(CommandName.Enter, 0x161));
            AddCommand(new ADBShellSendEventCommand(CommandName.Home, 172));
            AddCommand(new ADBShellKeyEventCommand(CommandName.Info, AdbShellKeys.KEYCODE_INFO));
            AddCommand(new ADBShellSendEventCommand(CommandName.MediaFastForward, 208));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaNext, AdbShellKeys.KEYCODE_MEDIA_NEXT));
            AddCommand(new ADBShellSendEventCommand(CommandName.MediaPlayPause, 164));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaPrevious, AdbShellKeys.KEYCODE_MEDIA_PREVIOUS));
            AddCommand(new ADBShellSendEventCommand(CommandName.MediaRewind, 168));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaSkipBackward, AdbShellKeys.KEYCODE_MEDIA_SKIP_BACKWARD));
            AddCommand(new ADBShellKeyEventCommand(CommandName.MediaSkipForward, AdbShellKeys.KEYCODE_MEDIA_SKIP_FORWARD));
            AddCommand(new ADBShellSendEventCommand(CommandName.MediaStop, 128));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOff, AdbShellKeys.KEYCODE_SLEEP));
            AddCommand(new ADBShellKeyEventCommand(CommandName.PowerOn, AdbShellKeys.KEYCODE_WAKEUP));
            AddCommand(new DeviceCommand(CommandName.PowerQuery));
            AddCommand(new ADBShellSendEventCommand(CommandName.Return, 158));
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

            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorUpEventDown, 103, ADBShellSendEventCommand.ButtonPressType.Down));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorUpEventUp, 103, ADBShellSendEventCommand.ButtonPressType.Up));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorDownEventDown, 108, ADBShellSendEventCommand.ButtonPressType.Down));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorDownEventUp, 108, ADBShellSendEventCommand.ButtonPressType.Up));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorRightEventDown, 106, ADBShellSendEventCommand.ButtonPressType.Down));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorRightEventUp, 106, ADBShellSendEventCommand.ButtonPressType.Up));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorLeftEventDown, 105, ADBShellSendEventCommand.ButtonPressType.Down));
            //AddCommand(new ADBShellSendEventCommand(CommandName.CursorLeftEventUp, 105, ADBShellSendEventCommand.ButtonPressType.Up));

            AddKeyboardCommands();

            AddFeedback(new DeviceFeedback(FeedbackName.Power, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.Screen, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.ScreenSaverRunning, TypeCode.Boolean));
            AddFeedback(new DeviceFeedback(FeedbackName.CurrentApplication, TypeCode.String));

            StartServer();
        }

        private void AddKeyboardCommands()
        {
            for (char c = '0'; c <= '9'; c++)
            {
                AddCommand(new ADBShellCharCommand(c.ToString(), c));
            }

            for (char c = 'a'; c <= 'z'; c++)
            {
                AddCommand(new ADBShellCharCommand(c.ToString(), c));
            }

            for (char c = 'A'; c <= 'Z'; c++)
            {
                AddCommand(new ADBShellCharCommand(c.ToString(), c));
            }
            //string otherChars = "!,?.~;()'^*%?@&#=+,:/_-";
            //foreach (char c in otherChars)
            //{
            //    AddCommand(new ADBShellCharCommand(c.ToString(), c));
            //}

            //AddCommand(new ADBShellCharCommand("Space", "%s"));
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

        public override Task ExecuteCommandCore(DeviceCommand command, bool canIgnore, CancellationToken token)
        {
            if (canIgnore && ShouldIgnoreCommand(command.Id))
            {
                Trace.WriteLine(Invariant($"Ignoring Command for ADB Device {Name} {command.Id} as it is out of order"));
                return Task.FromResult(true);
            }

            return ExecuteCommand2(command, token);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeConnection();
            }
            base.Dispose(disposing);
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

        private async Task ExecuteCommand2(DeviceCommand command, CancellationToken token)
        {
            try
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Andriod Device {Name} on {DeviceIP}"));
                await SendCommand(command, token).ConfigureAwait(false);
            }
            catch
            {
                DisposeConnection();
                throw;
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
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitOnRequestCompletion(token);
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
                    if (!await IsPoweredOn(token))
                    {
                        UpdateFeedback(FeedbackName.Power, false);
                        break;
                    }
                    UpdateFeedback(FeedbackName.Power, await CheckScreenOn(token));
                    break;

                case CommandName.ScreenQuery:
                    UpdateFeedback(FeedbackName.Screen, await CheckScreenOn(token));
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

        private bool ShouldIgnoreCommand(string commandId)
        {
            foreach (var outofCommandDetector in outofCommandDetectors)
            {
                if (outofCommandDetector.ShouldIgnore(commandId))
                {
                    return true;
                }
            }

            return false;
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

        private static readonly Regex windowRegEx = new Regex(@"mCurrentFocus=Window{(?<id>.+?) (?<user>.+) (?<package>.+?)(?:\/(?<activity>.+?))?}",
                                                              RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private readonly string adbPath;
        private readonly AsyncLock connectionLock = new AsyncLock();

        private readonly List<OutofOrderCommandDetector> outofCommandDetectors = new List<OutofOrderCommandDetector>()
        {
            new OutofOrderCommandDetector(CommandName.CursorDownEventDown, CommandName.CursorDownEventUp),
            new OutofOrderCommandDetector(CommandName.CursorUpEventDown, CommandName.CursorUpEventUp),
            new OutofOrderCommandDetector(CommandName.CursorRightEventDown, CommandName.CursorRightEventUp),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventDown, CommandName.CursorLeftEventUp),
        };

        private AdbClient adbClient;
        private CancellationTokenSource cursorCancelLoopSource;
        private DeviceMonitor monitor;
        private CancellationTokenSource queryRunningApplicationTokenSource;
    }

    internal class ADBShellKeyEventCommand : DeviceCommand
    {
        public ADBShellKeyEventCommand(string id, AdbShellKeys key)
            : base(id, Invariant($@"input keyevent {(int)key}"))
        {
        }
    }

    internal class ADBShellCharCommand : DeviceCommand
    {
        public ADBShellCharCommand(string id, char key)
            : base(id, Invariant($"input text \"{key}\""))
        {
        }

        public ADBShellCharCommand(string id, string key)
            : base(id, Invariant($"input text \"{key}\""))
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

    internal class ADBShellSendEventCommand : DeviceCommand
    {
        public ADBShellSendEventCommand(string id, int key)
            : base(id, BuildCommand(key))
        {
        }

        //public ADBShellSendEventCommand(string id, int key, ButtonPressType type)
        //    : base(id, Invariant($"sendevent /dev/input/event0 1  {key} {(int)type} && sendevent /dev/input/event0 0 0 0"))
        //{
        //    Key = key;
        //}

        //public enum ButtonPressType
        //{
        //    Down = 1,
        //    Up = 0
        //}

        private static string BuildCommand(int key)
        {
            StringBuilder stb = new StringBuilder();
            stb.Append(Invariant($"sendevent /dev/input/event0 1 {key} 1 && "));
            stb.Append(Invariant($"sendevent /dev/input/event0 0 0 0 && "));
            stb.Append(Invariant($"sendevent /dev/input/event0 1 {key} 0 && "));
            stb.Append(Invariant($"sendevent /dev/input/event0 0 0 0"));
            //stb.Append(Invariant($"sendevent /dev/input/event0 4 4 8420876"));
            return stb.ToString();
        }
    }
}