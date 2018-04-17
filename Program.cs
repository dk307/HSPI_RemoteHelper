using System.Diagnostics;

namespace Hspi
{

    using static System.FormattableString;

    /// <summary>
    /// Class for the main program.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The homeseer server address.  Defaults to the local computer but can be changed through the command line argument, server=address.
        /// </summary>
        private static string serverAddress = "127.0.0.1";

        private const int serverPort = 10400;

        private static ConsoleTraceListener consoleTracer = new ConsoleTraceListener();

        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        /// <param name="args">Command line arguments</param>
        private static void Main(string[] args)
        {
            Trace.Listeners.Add(consoleTracer);
            Trace.WriteLine("Starting...");

            // parse command line arguments
            foreach (string sCmd in args)
            {
                string[] parts = sCmd.Split('=');
                switch (parts[0].ToUpperInvariant())
                {
                    case "SERVER":
                        serverAddress = parts[1];
                        break;
                }
            }

            try
            {
                using (var plugin = new HSPI_RemoteHelper.HSPI())
                {
                    plugin.Connect(serverAddress, serverPort);
                    plugin.WaitforShutDownOrDisconnect();
                    KillAdbProcesses();
                }
            }
            finally
            {
                Trace.WriteLine("Bye!!!");
                KillAdbProcesses();
            }
        }

        private static void KillAdbProcesses()
        {
            foreach (var process in Process.GetProcessesByName("adb"))
            {
                Trace.WriteLine(Invariant($"Killing adb process {process.Id}"));
                process.Kill();
            }
        }
    }
}