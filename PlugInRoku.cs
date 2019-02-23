using Hspi.Pages;
using Hspi.Roku;
using Hspi.Utils;
using Nito.AsyncEx;
using Rssdp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.FormattableString;

namespace Hspi
{
    internal partial class Plugin : HspiBase, IConnectionProvider
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public void LoadRokuToDeviceMapping()
        {
            try
            {
                string fileName = emulatorRokuPluginConfig.CommandMappingFile;
                if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
                {
                    Trace.TraceError(Invariant($"Command Mapping file {fileName} not found"));
                    return;
                }

                XDocument xml = XDocument.Load(fileName);

                var query = from c in xml.Root.Descendants("map")
                            select new
                            {
                                Roku = c.Attribute("roku").Value,
                                Key = c.Attribute("key").Value,
                                Device = c.Attribute("device").Value,
                                Command = c.Attribute("command").Value,
                            };

                var keyPressedTriggersTemp =
                    new Dictionary<KeyPressedTrigger, List<DeviceCommandId>>(new KeyPressedTrigger.EqualityComparer());
                var rokuDevices = emulatorRokuPluginConfig.Devices;
                foreach (var element in query)
                {
                    var rokuDevice = rokuDevices.Values.Where(
                        (x) => { return element.Roku == x.Name; }).FirstOrDefault();

                    if (rokuDevice != null)
                    {
                        var id = new KeyPressedTrigger(rokuDevice.Id, element.Key);

                        if (Enum.TryParse(element.Device, true, out DeviceType deviceType))
                        {
                            var value = new DeviceCommandId(deviceType, element.Command);

                            if (!keyPressedTriggersTemp.TryGetValue(id, out var deviceCommandIds))
                            {
                                deviceCommandIds = new List<DeviceCommandId>();
                                keyPressedTriggersTemp.Add(id, deviceCommandIds);
                            }

                            deviceCommandIds.Add(value);
                        }
                    }
                }

                keyPressedTriggers = keyPressedTriggersTemp.Select((x) => new KeyValuePair<KeyPressedTrigger, IEnumerable<DeviceCommandId>>(x.Key, x.Value))
                                             .ToImmutableDictionary(keyPressedTriggersTemp.Comparer);

                Trace.TraceInformation(Invariant($"Loaded file {fileName}"));
            }
            catch (Exception ex)
            {
                Trace.TraceError(Invariant($"Failed to load device mapping file with {ex.GetFullMessage()}"));
            }
        }

        private async Task RestartRokuOperations()
        {
            try
            {
                using (var sync = await rokuConnectorManagerLock.LockAsync().ConfigureAwait(false))
                {
                    string ipAddress = emulatorRokuPluginConfig.SSDAdvertiseAddress?.ToString();
                    var newPublisher = new SsdpDevicePublisher(ipAddress ?? IPAddress.Any.ToString());

                    // This returns a new copy every time
                    var currentDevices = emulatorRokuPluginConfig.Devices;

                    // Update changed or new
                    foreach (var device in emulatorRokuPluginConfig.Devices)
                    {
                        if (rokuConnectorManager.TryGetValue(device.Key, out var oldConnector))
                        {
                            if (!device.Value.Equals(oldConnector.Settings))
                            {
                                oldConnector.Dispose();
                                rokuConnectorManager[device.Key] = new EmulatedRoku(newPublisher,
                                                                                device.Value,
                                                                                TriggerFireKeyPressed,
                                                                                ShutdownCancellationToken);
                            }
                        }
                        else
                        {
                            rokuConnectorManager.Add(device.Key, new EmulatedRoku(newPublisher,
                                                                              device.Value,
                                                                              TriggerFireKeyPressed,
                                                                              ShutdownCancellationToken));
                        }
                    }

                    // Remove deleted
                    var removalList = new List<Guid>();
                    foreach (var deviceKeyPair in rokuConnectorManager)
                    {
                        if (!currentDevices.ContainsKey(deviceKeyPair.Key))
                        {
                            deviceKeyPair.Value.Dispose();
                            removalList.Add(deviceKeyPair.Key);
                        }
                    }

                    foreach (var key in removalList)
                    {
                        rokuConnectorManager.Remove(key);
                    }

                    publisher?.Dispose();
                    publisher = newPublisher;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(Invariant($"Failed in starting rokus with {ex.GetFullMessage()}"));
            }
        }

        private async Task TriggerFireKeyPressed(Guid deviceId, string key)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Trace.Write(Invariant($"Key [{key}] Pressed on {deviceId}"));
            var keyPressedTrigger = new KeyPressedTrigger(deviceId, key);
            if (keyPressedTriggers.TryGetValue(keyPressedTrigger, out var deviceCommandIds))
            {
                foreach (var deviceCommandId in deviceCommandIds)
                {
                    await HandleCommand(deviceCommandId.Device, deviceCommandId.CommandId).ConfigureAwait(false);
                }

                var timeLeft = emulatorRokuPluginConfig.MinWaitForKeyPress - stopwatch.Elapsed;

                if (timeLeft > TimeSpan.Zero)
                {
                    Trace.Write(Invariant($"Wait for [{key}] {timeLeft}"));
                    await Task.Delay(timeLeft).ConfigureAwait(false);
                }
            }
            else
            {
                Trace.Write(Invariant($"No handler for [{key}] on {deviceId}"));
            }
        }

        private readonly Dictionary<Guid, EmulatedRoku> rokuConnectorManager = new Dictionary<Guid, EmulatedRoku>();

        private readonly AsyncLock rokuConnectorManagerLock = new AsyncLock();

        private EmulatedRokuConfigPage emulatedRokuConfigPage;

        private EmulatorRokuConfig emulatorRokuPluginConfig;

        private volatile ImmutableDictionary<KeyPressedTrigger, IEnumerable<DeviceCommandId>> keyPressedTriggers =
             ImmutableDictionary<KeyPressedTrigger, IEnumerable<DeviceCommandId>>.Empty;

        private SsdpDevicePublisher publisher;

        private readonly struct DeviceCommandId
        {
            public DeviceCommandId(DeviceType device, string commandId)
            {
                Device = device;
                CommandId = commandId;
            }

            public string CommandId { get; }
            public DeviceType Device { get; }
        }
    }
}