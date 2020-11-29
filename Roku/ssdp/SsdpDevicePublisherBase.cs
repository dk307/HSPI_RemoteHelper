using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Provides the platform independent logic for publishing SSDP devices (notifications and search responses).
    /// </summary>
    internal abstract class SsdpDevicePublisherBase : DisposableManagedObjectBase, ISsdpDevicePublisher
    {
        #region Fields & Constants

        private ISsdpCommunicationsServer _CommsServer;
        private ISsdpLogger _Log;

        private IList<SsdpRootDevice> _Devices;
        private readonly ReadOnlyEnumerable<SsdpRootDevice> _ReadOnlyDevices;

        private System.Threading.Timer _RebroadcastAliveNotificationsTimer;
        private TimeSpan _RebroadcastAliveNotificationsTimeSpan;
        private DateTime _LastNotificationTime;

        private IDictionary<string, SearchRequest> _RecentSearchRequests;

        private Random _Random;
        private TimeSpan _MinCacheTime;
        private TimeSpan _NotificationBroadcastInterval = TimeSpan.Zero;

                #endregion Fields & Constants

        #region Constructors

        /// <summary>
        /// Full constructor.
        /// </summary>
        /// <param name="communicationsServer">The <see cref="ISsdpCommunicationsServer"/> implementation, used to send and receive SSDP network messages.</param>
        /// <param name="osName">Then name of the operating system running the server.</param>
        /// <param name="osVersion">The version of the operating system running the server.</param>
        /// <param name="log">An implementation of <see cref="ISsdpLogger"/> to be used for logging activity. May be null, in which case no logging is performed.</param>
        protected SsdpDevicePublisherBase(ISsdpCommunicationsServer communicationsServer, ISsdpLogger log)
        {
            if (communicationsServer == null) throw new ArgumentNullException(nameof(communicationsServer));

            _Log = log ?? NullLogger.Instance;
            _Devices = new List<SsdpRootDevice>();
            _ReadOnlyDevices = new ReadOnlyEnumerable<SsdpRootDevice>(_Devices);
            _RecentSearchRequests = new Dictionary<string, SearchRequest>(StringComparer.OrdinalIgnoreCase);
            _Random = new Random();

            _CommsServer = communicationsServer;
            _CommsServer.RequestReceived += CommsServer_RequestReceived;

            _Log.LogInfo("Publisher started.");
            _CommsServer.BeginListeningForBroadcasts();
            _Log.LogInfo("Publisher started listening for broadcasts.");
        }

        #endregion Constructors

        #region Public Methods

        /// <summary>
        /// Adds a device (and it's children) to the list of devices being published by this server, making them discoverable to SSDP clients.
        /// </summary>
        /// <remarks>
        /// <para>Adding a device causes "alive" notification messages to be sent immediately, or very soon after. Ensure your device/description service is running before adding the device object here.</para>
        /// <para>Devices added here with a non-zero cache life time will also have notifications broadcast periodically.</para>
        /// <para>This method ignores duplicate device adds (if the same device instance is added multiple times, the second and subsequent add calls do nothing).</para>
        /// </remarks>
        /// <param name="device">The <see cref="SsdpDevice"/> instance to add.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="device"/> argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the <paramref name="device"/> contains property values that are not acceptable to the UPnP 1.0 specification.</exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "AddDevice")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "t", Justification = "Capture task to local variable supresses compiler warning, but task is not really needed.")]
        public void AddDevice(SsdpRootDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            ThrowIfDisposed();

            TimeSpan minCacheTime = TimeSpan.Zero;
            bool wasAdded = false;
            lock (_Devices)
            {
                if (!_Devices.Contains(device))
                {
                    _Devices.Add(device);
                    wasAdded = true;
                    minCacheTime = GetMinimumNonZeroCacheLifetime();
                }
            }

            if (wasAdded)
            {
                LogDeviceEvent("Device added", device);

                _MinCacheTime = minCacheTime;

                SetRebroadcastAliveNotificationsTimer(minCacheTime);

                SendAliveNotifications(device, true);
            }
            else
            {
                LogDeviceEventWarning("AddDevice ignored (duplicate add)", device);
            }
        }

        /// <summary>
        /// Removes a device (and it's children) from the list of devices being published by this server, making them undiscoverable.
        /// </summary>
        /// <remarks>
        /// <para>Removing a device causes "byebye" notification messages to be sent immediately, advising clients of the device/service becoming unavailable. We recommend removing the device from the published list before shutting down the actual device/service, if possible.</para>
        /// <para>This method does nothing if the device was not found in the collection.</para>
        /// </remarks>
        /// <param name="device">The <see cref="SsdpDevice"/> instance to add.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="device"/> argument is null.</exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "RemoveDevice")]
        public void RemoveDevice(SsdpRootDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            ThrowIfDisposed();

            bool wasRemoved = false;
            TimeSpan minCacheTime = TimeSpan.Zero;
            lock (_Devices)
            {
                if (_Devices.Contains(device))
                {
                    _Devices.Remove(device);
                    wasRemoved = true;
                    minCacheTime = GetMinimumNonZeroCacheLifetime();
                }
            }

            if (wasRemoved)
            {
                _MinCacheTime = minCacheTime;

                LogDeviceEvent("Device Removed", device);

                SetRebroadcastAliveNotificationsTimer(minCacheTime);
            }
            else
                LogDeviceEventWarning("RemoveDevice ignored (device not in publisher)", device);
        }

        #endregion Public Methods

        #region Public Properties

        /// <summary>
        /// Returns a reference to the injected <see cref="ISsdpLogger"/> instance.
        /// </summary>
        /// <remarks>
        /// <para>Should never return null. If null was injected a reference to an internal null logger should be returned.</para>
        /// </remarks>
        protected ISsdpLogger Log
        {
            get { return _Log; }
        }

        /// <summary>
        /// Returns a read only list of devices being published by this instance.
        /// </summary>
        public IEnumerable<SsdpRootDevice> Devices
        {
            get
            {
                return _ReadOnlyDevices;
            }
        }

        /// <summary>
        /// Sets or returns a fixed interval at which alive notifications for services exposed by this publisher instance are broadcast.
        /// </summary>
        /// <remarks>
        /// <para>If this is set to <see cref="TimeSpan.Zero"/> then the system will follow the process recommended
        /// by the SSDP spec and calculate a randomised interval based on the cache life times of the published services.
        /// The default and recommended value is TimeSpan.Zero.
        /// </para>
        /// <para>While (zero and) any positive <see cref="TimeSpan"/> value are allowed, the SSDP specification says
        /// notifications should not be broadcast more often than 15 minutes. If you wish to remain compatible with the SSDP
        /// specification, do not set this property to a value greater than zero but less than 15 minutes.
        /// </para>
        /// </remarks>
        public TimeSpan NotificationBroadcastInterval
        {
            get { return _NotificationBroadcastInterval; }
        }

        #endregion Public Properties

        #region Overrides

        /// <summary>
        /// Stops listening for requests, stops sending periodic broadcasts, disposes all internal resources.
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _Log.LogInfo("Publisher disposed.");

                DisposeRebroadcastTimer();

                var commsServer = _CommsServer;
                _CommsServer = null;

                if (commsServer != null)
                {
                    commsServer.RequestReceived -= CommsServer_RequestReceived;
                    if (!commsServer.IsShared)
                        commsServer.Dispose();
                }

                _RecentSearchRequests = null;
            }
        }

        #endregion Overrides

        #region Private Methods

        #region Search Related Methods

        private void ProcessSearchRequest(string searchTarget, UdpEndPoint endPoint)
        {
            _Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "Search Request Received From {0}, Target = {1}", endPoint.ToString(), searchTarget ?? string.Empty));
            if (string.IsNullOrEmpty(searchTarget))
            {
                _Log.LogWarning(string.Format(CultureInfo.InvariantCulture, "Invalid search request received From {0}, Target is null/empty.", endPoint.ToString()));
                return;
            }

            if (IsDuplicateSearchRequest(searchTarget, endPoint))
            {
                Log.LogInfo("Search Request is Duplicate, ignoring.");
                return;
            }

            //Do not block synchronously as that may tie up a threadpool thread for several seconds.
#pragma warning disable CA5394 // Do not use insecure randomness
#pragma warning disable CA2008 // Do not create tasks without passing a TaskScheduler
            TaskEx.Delay(_Random.Next(16, 1000)).ContinueWith((parentTask) =>
#pragma warning restore CA5394 // Do not use insecure randomness
            {
                //Copying devices to local array here to avoid threading issues/enumerator exceptions.
                var devices = GetDevicesMatchingSearchTarget(searchTarget);

                if (devices != null)
                    SendSearchResponses(searchTarget, endPoint, devices);
                else
                    _Log.LogWarning("Sending search responses for 0 devices (no matching targets).");
            });
#pragma warning restore CA2008 // Do not create tasks without passing a TaskScheduler
        }

        private IEnumerable<SsdpDevice> GetDevicesMatchingSearchTarget(string searchTarget)
        {
            IEnumerable<SsdpDevice> devices = null;
            lock (_Devices)
            {
                if ((string.Compare(SsdpConstants.SsdpDiscoverAllSTHeader, searchTarget, StringComparison.OrdinalIgnoreCase) == 0) ||
                    (string.Compare(SsdpConstants.SsdpDiscoverMessage, searchTarget, StringComparison.OrdinalIgnoreCase) == 0) ||
                    (string.Compare(SsdpConstants.UpnpDeviceTypeRootDevice, searchTarget, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    devices = _Devices.ToArray();
                }
                else if (searchTarget.Trim().StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
                {
                    devices = (
                                            from device
                                            in GetAllDevicesAsFlatEnumerable()
                                            where string.Compare(device.Uuid, searchTarget.Substring(5), StringComparison.OrdinalIgnoreCase) == 0
                                            select device
                                        ).ToArray();
                }
                else if (searchTarget.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
                {
                    if (searchTarget.Contains(":service:"))
                    {
                        devices = Array.Empty<SsdpDevice>();
                    }
                    else
                    {
                        devices =
                        (
                            from device
                            in GetAllDevicesAsFlatEnumerable()
                            where string.Compare(device.FullDeviceType, searchTarget, StringComparison.OrdinalIgnoreCase) == 0
                            select device
                        ).ToArray();
                    }
                }
            }

            return devices;
        }

        private IEnumerable<SsdpDevice> GetAllDevicesAsFlatEnumerable()
        {
            return _Devices.Union(_Devices.SelectManyRecursive<SsdpDevice>((d) => Array.Empty<SsdpDevice>()));
        }

        private void SendSearchResponses(string searchTarget, UdpEndPoint endPoint, IEnumerable<SsdpDevice> devices)
        {
            _Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "Sending search (target = {1}) responses for {0} devices", devices.Count(), searchTarget));

            if (!searchTarget.Contains(":service:"))
            {
                foreach (var device in devices)
                {
                    SendDeviceSearchResponses(device, searchTarget, endPoint);
                }
            }
        }

        private void SendDeviceSearchResponses(SsdpDevice device, string searchTarget, UdpEndPoint endPoint)
        {
            //http://www.upnp.org/specs/arch/UPnP-arch-DeviceArchitecture-v1.0-20080424.pdf - page 21
            //For ssdp:all - Respond 3+2d+k times for a root device with d embedded devices and s embedded services but only k distinct service types
            //Root devices - Respond once (special handling when in related/Win Explorer support mode)
            //Udn (uuid) - Response once
            //Device type - response once
            //Service type - respond once per service type

            bool isRootDevice = (device as SsdpRootDevice) != null;
            bool sendAll = searchTarget == SsdpConstants.SsdpDiscoverAllSTHeader;
            bool sendRootDevices = searchTarget == SsdpConstants.UpnpDeviceTypeRootDevice || searchTarget == SsdpConstants.PnpDeviceTypeRootDevice;

            if (isRootDevice && (sendAll || sendRootDevices))
            {
                SendSearchResponse(device, GetUsn(device.Udn, SsdpConstants.UpnpDeviceTypeRootDevice), endPoint);
            }

            if (sendAll || searchTarget.StartsWith("uuid:", StringComparison.Ordinal))
                SendSearchResponse(device, device.Udn, endPoint);

            if (sendAll || searchTarget.Contains(":device:"))
                SendSearchResponse(device, GetUsn(device.Udn, device.FullDeviceType), endPoint);
        }

        private static string GetUsn(string udn, string fullDeviceType)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}::{1}", udn, fullDeviceType);
        }

        private void SendSearchResponse(SsdpDevice device, string uniqueServiceName, UdpEndPoint endPoint)
        {
            var rootDevice = device.ToRootDevice();

            StringBuilder builder = new StringBuilder();
            builder.Append("HTTP/1.1 200 OK\r\n");
            builder.Append(CacheControlHeaderFromTimeSpan(rootDevice));
            builder.Append("\r\n");
            builder.Append("ST: roku:ecp\r\n");
            builder.Append(Invariant($"Location: {rootDevice.Location}\r\n"));
            builder.Append(Invariant($"USN: uuid:roku:ecp:{rootDevice.SerialNumber}:\r\n"));
            builder.Append("\r\n");
            var message = builder.ToString();

            _CommsServer.SendMessage(System.Text.UTF8Encoding.UTF8.GetBytes(message), endPoint);

            LogDeviceEventVerbose(string.Format(CultureInfo.InvariantCulture, "Sent search response ({0}) to {1}", uniqueServiceName, endPoint.ToString()), device);
        }

        private bool IsDuplicateSearchRequest(string searchTarget, UdpEndPoint endPoint)
        {
            var isDuplicateRequest = false;

            var newRequest = new SearchRequest() { EndPoint = endPoint, SearchTarget = searchTarget, Received = DateTime.UtcNow };
            lock (_RecentSearchRequests)
            {
                if (_RecentSearchRequests.ContainsKey(newRequest.Key))
                {
                    var lastRequest = _RecentSearchRequests[newRequest.Key];
                    if (lastRequest.IsOld())
                        _RecentSearchRequests[newRequest.Key] = newRequest;
                    else
                        isDuplicateRequest = true;
                }
                else
                {
                    _RecentSearchRequests.Add(newRequest.Key, newRequest);
                    if (_RecentSearchRequests.Count > 10)
                        CleanUpRecentSearchRequestsAsync();
                }
            }

            return isDuplicateRequest;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "t", Justification = "Capturing task to local variable avoids compiler warning, but value is otherwise not required.")]
        private void CleanUpRecentSearchRequestsAsync()
        {
            var t = TaskEx.Run(() =>
                {
                    lock (_RecentSearchRequests)
                    {
                        foreach (var requestKey in (from r in _RecentSearchRequests where r.Value.IsOld() select r.Key).ToArray())
                        {
                            _RecentSearchRequests.Remove(requestKey);
                        }
                    }
                });
        }

        #endregion Search Related Methods

        #region Notification Related Methods

        #region Alive

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private void SendAllAliveNotifications(object state)
        {
            try
            {
                if (IsDisposed) return;

                try
                {
                    //Only dispose the timer so it gets re-created if we're following
                    //the SSDP Spec and randomising the broadcast interval.
                    //If we're using a fixed interval, no need to kill the timer as it's
                    //already set to go off on the correct interval.
                    if (_NotificationBroadcastInterval == TimeSpan.Zero)
                        DisposeRebroadcastTimer();
                }
                finally
                {
                    // Must reset this here, otherwise if the next reset interval
                    // is calculated to be the same as the previous one we won't
                    // reset the timer.
                    // Reset it to _NotificationBroadcastInterval which is either TimeSpan.Zero
                    // which will cause the system to calculate a new random interval, or it's the
                    // current fixed interval which is fine.
                    _RebroadcastAliveNotificationsTimeSpan = _NotificationBroadcastInterval;
                }

                _Log.LogInfo("Sending Alive Notifications For All Devices");

                _LastNotificationTime = DateTime.Now;

                IEnumerable<SsdpRootDevice> devices;
                lock (_Devices)
                {
                    devices = _Devices.ToArray();
                }

                foreach (var device in devices)
                {
                    if (IsDisposed) return;

                    SendAliveNotifications(device, true);
                }
            }
            catch (Exception ex)
            {
                _Log.LogError("Publisher stopped, exception " + ex.Message);
                Dispose();
            }
            finally
            {
                if (!IsDisposed)
                    SetRebroadcastAliveNotificationsTimer(_MinCacheTime);
            }
        }

        private void SendAliveNotifications(SsdpDevice device, bool isRoot)
        {
            if (isRoot)
            {
                SendAliveNotification(device, SsdpConstants.UpnpDeviceTypeRootDevice, GetUsn(device.Udn, SsdpConstants.UpnpDeviceTypeRootDevice));
            }

            SendAliveNotification(device, device.Udn, device.Udn);
            SendAliveNotification(device, device.FullDeviceType, GetUsn(device.Udn, device.FullDeviceType));
        }

        private void SendAliveNotification(SsdpDevice device, string notificationType, string uniqueServiceName)
        {
            string multicastIpAddress = _CommsServer.DeviceNetworkType.GetMulticastIPAddress();

            var multicastMessage = BuildAliveMessage(device, multicastIpAddress);

            _CommsServer.SendMessage(multicastMessage, new UdpEndPoint
            {
                IPAddress = multicastIpAddress,
                Port = SsdpConstants.MulticastPort
            });

            LogDeviceEvent(string.Format(CultureInfo.InvariantCulture, "Sent alive notification NT={0}, USN={1}", notificationType, uniqueServiceName), device);
        }

        private static byte[] BuildAliveMessage(SsdpDevice device, string hostAddress)
        {
            var rootDevice = device.ToRootDevice();

            StringBuilder builder = new StringBuilder();
            builder.Append("NOTIFY * HTTP/1.1\r\n");
            builder.Append(Invariant($"HOST: {hostAddress}:{SsdpConstants.MulticastPort}\r\n"));
            builder.Append(CacheControlHeaderFromTimeSpan(rootDevice));
            builder.Append("\r\n");
            builder.Append("NT: upnp:rootdevice\r\n");
            builder.Append("NTS: ssdp:alive\r\n");
            builder.Append(Invariant($"Location: {rootDevice.Location}/\r\n"));
            builder.Append(Invariant($"USN: uuid:roku:ecp:{rootDevice.SerialNumber}\r\n"));
            builder.Append("\r\n");

            var message = builder.ToString();

            return System.Text.UTF8Encoding.UTF8.GetBytes(message);
        }

        #endregion Alive

        #region Rebroadcast Timer

        private void DisposeRebroadcastTimer()
        {
            var timer = _RebroadcastAliveNotificationsTimer;
            _RebroadcastAliveNotificationsTimer = null;
            if (timer != null)
                timer.Dispose();
        }

        private void SetRebroadcastAliveNotificationsTimer(TimeSpan minCacheTime)
        {
            TimeSpan rebroadCastInterval = TimeSpan.Zero;
            if (NotificationBroadcastInterval != TimeSpan.Zero)
            {
                if (_RebroadcastAliveNotificationsTimeSpan == NotificationBroadcastInterval) return;

                rebroadCastInterval = NotificationBroadcastInterval;
            }
            else
            {
                if (minCacheTime == _RebroadcastAliveNotificationsTimeSpan) return;
                if (minCacheTime == TimeSpan.Zero) return;

                // According to UPnP/SSDP spec, we should randomise the interval at
                // which we broadcast notifications, to help with network congestion.
                // Specs also advise to choose a random interval up to *half* the cache time.
                // Here we do that, but using the minimum non-zero cache time of any device we are publishing.
#pragma warning disable CA5394 // Do not use insecure randomness
                rebroadCastInterval = new TimeSpan(Convert.ToInt64((_Random.Next(1, 50) / 100D) * (minCacheTime.Ticks / 2)));
#pragma warning restore CA5394 // Do not use insecure randomness
            }

            DisposeRebroadcastTimer();

            // If we were already setup to rebroadcast sometime in the future,
            // don't just blindly reset the next broadcast time to the new interval
            // as repeatedly changing the interval might end up causing us to over
            // delay in sending the next one.
            var nextBroadcastInterval = rebroadCastInterval;
            if (_LastNotificationTime != DateTime.MinValue)
            {
                nextBroadcastInterval = rebroadCastInterval.Subtract(DateTime.Now.Subtract(_LastNotificationTime));
                if (nextBroadcastInterval.Ticks < 0)
                    nextBroadcastInterval = TimeSpan.Zero;
                else if (nextBroadcastInterval > rebroadCastInterval)
                    nextBroadcastInterval = rebroadCastInterval;
            }

            _RebroadcastAliveNotificationsTimeSpan = rebroadCastInterval;
            _RebroadcastAliveNotificationsTimer = new System.Threading.Timer(SendAllAliveNotifications, null, nextBroadcastInterval, rebroadCastInterval);

            _Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "Rebroadcast Interval = {0}, Next Broadcast At = {1}", rebroadCastInterval.ToString(), nextBroadcastInterval.ToString()));
        }

        private TimeSpan GetMinimumNonZeroCacheLifetime()
        {
            var nonzeroCacheLifetimesQuery = (from device
                                                                                in _Devices
                                              where device.CacheLifetime != TimeSpan.Zero
                                              select device.CacheLifetime);

            if (nonzeroCacheLifetimesQuery.Any())
                return nonzeroCacheLifetimesQuery.Min();
            else
                return TimeSpan.Zero;
        }

        #endregion Rebroadcast Timer

        #endregion Notification Related Methods

        private static string GetFirstHeaderValue(System.Net.Http.Headers.HttpRequestHeaders httpRequestHeaders, string headerName)
        {
            string retVal = null;
            if (httpRequestHeaders.TryGetValues(headerName, out IEnumerable<string> values) && values != null)
                retVal = values.FirstOrDefault();

            return retVal;
        }

        private static string CacheControlHeaderFromTimeSpan(SsdpRootDevice device)
        {
            if (device.CacheLifetime == TimeSpan.Zero)
                return "CACHE-CONTROL: no-cache";
            else
                return string.Format(CultureInfo.InvariantCulture, "CACHE-CONTROL: public, max-age={0}", device.CacheLifetime.TotalSeconds);
        }

        private void LogDeviceEvent(string text, SsdpDevice device)
        {
            _Log.LogInfo(GetDeviceEventLogMessage(text, device));
        }

        private void LogDeviceEventWarning(string text, SsdpDevice device)
        {
            _Log.LogWarning(GetDeviceEventLogMessage(text, device));
        }

        private void LogDeviceEventVerbose(string text, SsdpDevice device)
        {
            _Log.LogVerbose(GetDeviceEventLogMessage(text, device));
        }

        private static string GetDeviceEventLogMessage(string text, SsdpDevice device)
        {
            var rootDevice = device as SsdpRootDevice;
            if (rootDevice != null)
                return text + " " + device.DeviceType + " - " + device.Uuid + " - " + rootDevice.Location;
            else
                return text + " " + device.DeviceType + " - " + device.Uuid;
        }

        #endregion Private Methods

        #region Event Handlers

        private void CommsServer_RequestReceived(object sender, RequestReceivedEventArgs e)
        {
            if (IsDisposed) return;

            if (e.Message.Method.Method == SsdpConstants.MSearchMethod)
            {
                ProcessSearchRequest(GetFirstHeaderValue(e.Message.Headers, "ST"), e.ReceivedFrom);
            }
            else if (string.Compare(e.Message.Method.Method, "NOTIFY", StringComparison.OrdinalIgnoreCase) != 0)
                _Log.LogWarning(string.Format(CultureInfo.InvariantCulture, "Unknown request \"{0}\"received, ignoring.", e.Message.Method.Method));
        }

        #endregion Event Handlers

        #region Private Classes

        private class SearchRequest
        {
            public UdpEndPoint EndPoint { get; set; }
            public DateTime Received { get; set; }
            public string SearchTarget { get; set; }

            public string Key
            {
                get { return SearchTarget + ":" + EndPoint.ToString(); }
            }

            public bool IsOld()
            {
                return DateTime.UtcNow.Subtract(Received).TotalMilliseconds > 500;
            }
        }

        #endregion Private Classes
    }
}