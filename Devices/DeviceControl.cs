﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hspi.Devices
{
    using static System.FormattableString;

    internal abstract class DeviceControl : IDisposable
    {
        protected DeviceControl(string name)
        {
            Name = name;
            AddFeedback(new DeviceFeedback(FeedbackName.Connection, TypeCode.Boolean));
        }

        ~DeviceControl()
        {
            Dispose(false);
        }

        public event EventHandler<FeedbackValue> FeedbackChanged;

        public IEnumerable<DeviceCommand> Commands => commands;

        public bool Connected => connected;

        public IEnumerable<DeviceFeedback> Feedbacks => feedbacks;

        public abstract bool InvalidState { get; }
        public string Name { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual async Task ExecuteCommand(DeviceCommand command, CancellationToken token = default(CancellationToken))
        {
            await Task.Delay(0);
            throw new NotImplementedException();
        }

        public virtual async Task ExecuteCommand(FeedbackValue value, CancellationToken token)
        {
            await Task.Delay(0);
            throw new NotImplementedException();
        }

        public DeviceCommand GetCommand(string id)
        {
            if (commands.TryGetValue(id, out var command))
            {
                return command;
            }

            throw new CommandNotFoundException(Invariant($"{id} command Not found in {Name}"));
        }

        internal DeviceFeedback GetFeedback(string feedbackName)
        {
            if (feedbacks.TryGetValue(feedbackName, out var feedback))
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

        protected void UpdateConnectedState(bool value)
        {
            Trace.WriteLine(Invariant($"Updating Connected State for {Name} to {value}"));

            connected = value;
            UpdateFeedback(FeedbackName.Connection, value);
        }

        protected void UpdateFeedback(string feedbackName, object value)
        {
            Trace.WriteLine(Invariant($"Updating {feedbackName} for {Name} to {value}"));
            if (feedbacks.TryGetValue(feedbackName, out var feedback))
            {
                FeedbackChanged?.Invoke(this, new FeedbackValue(feedback, value));
            }
            else
            {
                Trace.WriteLine(Invariant($"Unknown Feedback {feedbackName} for {Name}"));
            }
        }

        private readonly DeviceCommandCollection commands = new DeviceCommandCollection();
        private readonly DeviceFeedbackCollection feedbacks = new DeviceFeedbackCollection();
        private bool connected = false;
        private bool disposedValue = false;

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