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

            AddCommand(new ADBShellSendEventCommand(CommandName.CursorUpEventDown, 103, ADBShellSendEventCommand.ButtonPressType.Down));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorUpEventUp, 103, ADBShellSendEventCommand.ButtonPressType.Up));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorDownEventDown, 108, ADBShellSendEventCommand.ButtonPressType.Down));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorDownEventUp, 108, ADBShellSendEventCommand.ButtonPressType.Up));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorRightEventDown, 106, ADBShellSendEventCommand.ButtonPressType.Down));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorRightEventUp, 106, ADBShellSendEventCommand.ButtonPressType.Up));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorLeftEventDown, 105, ADBShellSendEventCommand.ButtonPressType.Down));
            AddCommand(new ADBShellSendEventCommand(CommandName.CursorLeftEventUp, 105, ADBShellSendEventCommand.ButtonPressType.Up));

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
            return await NetworkHelper.PingAddress(DeviceIP, networkPingTimeout).WaitOnRequestCompletion(token);
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

        private async Task RevertDownKey(CancellationToken token)
        {
            if (downKey.HasValue)
            {
                var upCommand = new ADBShellSendEventCommand("Up Command", downKey.Value, ADBShellSendEventCommand.ButtonPressType.Up);
                await SendCommandCore(upCommand.Data, token).ConfigureAwait(false);
                downKey = null;
            }
        }

        private async Task SendCommand(DeviceCommand command, CancellationToken token)
        {
            if (ShouldIgnoreCommand(command.Id))
            {
                Trace.WriteLine(Invariant($"Ignoring Command from ADB Device {Name} {command.Id} as it is out of order"));
                return;
            }

            string output;
            switch (command.Id)
            {
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

                case CommandName.CursorUpEventDown:
                case CommandName.CursorDownEventDown:
                case CommandName.CursorRightEventDown:
                case CommandName.CursorLeftEventDown:
                    await RevertDownKey(token).ConfigureAwait(false);
                    await SendCommandCore(command.Data, token).ConfigureAwait(false);
                    downKey = (command as ADBShellSendEventCommand)?.Key;
                    break;

                case CommandName.CursorUpEventUp:
                case CommandName.CursorDownEventUp:
                case CommandName.CursorRightEventUp:
                case CommandName.CursorLeftEventUp:
                    await RevertDownKey(token).ConfigureAwait(false);
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

                await adbClient.ExecuteRemoteCommandAsync(commandData, device, receiver, token, 1000).ConfigureAwait(false);
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
            new OutofOrderCommandDetector(CommandName.CursorDownEventUp, CommandName.CursorDownEventDown),
            new OutofOrderCommandDetector(CommandName.CursorUpEventUp, CommandName.CursorUpEventDown),
            new OutofOrderCommandDetector(CommandName.CursorRightEventUp, CommandName.CursorRightEventDown),
            new OutofOrderCommandDetector(CommandName.CursorLeftEventUp, CommandName.CursorLeftEventDown),
        };

        private AdbClient adbClient;
        private int? downKey = null;
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
            : base(id, Invariant($"sendevent /dev/input/event0 1 {key} 1 && sendevent /dev/input/event0 1 {key} 0 && sendevent /dev/input/event0 0 0 0"))
        {
        }

        public ADBShellSendEventCommand(string id, int key, ButtonPressType type)
            : base(id, Invariant($"sendevent /dev/input/event0 1  {key} {(int)type} && sendevent /dev/input/event0 0 0 0"))
        {
            Key = key;
        }

        public enum ButtonPressType
        {
            Down = 1,
            Up = 0
        }

        public int Key { get; }
    }
}