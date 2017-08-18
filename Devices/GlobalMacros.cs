using Hspi.Connector;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Devices
{
    using static System.FormattableString;

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
            foreach (var device in devices)
            {
                msWait = Math.Max(msWait, (int)device.DefaultCommandDelay.TotalMilliseconds);
            }

            await Task.Delay(msWait, timeoutToken).ConfigureAwait(false);
        }

        private static bool? GetFeedbackAsBoolean(IDeviceFeedbackProvider deviceFeedbackProvider, string feedbackName)
        {
            var value = deviceFeedbackProvider.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        private static string GetFeedbackAsString(IDeviceFeedbackProvider connection, string feedbackName)
        {
            var value = connection.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static async Task IgnoreException(Task task)
        {
            try
            {
                await Task.WhenAll(task).ConfigureAwait(false);
            }
            catch { }
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
            await avr.HandleCommand(valueQueryCommand, token).ConfigureAwait(false);
            var feedbackProvider = ConnectionProvider.GetFeedbackProvider(avr.DeviceType);
            do
            {
                await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);

                var currentValue = feedbackProvider.GetFeedbackValue(feedbackName);
                if (object.Equals(currentValue, expectedValue))
                {
                    break;
                }
                else
                {
                    changed = true;
                    await avr.HandleCommand(valueChangeCommand, token).ConfigureAwait(false);
                    await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);
                }

                await avr.HandleCommand(valueQueryCommand, token).ConfigureAwait(false);
                maxRetries--;
            } while (!token.IsCancellationRequested && (maxRetries > 0));

            return changed;
        }

        private async Task ExecuteCommand2(DeviceCommand command, CancellationToken token)
        {
            Trace.WriteLine(Invariant($"Executing {command.Id} "));

            UpdateFeedback(FeedbackName.RunningMacro, command.Id);
            UpdateCommand(command);
            UpdateStatus($"{command.Id}");

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            try
            {
                CancellationTokenSource timeoutTokenSource = new CancellationTokenSource();
                timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
                var timeoutToken = timeoutTokenSource.Token;
                switch (command.Id)
                {
                    case CommandName.MacroTurnOnNvidiaShield:
                        await MacroTurnOnNvidiaShield(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnXBoxOne:
                        await MacroTurnOnXboxOne(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnPS3:
                        await MacroTurnOnPS3(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOffEverything:
                        await MacroTurnoffEverything(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOnSonyBluRay:
                        await MacroTurnOnSonyBluRay(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroGameModeOn:
                        await MacroTurnGameMode(true, timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroGameModeOff:
                        await MacroTurnGameMode(false, timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroToggleMute:
                        await MacroToggleMute(token).ConfigureAwait(false);
                        break;
                }
                Trace.WriteLine(Invariant($"Executing {command.Id} took {stopWatch.Elapsed}"));
            }
            finally
            {
                ClearStatus();
                UpdateFeedback(FeedbackName.RunningMacro, string.Empty);
                UpdateCommand(ConnectCommand);
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
            var connection = ConnectionProvider.GetCommandHandler(deviceType);
            return connection;
        }

        private bool? GetFeedbackAsBoolean(IDeviceCommandHandler connection, string feedbackName)
        {
            IDeviceFeedbackProvider deviceFeedbackProvider = ConnectionProvider.GetFeedbackProvider(connection.DeviceType);
            return GetFeedbackAsBoolean(deviceFeedbackProvider, feedbackName);
        }

        private async Task MacroToggleMute(CancellationToken token)
        {
            var avr = GetConnection(DeviceType.DenonAVR);
            await avr.HandleCommand(CommandName.MuteQuery, token).ConfigureAwait(false);
            await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);

            var muted = GetFeedbackAsBoolean(avr, FeedbackName.Mute) ?? false;
            await avr.HandleCommand(muted ? CommandName.MuteOff : CommandName.MuteOn, token).ConfigureAwait(false);
        }

        private async Task MacroTurnGameMode(bool on, CancellationToken timeoutToken)
        {
            UpdateStatus($"Setting TV Game Mode to {(on ? "ON" : "OFF")}");
            await MacroTurnGameModeCore(on, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnGameModeCore(bool on, CancellationToken timeoutToken)
        {
            var tv = GetConnection(DeviceType.SamsungTV);

            UpdateFeedback(FeedbackName.TVGameMode, on);

            await tv.HandleCommand(CommandName.Exit, timeoutToken).ConfigureAwait(false);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);
            await tv.HandleCommand(CommandName.Exit, timeoutToken).ConfigureAwait(false);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);

            await tv.HandleCommand(CommandName.Menu, timeoutToken).ConfigureAwait(false);
            await Task.Delay(750, timeoutToken).ConfigureAwait(false);

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

            var tv = GetConnection(DeviceType.SamsungTV);
            var avr = GetConnection(DeviceType.DenonAVR);
            var shutdownDevices = GetAllDevices();

            await ShutdownDevices(shutdownDevices, timeoutToken).ConfigureAwait(false);
            await IgnoreException(tv.HandleCommand(CommandName.PowerOff, timeoutToken)).ConfigureAwait(false);
            await IgnoreException(avr.HandleCommand(CommandName.PowerOff, timeoutToken)).ConfigureAwait(false);
        }

        private async Task MacroTurnOnNvidiaShield(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.NvidiaShieldInput;
            string inputSwitchCommand = CommandName.ChangeInputMPLAY;
            var device = GetConnection(DeviceType.ADBRemoteControl);
            var shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.ADBRemoteControl);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, false, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnPS3(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.PS3Input;
            string inputSwitchCommand = CommandName.ChangeInputCD;
            var device = GetConnection(DeviceType.PS3);
            var shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.PS3);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, true, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnSonyBluRay(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.BlueRayPlayerInput;
            string inputSwitchCommand = CommandName.ChangeInputBD;
            var device = GetConnection(DeviceType.SonyBluRay);
            var shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.SonyBluRay);
            await TurnOnDevice(input, inputSwitchCommand, device, shutdownDevices, false, timeoutToken).ConfigureAwait(false);
        }

        private async Task MacroTurnOnXboxOne(CancellationToken timeoutToken)
        {
            string input = DenonAVRControl.XBoxOneInput;
            string inputSwitchCommand = CommandName.ChangeInputGAME2;
            var device = GetConnection(DeviceType.XboxOne);
            var shutdownDevices = GetAllDevices().Where(x => x.DeviceType != DeviceType.XboxOne);
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
            var shutdownTasks = new List<Task>();
            foreach (var shutdownDevice in shutdownDevices)
            {
                shutdownTasks.Add(IgnoreException(shutdownDevice.HandleCommand(CommandName.PowerQuery, timeoutToken)));
            }

            await shutdownTasks.WhenAll().ConfigureAwait(false);
            shutdownTasks.Clear();

            foreach (var shutdownDevice in shutdownDevices)
            {
                if (GetFeedbackAsBoolean(shutdownDevice, FeedbackName.Power) ?? true)
                {
                    shutdownTasks.Add(IgnoreException(shutdownDevice.HandleCommand(CommandName.PowerOff, timeoutToken)));
                }
            }

            await shutdownTasks.WhenAll().ConfigureAwait(false);
        }

        private async Task<bool> TurnDeviceOnIfOff(IDeviceCommandHandler connection, CancellationToken token)
        {
            bool turnedOn = false;
            do
            {
                var isOn = GetFeedbackAsBoolean(connection, FeedbackName.Power);
                if (isOn ?? true)
                {
                    break;
                }
                else
                {
                    turnedOn = true;
                    await connection.HandleCommand(CommandName.PowerOn, token).ConfigureAwait(false);
                    await Task.Delay(connection.PowerOnDelay, token).ConfigureAwait(false);
                }

                await connection.HandleCommand(CommandName.PowerQuery, token).ConfigureAwait(false);
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
            var tv = GetConnection(DeviceType.SamsungTV);
            var avr = GetConnection(DeviceType.DenonAVR);

            UpdateStatus($"Detecting Device Power State");

            List<Task> tasks = new List<Task>();
            tasks.Add(tv.HandleCommand(CommandName.PowerQuery, timeoutToken));
            tasks.Add(avr.HandleCommand(CommandName.PowerQuery, timeoutToken));
            tasks.Add(device.HandleCommand(CommandName.PowerQuery, timeoutToken));
            await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
            tasks.Clear();

            await DelayDefaultCommandTime(timeoutToken, device, tv, avr).ConfigureAwait(false);

            UpdateStatus($"Turning On Devices");

            await TurnDeviceOnIfOff(tv, timeoutToken).ConfigureAwait(false);
            bool turnedOnAVR = await TurnDeviceOnIfOff(avr, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Switching {tv.Name} Input");
            await tv.HandleCommand(CommandName.TVAVRInput, timeoutToken).ConfigureAwait(false);
            await DelayDefaultCommandTime(timeoutToken, tv).ConfigureAwait(false);

            // switch to input
            UpdateStatus($"Switching {avr.Name} to {input}");
            bool inputChanged = await EnsureAVRState(avr, input, CommandName.InputStatusQuery,
                                 inputSwitchCommand, FeedbackName.Input, int.MaxValue, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Turning on {device.Name}");

            bool deviceOn = await TurnDeviceOnIfOff(device, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Setting up rest...");

            // Turn on/off Game Mode
            var currentGameMode = GetFeedbackAsBoolean(ConnectionProvider.GetFeedbackProvider(DeviceType.GlobalMacros),
                                                       FeedbackName.TVGameMode);

            if (!currentGameMode.HasValue || (currentGameMode.Value != gameMode))
            {
                tasks.Add(MacroTurnGameModeCore(gameMode, timeoutToken));
            }

            if (turnedOnAVR || inputChanged || deviceOn)
            {
                tasks.Add(SetAVRDefaultState(avr, timeoutToken));
            }

            //shutdown Devices
            tasks.Add(ShutdownDevices(shutdownDevices, timeoutToken));

            await tasks.WhenAll().ConfigureAwait(false);
            tasks.Clear();
        }

        private void UpdateStatus(System.FormattableString formatableString)
        {
            UpdateFeedback(FeedbackName.MacroStatus, formatableString.ToString());
        }
    }
}