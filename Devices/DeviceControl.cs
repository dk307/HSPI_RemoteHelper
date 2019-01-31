using NullGuard;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceControl : IDisposable
    {
        protected DeviceControl(string name, IConnectionProvider connectionProvider)
        {
            ConnectionProvider = connectionProvider;
            Name = name;
            AddCommand(ConnectCommand);
            AddCommand(NotConnectedCommand);
        }

        ~DeviceControl()
        {
            Dispose(false);
        }

        public event EventHandler<DeviceCommand> CommandChanged;

        public event EventHandler<FeedbackValue> FeedbackChanged;

        public IEnumerable<DeviceCommand> Commands => commands;
        public bool Connected => connected;
        public IEnumerable<DeviceFeedback> Feedbacks => feedbacks;
        public abstract bool InvalidState { get; }
        public string Name { get; }
        protected IConnectionProvider ConnectionProvider { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public abstract Task ExecuteCommand(DeviceCommand command, CancellationToken token);

        public virtual Task ExecuteCommand(FeedbackValue value, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public DeviceCommand GetCommand(string id)
        {
            if (commands.TryGetValue(id, out DeviceCommand command))
            {
                return command;
            }

            throw new CommandNotFoundException(Invariant($"{id} command Not found in {Name}"));
        }

        public virtual Task Refresh(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        internal DeviceFeedback GetFeedback(string feedbackName)
        {
            if (feedbacks.TryGetValue(feedbackName, out DeviceFeedback feedback))
            {
                return feedback;
            }

            throw new FeedbackNotFoundException(Invariant($"{feedback} command Not found in {Name}"));
        }

        protected void AddCommand(DeviceCommand command) => commands.Add(command);

        protected void AddFeedback(DeviceFeedback feedback) => feedbacks.Add(feedback);

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                disposedValue = true;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        protected virtual string TranslateStringFeedback(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string[] words = input.Split(' ', ':');

            List<string> newWords = new List<string>(words.Length);
            foreach (string v in words)
            {
                if (v.Length > 1)
                {
                    newWords.Add(v[0].ToString().ToUpperInvariant() + v.Substring(1).ToLowerInvariant());
                }
                else
                {
                    newWords.Add(v);
                }
            }

            return string.Join(" ", newWords);
        }

        protected void UpdateConnectedState(bool value)
        {
            Trace.WriteLine(Invariant($"Updating Connected State for {Name} to {value}"));
            connected = value;

            CommandChanged?.Invoke(this, value ? ConnectCommand : NotConnectedCommand);
        }

        protected void UpdateFeedback(string feedbackName, object value)
        {
            Trace.WriteLine(Invariant($"Updating {feedbackName} for {Name} to [{value}]"));
            if (feedbacks.TryGetValue(feedbackName, out DeviceFeedback feedback))
            {
                if ((value != null) && (value.GetType() == typeof(string)))
                {
                    value = TranslateStringFeedback((string)value);
                }
                FeedbackChanged?.Invoke(this, new FeedbackValue(feedback, value));
            }
            else
            {
                Trace.WriteLine(Invariant($"Unknown Feedback {feedbackName} for {Name}"));
            }
        }

        public static readonly DeviceCommand ConnectCommand
            = new DeviceCommand(CommandName.ConnectedState, fixedValue: 0, type: DeviceCommandType.Status);

        public static readonly DeviceCommand NotConnectedCommand
            = new DeviceCommand(CommandName.NotConnectedState, fixedValue: 255, type: DeviceCommandType.Status);

        private readonly DeviceCommandCollection commands = new DeviceCommandCollection();
        private readonly DeviceFeedbackCollection feedbacks = new DeviceFeedbackCollection();
        private bool connected = false;
        private bool disposedValue = false;

        [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
        internal class DeviceCommandCollection : KeyedCollection<string, DeviceCommand>
        {
            public bool TryGetValue(string key, out DeviceCommand value)
            {
                return Dictionary.TryGetValue(key, out value);
            }

            protected override string GetKeyForItem(DeviceCommand item)
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }
                return item.Id;
            }
        }

        [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
        internal class DeviceFeedbackCollection : KeyedCollection<string, DeviceFeedback>
        {
            public bool TryGetValue(string key, out DeviceFeedback value)
            {
                return Dictionary.TryGetValue(key, out value);
            }

            protected override string GetKeyForItem(DeviceFeedback item)
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }
                return item.Id;
            }
        }
    };
}