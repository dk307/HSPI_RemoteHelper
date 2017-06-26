using Hspi.Connector;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NullGuard;
using System.Globalization;

namespace Hspi.Devices
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class GlobalMacros : DeviceControl
    {
        public GlobalMacros(string name, IReadOnlyDictionary<DeviceType, DeviceControlManager> connections) :
            base(name)
        {
            this.connections = connections;
            AddCommand(new DeviceCommand(CommandName.MacroTurnOnNvidiaShield, type: DeviceCommandType.Both));
            AddCommand(new DeviceCommand(CommandName.MacroTurnOffEverything, type: DeviceCommandType.Both));
            AddCommand(new DeviceCommand(CommandName.MacroGameModeOn, type: DeviceCommandType.Both));
            AddCommand(new DeviceCommand(CommandName.MacroGameModeOff, type: DeviceCommandType.Both));
            AddCommand(new DeviceCommand(CommandName.MacroToggleMute, type: DeviceCommandType.Both));

            AddFeedback(new DeviceFeedback(FeedbackName.RunningMacro, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.MacroStatus, TypeCode.String));
            AddFeedback(new DeviceFeedback(FeedbackName.TVGameMode, TypeCode.Boolean));
        }

        public override bool InvalidState => false;

        public override Task ExecuteCommand(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommand2(command, token);
        }

        private static async Task DelayDefaultCommandTime(CancellationToken timeoutToken, params DeviceControlManager[] devices)
        {
            int msWait = 0;
            foreach (var device in devices)
            {
                msWait = Math.Max(msWait, (int)device.DefaultCommandDelay.TotalMilliseconds);
            }

            await Task.Delay(msWait, timeoutToken).ConfigureAwait(false);
        }

        private static async Task<bool> EnsureAVRState(DeviceControlManager avr, object value,
                                            string valueQueryCommand, string valueChangeCommand,
                                            string feedbackName, CancellationToken token)
        {
            bool changed = false;
            await avr.HandleCommand(valueQueryCommand, token).ConfigureAwait(false);
            do
            {
                await Task.Delay(avr.DefaultCommandDelay, token).ConfigureAwait(false);

                var currentInput = avr.GetFeedbackValue(feedbackName);
                if (object.Equals(currentInput, value))
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
            } while (!token.IsCancellationRequested);

            return changed;
        }

        private static bool? GetFeedbackAsBoolean(DeviceControlManager connection, string feedbackName)
        {
            var value = connection.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static string GetFeedbackAsString(DeviceControlManager connection, string feedbackName)
        {
            var value = connection.GetFeedbackValue(feedbackName);

            if (value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static async Task IgnoreException(Func<Task> action)
        {
            try
            {
                await Task.Run(action).ConfigureAwait(false);
            }
            catch { }
        }

        private static async Task<bool> TurnDeviceOnIfOff(DeviceControlManager connection, CancellationToken token)
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

        private void ClearStatus()
        {
            UpdateFeedback(FeedbackName.MacroStatus, string.Empty);
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
                timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(20));
                var timeoutToken = timeoutTokenSource.Token;
                switch (command.Id)
                {
                    case CommandName.MacroTurnOnNvidiaShield:
                        await MacroTurnOnNvidiaShield(timeoutToken).ConfigureAwait(false);
                        break;

                    case CommandName.MacroTurnOffEverything:
                        await MacroTurnoffEverything(timeoutToken).ConfigureAwait(false);
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

        private DeviceControlManager GetConnection(DeviceType deviceType)
        {
            var conn = connections[deviceType];
            return conn;
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

            var tv = GetConnection(DeviceType.SamsungTV);

            UpdateFeedback(FeedbackName.TVGameMode, on);

            await tv.HandleCommand(CommandName.Exit, timeoutToken);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken);
            await tv.HandleCommand(CommandName.Exit, timeoutToken);
            await Task.Delay(tv.DefaultCommandDelay, timeoutToken);

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
            var adb = GetConnection(DeviceType.ADBRemoteControl);

            await IgnoreException(async () => await adb.HandleCommand(CommandName.PowerOff, timeoutToken));
            await IgnoreException(async () => await avr.HandleCommand(CommandName.PowerOff, timeoutToken));
            await IgnoreException(async () => await tv.HandleCommand(CommandName.PowerOff, timeoutToken));
        }

        private async Task MacroTurnOnNvidiaShield(CancellationToken timeoutToken)
        {
            string input = "MPLAY";
            string inputSwitchCommand = CommandName.ChangeInputMPLAY;
            var device = GetConnection(DeviceType.ADBRemoteControl);
            await TurnOnDevice(input, inputSwitchCommand, device, timeoutToken).ConfigureAwait(false);
        }

        private async Task TurnOnDevice(string input, string inputSwitchCommand,
                                                DeviceControlManager device, CancellationToken timeoutToken)
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

            await DelayDefaultCommandTime(timeoutToken, device, tv, avr);

            UpdateStatus($"Turning On Devices");

            await TurnDeviceOnIfOff(tv, timeoutToken).ConfigureAwait(false);
            bool turnedOnAVR = await TurnDeviceOnIfOff(avr, timeoutToken).ConfigureAwait(false);

            // switch to input
            UpdateStatus($"Switching {avr.Name} Inputs");
            bool inputChanged = await EnsureAVRState(avr, input, CommandName.InputStatusQuery,
                                 inputSwitchCommand, FeedbackName.Input, timeoutToken).ConfigureAwait(false);

            UpdateStatus($"Turning On {device.Name}");

            bool deviceOn = await TurnDeviceOnIfOff(device, timeoutToken).ConfigureAwait(false);

            if (turnedOnAVR || inputChanged || deviceOn)
            {
                UpdateStatus($"Setting Up {avr.Name}");

                await avr.HandleCommand(FeedbackName.DialogEnhancementLevel, 50, timeoutToken).ConfigureAwait(false);
                await Task.Delay(avr.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);
                await avr.HandleCommand(FeedbackName.SubwooferAdjustLevel, 50, timeoutToken).ConfigureAwait(false);
                await Task.Delay(avr.DefaultCommandDelay, timeoutToken).ConfigureAwait(false);

                await EnsureAVRState(avr, false, CommandName.DialogEnhancerModeQuery,
                     CommandName.DialogEnhancerModeOff, FeedbackName.DialogEnhancementMode, timeoutToken).ConfigureAwait(false);
                await EnsureAVRState(avr, false, CommandName.SubWooferLevelAdjustQuery,
                     CommandName.SubWooferLevelAdjustOff, FeedbackName.SubwooferAdjustMode, timeoutToken).ConfigureAwait(false);
            }
        }

        private void UpdateStatus(System.FormattableString formatableString)
        {
            UpdateFeedback(FeedbackName.MacroStatus, formatableString.ToString());
        }

        private readonly IReadOnlyDictionary<DeviceType, DeviceControlManager> connections;
    }
}