using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi
{
    internal static class TaskHelper
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static async Task WaitForFinishNoCancelException(this Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static async Task<TResult> WaitOnRequestCompletion<TResult>(this Task<TResult> task, CancellationToken token)
        {
            Task finishedTask = await Task.WhenAny(task, Task.Delay(-1, token));

            if (finishedTask == task)
            {
                return task.Result;
            }
            else
            {
                throw new TaskCanceledException();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static async Task WaitOnRequestCompletion(this Task task, CancellationToken token)
        {
            Task finishedTask = await Task.WhenAny(task, Task.Delay(-1, token));

            if (finishedTask == task)
            {
                return;
            }
            else
            {
                throw new TaskCanceledException();
            }
        }
    }
}