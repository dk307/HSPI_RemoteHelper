using Hspi.Utils;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class IP2IRDeviceControl : IPAddressableDeviceControl
    {
        public IP2IRDeviceControl(string name, IPAddress deviceIP,
                                  TimeSpan defaultCommandDelay,
                                  string fileName,
                                  IConnectionProvider connectionProvider,
                                  AsyncProducerConsumerQueue<DeviceCommand> commandQueue,
                                  AsyncProducerConsumerQueue<FeedbackValue> feedbackQueue) :
            base(name, deviceIP, defaultCommandDelay, connectionProvider, commandQueue, feedbackQueue)
        {
            // <commands>
            // <ir name="Samsung TV HDMI4",\  id="-100", port="1:2", pronto="0000 33 44 "/>
            // </commands>

            XDocument xml = XDocument.Load(fileName);

            var query = from c in xml.Root.Descendants("ir")
                        select new
                        {
                            Name = c.Attribute("name").Value,
                            FixedValue = int.Parse(c.Attribute("id").Value, CultureInfo.InvariantCulture),
                            Port = c.Attribute("port").Value,
                            Gc = c.Attribute("gc").Value,
                        };

            foreach (var element in query)
            {
                SendIRCommand command = new SendIRCommand(element.Name, element.Port,
                                                element.Gc, element.FixedValue);
                AddCommand(command);
            }
        }

        public override bool InvalidState
        {
            get
            {
                if (client != null)
                {
                    return !client.Connected;
                }
                return false;
            }
        }

        public override Task Refresh(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            stopTokenSource?.Cancel();
            if (disposing)
            {
                DisposeConnection();
            }

            base.Dispose(disposing);
        }

        protected override Task ExecuteCommandCore(DeviceCommand command, CancellationToken token)
        {
            return ExecuteCommandCore2(command, token);
        }

        private static async Task<string> ReadLineAsync(StreamReader reader)
        {
            StringBuilder sb = new StringBuilder();
            char[] buffer = new char[1];
            while (!reader.EndOfStream)
            {
                await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false);

                if (buffer[0] == Seperator)
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(buffer[0]);
                }
            }

            return sb.ToString();
        }

        private async Task Connect(CancellationToken token)
        {
            if (!await IsNetworkOn(token).ConfigureAwait(false))
            {
                throw new DevicePoweredOffException($"IP2IR {Name} on {DeviceIP} not powered On");
            }

            client = new TcpClient()
            {
                NoDelay = true,
            };

            stopTokenSource = new CancellationTokenSource();

            await client.ConnectAsync(DeviceIP.ToString(), Port).ConfigureAwait(false);
            await UpdateConnectedState(true, token).ConfigureAwait(false);

            stream = client.GetStream();
            MyTaskHelper.StartAsync(() => ProcessRead(stopTokenSource.Token), stopTokenSource.Token);
        }

        private void DisposeConnection()
        {
            if (client != null)
            {
                client.CloseConnection();
                client.Dispose();
            }
        }

        private async Task ExecuteCommandCore2(DeviceCommand command, CancellationToken token)
        {
            using (await connectionLock.LockAsync().ConfigureAwait(false))
            {
                Trace.WriteLine(Invariant($"Sending {command.Id} to IP2IR {Name} on {DeviceIP}"));

                if (!Connected)
                {
                    await Connect(token).ConfigureAwait(false);
                }

                int irCounter = Interlocked.Increment(ref counter);
                string data = string.Format(CultureInfo.InvariantCulture, command.Data, irCounter) + Seperator;

                byte[] bytes = encoding.GetBytes(data);
                TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
                commandResponseWaitQueue[irCounter] = taskCompletionSource;
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);

                CancellationTokenSource delay = new CancellationTokenSource(DefaultCommandDelay + DefaultCommandDelay);
                await taskCompletionSource.Task
                                          .WaitAsync(CancellationTokenSource.CreateLinkedTokenSource(token, delay.Token).Token)
                                          .ConfigureAwait(false);
            }
        }

        private async Task<bool> IsNetworkOn(CancellationToken token)
        {
            TimeSpan networkPingTimeout = TimeSpan.FromMilliseconds(500);
            return await NetworkHelper.PingHost(DeviceIP, 80, networkPingTimeout, token).ConfigureAwait(false);
        }

        private void ProcessFeedback(string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
            {
                return;
            }

            Trace.WriteLine(Invariant($"Feedback from IP2IR {Name}:{feedback}"));

            switch (feedback)
            {
                case var completeIR when feedback.StartsWith("completeir", StringComparison.Ordinal):
                    if (int.TryParse(completeIR.Substring(15), out int sequenceNumber))
                    {
                        if (commandResponseWaitQueue.TryRemove(sequenceNumber, out TaskCompletionSource<bool> completionSource))
                        {
                            completionSource.SetResult(true);
                        }
                    }
                    else
                    {
                        Trace.WriteLine(Invariant($"Unknown Compeltion response from {Name}:{feedback}"));
                    }
                    break;

                case var unknownError when feedback.StartsWith("unknowncommand,", StringComparison.Ordinal) && feedback.Length > 16:
                    SetError(unknownError.Substring(15));
                    break;

                case var errorMessage when iTachErrorResponse.IsMatch(feedback):
                    bool found = false;
                    Match matches = iTachErrorResponse.Match(errorMessage);
                    if (matches.Success)
                    {
                        Group errorGroup = matches.Groups["error"];
                        if (errorGroup.Success)
                        {
                            if (int.TryParse(errorGroup.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int errorId))
                            {
                                if (errorId > 0 && errorId <= ItachErrorMessages.Length)
                                {
                                    SetError(ItachErrorMessages[errorId]);
                                    found = true;
                                }
                            }
                        }

                        if (!found)
                        {
                            SetError(errorGroup.Value);
                        }
                    }
                    else
                    {
                        SetError(feedback);
                    }
                    break;

                default:
                    SetError(feedback);
                    break;
            }
        }

        private async Task ProcessRead(CancellationToken combinedToken)
        {
            try
            {
                using (StreamReader reader = new StreamReader(stream, encoding))
                {
                    while (!combinedToken.IsCancellationRequested)
                    {
                        string feedback = await ReadLineAsync(reader).ConfigureAwait(false);
                        ProcessFeedback(feedback);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.IsCancelException())
                {
                    Trace.WriteLine(Invariant($"Connection to Denon AVR {Name} on {DeviceIP} dropped with {ExceptionHelper.GetFullMessage(ex)}"));
                }
                await UpdateConnectedState(false, combinedToken).ConfigureAwait(false);
            }
        }

        private void SetError(string error)
        {
            Trace.WriteLine(Invariant($"Command to {Name} on {DeviceIP} failed with {error}"));

            var keys = commandResponseWaitQueue.Keys;

            foreach (int key in keys)
            {
                if (commandResponseWaitQueue.TryRemove(key, out TaskCompletionSource<bool> completionSource))
                {
                    completionSource.SetException(new DeviceException(Invariant($"Failed with {error}")));
                }
            }
        }

        private const int Port = 4998;

        private const char Seperator = '\r';

        // Errors returned by GlobalCache iTach devices
        private static readonly string[] ItachErrorMessages = new string[]
        {
            // 0
            "Unknown error",
            // 1
            "Invalid command. Command not found.",
            // 2
            "Invalid module address (does not exist).",
            // 3
            "Invalid connector address (does not exist).",
            // 4
            "Invalid ID value.",
            // 5
            "Invalid frequency value",
            // 6
            "Invalid repeat value.",
            // 7
            "Invalid offset value.",
            // 8
            "Invalid pulse count.",
            // 9
            "Invalid pulse data.",
            // 10
            "Uneven amount of <on|off> statements.",
            // 11
            "No carriage return found.",
            // 12
            "Repeat count exceeded.",
            // 13
            "IR command sent to input connector.",
            // 14
            "Blaster command sent to non-blaster connector.",
            // 15
            "No carriage return before buffer full.",
            // 16
            "No carriage return.",
            // 17
            "Bad command syntax.",
            // 18
            "Sensor command sent to non-input connector.",
            // 19
            "Repeated IR transmission failure.",
            // 20
            "Above designated IR <on|off> pair limit.",
            // 21
            "Symbol odd boundary.",
            // 22
            "Undefined symbol.",
            // 23
            "Unknown option.",
            // 24
            "Invalid baud rate setting.",
            // 25
            "Invalid flow control setting.",
            // 26
            "Invalid parity setting.",
            // 27
            "Settings are locked."
        };

        private static readonly Regex iTachErrorResponse = new Regex(@"ERR_(?<mod>[0-9]):(?<con>[0-3]),(?<error>\d+)",
                                                     RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static volatile int counter = 0;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> commandResponseWaitQueue
                                               = new ConcurrentDictionary<int, TaskCompletionSource<bool>>();

        private readonly AsyncLock connectionLock = new AsyncLock();

        private readonly Encoding encoding = Encoding.GetEncoding("ISO-8859-1");

        private TcpClient client;

        private CancellationTokenSource stopTokenSource;

        private NetworkStream stream;

        [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
        private class SendIRCommand : DeviceCommand
        {
            public SendIRCommand(string id, string port,
                                string gc, int fixedValue)
                : base(id, CreateCommand(port, gc), fixedValue: fixedValue)
            {
            }

            private static string CreateCommand(string port, string gc)
            {
                StringBuilder stb = new StringBuilder();
                stb.Append("sendir,");
                stb.Append(port);
                stb.Append(",{0},");
                stb.Append(gc);
                return stb.ToString();
            }
        }
    }
}