using System.Diagnostics;

namespace Rssdp
{
    /// <summary>
    /// Implementation of <see cref="ISsdpLogger"/> that writes to the .Net tracing system on platforms that support it, or <see cref="System.Diagnostics.Debug"/> on those that don't.
    /// </summary>
    /// <remarks>
    /// <para>On platforms that only support <see cref="System.Diagnostics.Debug"/> no log entries will be output unless running a debug build, and this effectively becomes a null logger for release builds.</para>
    /// </remarks>
    internal class SsdpTraceLogger : ISsdpLogger
    {
        /// <summary>
        /// Records a regular log message.
        /// </summary>
        /// <param name="message">The text to be logged.</param>
        public void LogInfo(string message)
        {
        }

        /// <summary>
        /// Records a frequent or large log message usually only required when trying to trace a problem.
        /// </summary>
        /// <param name="message">The text to be logged.</param>
        public void LogVerbose(string message)
        {
        }

        /// <summary>
        /// Records an important message, but one that may not neccesarily be an error.
        /// </summary>
        /// <param name="message">The text to be logged.</param>
        public void LogWarning(string message)
        {
        }

        /// <summary>
        /// Records a message that represents an error.
        /// </summary>
        /// <param name="message">The text to be logged.</param>
        public void LogError(string message)
        {
            Trace.TraceError(message);
        }
    }
}