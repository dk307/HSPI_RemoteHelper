using Hspi.Connector;
using Hspi.Utils;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class GlobalMacros : DeviceControl
    {
        public GlobalMacros(string name, IConnectionProvider connectionProvider) :
            base(name, connectionProvider)
        {
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnNvidiaShield, type: DeviceCommandType.Both, fixedValue: -100));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOffEverything, type: DeviceCommandType.Both, fixedValue: -99));
            AddCommand(new DeviceCommand(CommandName.MacroGameModeOn, type: DeviceCommandType.Both, fixedValue: -98));
            AddCommand(new DeviceCommand(CommandName.MacroGameModeOff, type: DeviceCommandType.Both, fixedValue: -97));
            AddCommand(new DeviceCommand(CommandName.MacroToggleMute, type: DeviceCommandType.Both, fixedValue: -96));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnXBoxOne, type: DeviceCommandType.Both, fixedValue: -95));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnSonyBluRay, type: DeviceCommandType.Both, fixedValue: -94));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnPS3, type: DeviceCommandType.Both, fixedValue: -93));
            AddCommand(new DeviceCommand(CommandName.MediaPlayPause, type: DeviceCommandType.Both, fixedValue: -92));
            AddCommand(new DeviceCommand(CommandName.CursorUp, type: DeviceCommandType.Both, fixedValue: -91));
            AddCommand(new DeviceCommand(CommandName.CursorDown, type: DeviceCommandType.Both, fixedValue: -90));
            AddCommand(new DeviceCommand(CommandName.CursorLeft, type: DeviceCommandType.Both, fixedValue: -89));
            AddCommand(new DeviceCommand(CommandName.CursorRight, type: DeviceCommandType.Both, fixedValue: -88));
            AddCommand(new DeviceCommand(CommandName.Exit, type: DeviceCommandType.Both, fixedValue: -87));
            AddCommand(new DeviceCommand(CommandName.Enter, type: DeviceCommandType.Both, fixedValue: -86));
            AddCommand(new DeviceCommand(CommandName.Home, type: DeviceCommandType.Both, fixedValue: -85));
            AddCommand(new DeviceCommand(CommandName.Return, type: DeviceCommandType.Both, fixedValue: -84));
            AddCommand(new DeviceCommand(CommandName.MediaPrevious, type: DeviceCommandType.Both, fixedValue: -83));
            AddCommand(new DeviceCommand(CommandName.MediaNext, type: DeviceCommandType.Both, fixedValue: -82));
            AddCommand(new DeviceCommand(CommandName.MediaRewind, type: DeviceCommandType.Both, fixedValue: -81));
            AddCommand(new DeviceCommand(CommandName.MediaFastForward, type: DeviceCommandType.Both, fixedValue: -80));
            AddCommand(new DeviceCommand(CommandName.MediaSkipBackward, type: DeviceCommandType.Both, fixedValue: -79));
            AddCommand(new DeviceCommand(CommandName.MediaSkipForward, type: DeviceCommandType.Both, fixedValue: -78));
            AddCommand(new DeviceCommand(CommandName.MediaStepBackward, type: DeviceCommandType.Both, fixedValue: -77));
            AddCommand(new DeviceCommand(CommandName.MediaStepForward, type: DeviceCommandType.Both, fixedValue: -76));
            AddCommand(new DeviceCommand(CommandName.MediaStop, type: DeviceCommandType.Both, fixedValue: -75));
            AddCommand(new DeviceCommand(CommandName.Info, type: DeviceCommandType.Both, fixedValue: -74));
            AddCommand(new DeviceCommand(CommandName.Menu, type: DeviceCommandType.Both, fixedValue: -73));

            AddFeedback(new DeviceFeedback(FeedbackName.RunningMacro, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.MacroStatus, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.TVGameMode, TypeCode.Boolean));
        }

        public override bool InvalidState => false;

        public override Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommand2(command, token);
        }

        private static async Task DelayDefaultCommandTime(CancellationToken timeoutToken, params IDeviceCommandHandler[] devices)
        {
            int msWait = 0;
            foreach (IDeviceCommandHandler device in devices)
            {
                msWait = Math.Max(msWait, (int)device.DefaultCommandDelay.TotalMilliseconds);
            }

            await Task.Delay(msWait, timeoutToken).ConfigureAwait(false);
        }

        public override Task Refresh(CancellationToken token)
        {
            UpdateConnectedState(true);
            ClearStatus();
            UpdateFeedback(FeedbackName.RunningMacro, string.Empty);
            return Task.CompletedTask;
        }

        private static bool? GetFeedbackAsBoolean(IDeviceFeedbackProvider deviceFeedbackProvider, string feedbackName)
        {
            object value = deviceFeedbackProvider.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        private static string GetFeedbackAsString(IDeviceFeedbackProvider connection, string feedbackName)
        {
            object value = connection.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private void ClearStatus()
        {
            UpdateFeedback(FeedbackName.MacroStatus, string.Empty);
        }

        private async Task<bool> EnsureAVRState(IDeviceCommandHandler avr, object expectedValue,
                                                string valueQueryCommand, string valueChangeCommand,
                                                string feedbackName, int maxRetries, CancellationToken token)
        {
            bool changed = false;
            await avr.HandleCommandIgnoreException(valueQueryCommand, token).ConfigureAwait(false);
            IDeviceFeedbackProvider feedbackProvider = ConnectionProvider.GetFeedbackProvider(avr.DeviceType);
            do
            {
                await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);

                object currentValue = feedbackProvider.GetFeedbackValue(feedbackName);
                if (object.Equals(currentValue, expectedValue))
                {
                    break;
                }
                else
                {
                    changed = true;
                    await avr.HandleCommandIgnoreException(valueChangeCommand, token).ConfigureAwait(false);
                    await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);
                }

                await avr.HandleCommandIgnoreException(valueQueryCommand, token).ConfigureAwait(false);
                maxRetries--;
            } while (!token.IsCancellationRequested && (maxRetries > 0));

            return changed;
        }

        private bool UpdateStates(DeviceCommand command)
        {
            switch (command.Id)
            {
                case CommandName.MacroTurnOnNvidiaShield:
                case CommandName.MacroTurnOnXBoxOne:

                case CommandName.MacroTurnOnPS3:

                case CommandName.MacroTurnOffEverything:

                case CommandName.MacroTurnOnSonyBluRay:

                case CommandName.MacroGameModeOn:

                case CommandName.MacroGameModeOff:

                case CommandName.MacroToggleMute:
                    return true;

                default:

                    return false;
            }
        }

        private async Task ExecuteCommand2(DeviceCommand command, CancellationToken token)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            Trace.WriteLine(Invariant($"Executing {command.Id} "));

            bool updateStatus = UpdateStates(command);

            if (updateStatus)
            {
                UpdateFeedback(FeedbackName.RunningMacro, command.Id);
                UpdateStatus(command.Id);
            }

            try
            {
                switch (command.Id)
                {
                    case CommandName.MacroTurnOnNvidiaShield:
                        await MacroTurnOnNvidiaShield(token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnXBoxOne:
                        await MacroTurnOnXboxOne(token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnPS3:
                        await MacroTurnOnPS3(token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOffEverything:
                        await MacroTurnoffEverything(token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnSonyBluRay:
                        await MacroTurnOnSonyBluRay(token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroGameModeOn:
                        await MacroTurnGameMode(true, token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroGameModeOff:
                        await MacroTurnGameMode(false, token).ConfigureAwait(false);
                        break;

                    case CommandName.MacroToggleMute:
                        await MacroToggleMute(token).ConfigureAwait(false);
                        break;

                    default:
                        await SendCommandToAVRInputDevice(command.Id, token).ConfigureAwait(false);
                        break;
                }
                Trace.TraceInformation(Invariant($"Executing {command.Id} took {stopWatch.Elapsed}"));
            }
            finally
            {
                if (updateStatus)
                {
                    ClearStatus();
                    UpdateFeedback(FeedbackName.RunningMacro, string.Empty);
                }
            }
        }

        private IDeviceCommandHandler[] GetAllDevices()
        {
            return new IDeviceCommandHandler[]
            {
                GetConnection(DeviceType.XboxOne),
                GetConnection(DeviceType.ADBRemoteControl),
                GetConnection(DeviceType.SonyBluRay),
                GetConnection(DeviceType.PS3),
            };
        }

        private IDeviceCommandHandler GetConnection(DeviceType deviceType)
        {
            IDeviceCommandHandler connection = ConnectionProvider.GetCommandHandler(deviceType);
            return connection;
        }

        private bool? GetFeedbackAsBoolean(IDeviceCommandHandler connection, string feedbackName)
        {
            IDeviceFeedbackProvider deviceFeedbackProvider = ConnectionProvider.GetFeedbackProvider(connection.DeviceType);
            return GetFeedbackAsBoolean(deviceFeedbackProvider, feedbackName);
        }

        private async Task MacroToggleMute(CancellationToken token)
        {
            IDeviceCommandHandler avr = GetConnection(DeviceType.DenonAVR);
            await avr.HandleCommand(CommandName.MuteQuery, token).ConfigureAwait(false);
            await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);

            bool muted = GetFeedbackAsBoolean(avr, FeedbackName.Mute) ?? false;
            await avr.HandleCommand(muted ? CommandName.MuteOff : CommandName.MuteOn, token).ConfigureAwait(false);
        }

        private async Task MacroTurnGameMode(bool on, CancellationToken timeoutToken)
        {
            UpdateStatus($"Setting TV Game Mode to {(on ? "ON" : "OFF")}");
            await MacroTurnGameModeCore(on, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnGameModeCore(bool on, CancellationToken timeoutToken)
        {
            IDeviceCommandHandler tv = GetConnection(DeviceType.SamsungTV);

            UpdateFeedback(FeedbackName.TVGameMode, on);

            await tv.HandleCommand(CommandName.Exit, timeoutToken).ConfigureAwait(false);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);
            await tv.HandleCommand(CommandName.Exit, timeoutToken).ConfigureAwait(false);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);

            await tv.HandleCommand(CommandName.Menu, timeoutToken).ConfigureAwait(false);
            await Task.Delay(850, timeoutToken).ConfigureAwait(false);

            string[] commandsOn = { CommandName.CursorRight,
                                    CommandName.CursorDown,
                                    CommandName.Enter,
                                    CommandName.CursorDown,
                                    CommandName.Enter,
                                    CommandName.CursorDown,
                                    CommandName.Enter,
                                    CommandName.Exit};

            string[] commandsOff = { CommandName.CursorRight,
                                        CommandName.CursorDown,
                                        CommandName.Enter,
                                        CommandName.CursorDown,
                                        CommandName.Enter,
                                        CommandName.CursorUp,
                                        CommandName.Enter,
                                        CommandName.Exit,};

            foreach (string macroCommand in (on ? commandsOn : commandsOff))
            {
                await tv.HandleCommand(macroCommand, timeoutToken).ConfigureAwait(false);
                await Task.Delay(tv.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);
            }
        }

        private async Task MacroTurnoffEverything(CancellationToken timeoutToken)
        {
            UpdateStatus($"Turning Off Devices");

            IDeviceCommandHandler tv = GetConnection(DeviceType.SamsungTV);
            IDeviceCommandHandler avr = GetConnection(DeviceType.DenonAVR);
            IDeviceCommandHandler[] shutdownDevices = GetAllDevices();

            await ShutdownDevices(shutdownDevices, timeoutToken).ConfigureAwait(false);
            await tv.HandleCommandIgnoreException(CommandName.PowerOff, timeoutToken).ConfigureAwait(false);
            await avr.HandleCommandIgnoreException(CommandName.PowerOff, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnNvidiaShield(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.NvidiaShieldInput;
            string inputSwitchCommand = CommandName.ChangeInputMPLAY;
            IDeviceCommandHandler device = GetConnection(DeviceType.ADBRemoteControl);
            IEnumerable<IDeviceCommandHandler> shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.ADBRemoteControl);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, false, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnPS3(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.PS3Input;
            string inputSwitchCommand = CommandName.ChangeInputCD;
            IDeviceCommandHandler device = GetConnection(DeviceType.PS3);
            IEnumerable<IDeviceCommandHandler> shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.PS3);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, true, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnSonyBluRay(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.BlueRayPlayerInput;
            string inputSwitchCommand = CommandName.ChangeInputBD;
            IDeviceCommandHandler device = GetConnection(DeviceType.SonyBluRay);
            IEnumerable<IDeviceCommandHandler> shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.SonyBluRay);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, false, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnXboxOne(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.XBoxOneInput;
            string inputSwitchCommand = CommandName.ChangeInputGAME2;
            IDeviceCommandHandler device = GetConnection(DeviceType.XboxOne);
            IEnumerable<IDeviceCommandHandler> shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.XboxOne);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, true, timeoutToken).ConfigureAwait(false);
        }

        private async Task SetAVRDefaultState(IDeviceCommandHandler avr, CancellationToken timeoutToken)
        {
            await avr.HandleCommand(FeedbackName.DialogEnhancementLevel, 50, timeoutToken).ConfigureAwait(false);
            await Task.Delay(avr.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);
            await avr.HandleCommand(FeedbackName.SubwooferAdjustLevel, 50, timeoutToken).ConfigureAwait(false);
            await Task.Delay(avr.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);

            await EnsureAVRState(avr, false, CommandName.DialogEnhancerModeQuery,
                 CommandName.DialogEnhancerModeOff, FeedbackName.DialogEnhancementMode, 20, timeoutToken).ConfigureAwait(false);
            await EnsureAVRState(avr, false, CommandName.SubWooferLevelAdjustQuery,
                 CommandName.SubWooferLevelAdjustOff, FeedbackName.SubwooferAdjustMode, 20, timeoutToken).ConfigureAwait(false);
            await EnsureAVRState(avr, "Off", CommandName.DynamicVolumeQuery,
                 CommandName.DynamicVolumeOff, FeedbackName.DynamicVolume, 5, timeoutToken).ConfigureAwait(false);
        }

        private async Task ShutdownDevices(IEnumerable<IDeviceCommandHandler> shutdownDevices,
                                                  CancellationToken timeoutToken)
        {
            List<Task> shutdownTasks = new List<Task>();
            foreach (IDeviceCommandHandler shutdownDevice in shutdownDevices)
            {
                shutdownTasks.Add(shutdownDevice.HandleCommandIgnoreException(CommandName.PowerQuery, timeoutToken));
            }

            await shutdownTasks.WhenAll().ConfigureAwait(false);
            shutdownTasks.Clear();

            await DelayDefaultCommandTime(timeoutToken, shutdownDevices.ToArray()).ConfigureAwait(false);

            foreach (IDeviceCommandHandler shutdownDevice in shutdownDevices)
            {
                if (GetFeedbackAsBoolean(shutdownDevice, FeedbackName.Power) ?? true)
                {
                    shutdownTasks.Add(shutdownDevice.HandleCommandIgnoreException(CommandName.PowerOff, timeoutToken));
                }
            }

            await shutdownTasks.WhenAll().ConfigureAwait(false);
        }

        private async Task<bool> TurnDeviceOnIfOff(IDeviceCommandHandler connection, CancellationToken token)
        {
            bool turnedOn = false;
            do
            {
                bool? isOn = GetFeedbackAsBoolean(connection, FeedbackName.Power);
                if (isOn ?? true)
                {
                    break;
                }
                else
                {
                    turnedOn = true;
                    await connection.HandleCommandIgnoreException(CommandName.PowerOn, token).ConfigureAwait(false);
                    await Task.Delay(connection.PowerOnDelay, token).ConfigureAwait(false);
                }

                await connection.HandleCommandIgnoreException(CommandName.PowerQuery, token).ConfigureAwait(false);
                await Task.Delay(connection.DefaultCommandDelay, token).ConfigureAwait(false);
            } while (!token.IsCancellationRequested);

            return turnedOn;
        }

        private async Task TurnOnDevice(string input, string inputSwitchCommand,
                                        IDeviceCommandHandler device,
                                        IEnumerable<IDeviceCommandHandler> shutdownDevices,
                                        bool gameMode,
                                        CancellationToken timeoutToken)
        {
            IDeviceCommandHandler tv = GetConnection(DeviceType.SamsungTV);
            IDeviceCommandHandler avr = GetConnection(DeviceType.DenonAVR);

            UpdateStatus($"Detecting Device Power State");

            List<Task> tasks = new List<Task>
            {
                tv.HandleCommandIgnoreException(CommandName.PowerQuery, timeoutToken),
                avr.HandleCommandIgnoreException(CommandName.PowerQuery, timeoutToken),
                device.HandleCommandIgnoreException(CommandName.PowerQuery, timeoutToken)
            };
            await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
            tasks.Clear();

            await DelayDefaultCommandTime(timeoutToken, device, tv, avr).ConfigureAwait(false);

            UpdateStatus($"Turning On Devices");

            await TurnDeviceOnIfOff(tv, timeoutToken).ConfigureAwait(false);
            bool turnedOnAVR = await TurnDeviceOnIfOff(avr, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Switching {tv.Name} Input");
            await tv.HandleCommandIgnoreException(CommandName.TVAVRInput, timeoutToken).ConfigureAwait(false);
            await DelayDefaultCommandTime(timeoutToken, tv).ConfigureAwait(false);

            // switch to input
            UpdateStatus($"Switching {avr.Name} to {input}");
            bool inputChanged = await EnsureAVRState(avr, input, CommandName.InputStatusQuery,
                                 inputSwitchCommand, FeedbackName.Input, int.MaxValue, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Turning on {device.Name}");

            bool deviceOn = await TurnDeviceOnIfOff(device, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Setting up rest...");

            // Turn on/off Game Mode
            bool? currentGameMode = GetFeedbackAsBoolean(ConnectionProvider.GetFeedbackProvider(DeviceType.GlobalMacros),
                                                       FeedbackName.TVGameMode);

            if (!currentGameMode.HasValue || (currentGameMode.Value != gameMode))
            {
                tasks.Add(MacroTurnGameModeCore(gameMode, timeoutToken).IgnoreException());
            }

            if (turnedOnAVR || inputChanged || deviceOn)
            {
                tasks.Add(SetAVRDefaultState(avr, timeoutToken).IgnoreException());
            }

            //shutdown Devices
            tasks.Add(ShutdownDevices(shutdownDevices, timeoutToken));

            await tasks.WhenAll().ConfigureAwait(false);
            tasks.Clear();
        }

        private async Task SendCommandToAVRInputDevice(string deviceCommand, CancellationToken token)
        {
            IDeviceCommandHandler avr = GetConnection(DeviceType.DenonAVR);
            IDeviceFeedbackProvider feedbackProvider = ConnectionProvider.GetFeedbackProvider(avr.DeviceType);

            object currentValue = feedbackProvider.GetFeedbackValue(FeedbackName.Input);

            if (currentValue == null)
            {
                await avr.HandleCommand(CommandName.InputStatusQuery, token).ConfigureAwait(false);
                await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);
            }

            currentValue = feedbackProvider.GetFeedbackValue(FeedbackName.Input);

            DeviceType? deviceType = null;
            switch (currentValue)
            {
                case DenonAVRControl.NvidiaShieldInput:
                    deviceType = DeviceType.ADBRemoteControl; break;
                case DenonAVRControl.XBoxOneInput:
                    deviceType = DeviceType.XboxOne; break;
                case DenonAVRControl.BlueRayPlayerInput:
                    deviceType = DeviceType.SonyBluRay; break;
            }

            if (deviceType.HasValue)
            {
                IDeviceCommandHandler device = GetConnection(deviceType.Value);
                await device.HandleCommand(deviceCommand, token).ConfigureAwait(false);
            }
            else
            {
                Trace.TraceInformation(Invariant($"There is no device for Input:{currentValue}"));
            }
        }

        private void UpdateStatus(string value)
        {
            UpdateFeedback(FeedbackName.MacroStatus, value);
        }
    }
}