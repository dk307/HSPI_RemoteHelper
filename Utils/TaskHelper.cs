﻿using Nito.AsyncEx.Synchronous;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Utils
{
    internal static class TaskHelper
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static T ResultForSync<T>(this Task<T> @this)
        {
            // https://blogs.msdn.microsoft.com/pfxteam/2012/04/13/should-i-expose-synchronous-wrappers-for-asynchronous-methods/
            return Task.Run(() => @this).Result;
        }

        public static void ResultForSync(this Task @this)
        {
            // https://blogs.msdn.microsoft.com/pfxteam/2012/04/13/should-i-expose-synchronous-wrappers-for-asynchronous-methods/
            Task.Run(() => @this).Wait();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "task")]
        public static void StartAsync(Func<Task> taskAction, CancellationToken token)
        {
            var task = Task.Factory.StartNew(() => taskAction(), token,
                                          TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                                          TaskScheduler.Current);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "task")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static void StartAsync(Action action, CancellationToken token)
        {
            var task = Task.Factory.StartNew(action, token,
                                          TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                                          TaskScheduler.Current);
        }
    }
}