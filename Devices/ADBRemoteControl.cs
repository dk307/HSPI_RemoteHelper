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
    using static System.FormattableString;

    internal enum AdbShellKeys
    {
        //KEYCODE_MENU = 82,
        //KEYCODE_MEDIA_STEP_BACKWARD = 275,
        //KEYCODE_MEDIA_STEP_FORWARD = 274,

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
            //AddCommand(new ADBShellDeviceCommand(CommandName.Menu, AdbShellKeys.KEYCODE_MENU));
            //AddCommand(new ADBShellDeviceCommand(CommandName.MediaStepBackward, AdbShellKeys.KEYCODE_MEDIA_STEP_BACKWARD));
            //AddCommand(new ADBShellDeviceCommand(CommandName.MediaStepForward, AdbShellKeys.KEYCODE_MEDIA_STEP_FORWARD));

            AddCommand(new ADBShellDeviceCommand(CommandName.AudioTrack, AdbShellKeys.KEYCODE_MEDIA_AUDIO_TRACK));
            AddCommand(new ADBShellDeviceCommand(CommandName.CursorDown, AdbShellKeys.KEYCODE_DPAD_DOWN));
            AddCommand(new ADBShellDeviceCommand(CommandName.CursorLeft, AdbShellKeys.KEYCODE_DPAD_LEFT));
            AddCommand(new ADBShellDeviceCommand(CommandName.CursorRight, AdbShellKeys.KEYCODE_DPAD_RIGHT));
            AddCommand(new ADBShellDeviceCommand(CommandName.CursorUp, AdbShellKeys.KEYCODE_DPAD_UP));
            AddCommand(new ADBShellDeviceCommand(CommandName.Enter, AdbShellKeys.KEYCODE_ENTER));
            AddCommand(new ADBShellDeviceCommand(CommandName.Home, AdbShellKeys.KEYCODE_HOME));
            AddCommand(new ADBShellDeviceCommand(CommandName.Info, AdbShellKeys.KEYCODE_INFO));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaFastForward, AdbShellKeys.KEYCODE_MEDIA_FAST_FORWARD));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaNext, AdbShellKeys.KEYCODE_MEDIA_NEXT));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaPlayPause, AdbShellKeys.KEYCODE_MEDIA_PLAY_PAUSE));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaPrevious, AdbShellKeys.KEYCODE_MEDIA_PREVIOUS));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaRewind, AdbShellKeys.KEYCODE_MEDIA_REWIND));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaSkipBackward, AdbShellKeys.KEYCODE_MEDIA_SKIP_BACKWARD));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaSkipForward, AdbShellKeys.KEYCODE_MEDIA_SKIP_FORWARD));
            AddCommand(new ADBShellDeviceCommand(CommandName.MediaStop, AdbShellKeys.KEYCODE_MEDIA_STOP));
            AddCommand(new ADBShellDeviceCommand(CommandName.PowerOff, AdbShellKeys.KEYCODE_SLEEP));
            AddCommand(new ADBShellDeviceCommand(CommandName.PowerOn, AdbShellKeys.KEYCODE_WAKEUP));
            AddCommand(new ADBShellDeviceCommand(CommandName.PowerQuery, string.Empty));
            AddCommand(new ADBShellDeviceCommand(CommandName.Return, AdbShellKeys.KEYCODE_ESCAPE));
            AddCommand(new ADBShellDeviceCommand(CommandName.Subtitle, AdbShellKeys.KEYCODE_CAPTIONS));

            AddCommand(new ADBShellDeviceCommand(CommandName.ScreenQuery, string.Empty));
            AddCommand(new ADBShellDeviceCommand(CommandName.ScreenSaveRunningQuery, string.Empty));
            AddCommand(new ADBShellDeviceCommand(CommandName.CurrentApplicationQuery, string.Empty));

            AddCommand(new ADBShellDeviceCommand(CommandName.LaunchNetflix, @"com.netflix.ninja"));
            AddCommand(new ADBShellDeviceCommand(CommandName.LaunchYoutube, @"com.google.android.youtube.tv"));
            AddCommand(new ADBShellDeviceCommand(CommandName.LaunchPlex, @"com.plexapp.android"));
            AddCommand(new ADBShellDeviceCommand(CommandName.LaunchAmazonVideo,
                                                 @"com.amazon.amazonvideo.livingroom.nvidia", @"com.amazon.ignition.IgnitionActivity"));
            AddCommand(new ADBShellDeviceCommand(CommandName.LaunchPBSKids, @"org.pbskids.video"));

            AddCommand(new DeviceCommand(CommandName.MacroStartCursorDownLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartCursorLeftLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartCursorRightLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartCursorUpLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStopCursorLoop));

            AddCommand(new DeviceCommand(CommandName.MacroStartRewindLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartFastForwardLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartSkipBackwardLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStartSkipForwardLoop));
            AddCommand(new DeviceCommand(CommandName.MacroStopMediaControlLoop));

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

        private static TimeSpan LoopCommandDelay => TimeSpan.FromMilliseconds(0);

        public override async Task ExecuteCommand(DeviceCommand command, CancellationToken token)
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
            if (!await IsPoweredOn(token))
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
            cursorLoopCancelSource?.Cancel();
            mediaControlLoopCancelSource?.Cancel();
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
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitOnRequestCompletion(token);
        }

        private void MacroCursorLoop(string commandId)
        {
            MacroStartCommandLoop(commandId, LoopCommandDelay, ref cursorLoopCancelSource);
        }

        private void MacroMediaControlLoop(string commandId)
        {
            MacroStartCommandLoop(commandId, LoopCommandDelay, ref mediaControlLoopCancelSource);
        }

        private void Monitor_DeviceDisconnected(object sender, DeviceDataEventArgs e)
        {
            if (e.Device.Serial.StartsWith(DeviceIP.ToString(), StringComparison.Ordinal))
            {
                Trace.WriteLine(Invariant($"Lost Connection to Andriod Device {Name} on {DeviceIP}"));
                UpdateConnectedState(false);
            }
        }

        private async Task SendCommand(DeviceCommand command, CancellationToken token)
        {
            string output;
            switch (command.Id)
            {
                case CommandName.MacroStartCursorDownLoop:
                    MacroCursorLoop(CommandName.CursorDown);
                    break;

                case CommandName.MacroStartCursorUpLoop:
                    MacroCursorLoop(CommandName.CursorUp);
                    break;

                case CommandName.MacroStartCursorRightLoop:
                    MacroCursorLoop(CommandName.CursorRight);
                    break;

                case CommandName.MacroStartCursorLeftLoop:
                    MacroCursorLoop(CommandName.CursorLeft);
                    break;

                case CommandName.MacroStopCursorLoop:
                    MacroStopCommandLoop(ref cursorLoopCancelSource);
                    break;

                case CommandName.MacroStartRewindLoop:
                    MacroMediaControlLoop(CommandName.MediaRewind);
                    break;

                case CommandName.MacroStartFastForwardLoop:
                    MacroMediaControlLoop(CommandName.MediaFastForward);
                    break;

                case CommandName.MacroStartSkipBackwardLoop:
                    MacroMediaControlLoop(CommandName.MediaSkipBackward);
                    break;

                case CommandName.MacroStartSkipForwardLoop:
                    MacroMediaControlLoop(CommandName.MediaSkipForward);
                    break;

                case CommandName.MacroStopMediaControlLoop:
                    MacroStopCommandLoop(ref mediaControlLoopCancelSource);
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
                    output = await SendCommandCore("dumpsys window windows | grep mCurrentFocus", token).ConfigureAwait(false);

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
                        UpdateFeedback(FeedbackName.CurrentApplication, null);
                    }

                    break;

                default:
                    await SendCommandCore(command.Data, token);
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

                Trace.WriteLine(Invariant($"ADB Device {Name}"));
                await adbClient.ExecuteRemoteCommandAsync(commandData, device, receiver, token, 1000).ConfigureAwait(false);
                //adbClient.ExecuteRemoteCommand(commandData, device, receiver);
                Trace.WriteLine(Invariant($"ADB Device {Name}"));
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

        private static readonly Regex windowRegEx = new Regex(@"mCurrentFocus=Window{(?<id>.+?) (?<user>.+) (?<package>.+?)(?:\/(?<activity>.+?))?}",
                                                              RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private readonly string adbPath;
        private readonly AsyncLock connectionLock = new AsyncLock();
        private AdbClient adbClient;
        private CancellationTokenSource cursorLoopCancelSource;
        private CancellationTokenSource mediaControlLoopCancelSource;
        private DeviceMonitor monitor;
    }

    internal class ADBShellDeviceCommand : DeviceCommand
    {
        public ADBShellDeviceCommand(string id, AdbShellKeys key)
            : base(id, Invariant($@"input keyevent {(int)key}"))
        {
        }

        public ADBShellDeviceCommand(string id, string packageName, string activityName)
            : base(id, Invariant($@"am start -n {packageName}/{activityName}"))
        {
        }

        public ADBShellDeviceCommand(string id, string packageName)
            : base(id, Invariant($@"monkey -p {packageName} -c android.intent.category.LAUNCHER 1"))
        {
        }
    }
}