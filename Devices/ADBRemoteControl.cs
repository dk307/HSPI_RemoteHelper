using SharpAdbClient;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using System.IO;
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

    internal sealed class ADBRemoteControl : DeviceControl
    {
        public ADBRemoteControl(string name, IPAddress deviceIP, string adbPath) :
            base(name)
        {
            if (!File.Exists(adbPath))
            {
                throw new FileNotFoundException(Invariant($"ADB Exe {adbPath} not found"), adbPath);
            }
            DeviceIP = deviceIP;

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

        public override async Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            try
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to Andriod Device {Name} on {DeviceIP}"));
                await SendCommand(command, token).ConfigureAwait(false);
            }
            finally
            {
                DisposeConnection();
            }
        }

        public IPAddress DeviceIP { get; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeConnection();
            }
            base.Dispose(disposing);
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
            if (adbClient != null)
            {
                monitor?.Dispose();
                //do not set null
            }
        }

        private SharpAdbClient.DeviceData GetOnlineDevice()
        {
            var device = adbClient.GetDevices().Where((x) =>
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
            return await NetworkHelper.PingHost(DeviceIP, AdbClient.DefaultPort, networkPingTimeout, token);
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
                case CommandName.ScreenQuery:
                    output = await SendCommandCore("dumpsys power | grep \"Display Power\"", token).ConfigureAwait(false);
                    UpdateFeedback(FeedbackName.Screen, output.Contains("state=ON"));
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

            await adbClient.ExecuteRemoteCommandAsync(commandData, device, receiver, token, 1000);

            string output = receiver.ToString();

            if (string.IsNullOrEmpty(output))
            {
                Trace.WriteLine(Invariant($"Feedback from ADB Device {Name}:{output}"));
            }

            receiver.ThrowOnError(output);
            return output;
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

        private readonly string adbPath;
        private AdbClient adbClient;
        private DeviceMonitor monitor;

        private static readonly Regex windowRegEx = new Regex(@"mCurrentFocus=Window{(?<id>.+?) (?<user>.+) (?<package>.+?)(?:\/(?<activity>.+?))?}",
                                                              RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);
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