using Hspi.Properties;
using Rssdp;
using Rssdp.Infrastructure;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;
using Unosquare.Labs.EmbedIO.Modules;
using static System.FormattableString;

namespace Hspi.Roku
{
    internal sealed class EmulatedRoku : IDisposable
    {
        public EmulatedRoku(ISsdpDevicePublisher publisher,
                            EmulatedRokuSettings emulatedRokuSettings,
                            Func<Guid, string, Task> keyPressedCallback,
                            CancellationToken cancellationToken)
        {
            this.publisher = publisher;
            Settings = emulatedRokuSettings;
            this.keyPressedCallback = keyPressedCallback;
            var advertiseUrl = new Uri(Invariant($"http://{Settings.AdvertiseAddress.Address}:{Settings.AdvertiseAddress.Port}/"));

            string deviceDescriptionXml = string.Format(CultureInfo.InvariantCulture, Resources.SsdpDeviceInfoTemplate, Settings.Name, Settings.SerialNumber, Settings.Id);
            deviceDefinition = new SsdpRootDevice(advertiseUrl, TimeSpan.FromSeconds(300), deviceDescriptionXml);
            Start(cancellationToken);
        }

        ~EmulatedRoku()
        {
            Dispose();
        }

        public void Dispose()
        {
            publisher.RemoveDevice(deviceDefinition);
            server?.Dispose();
            GC.SuppressFinalize(this);
        }

        private CommandsAPIController CreateWebApiModule(IHttpContext context)
        {
            var deviceInfo = string.Format(CultureInfo.InvariantCulture,
                Resources.DeviceInfoTemplate, Settings.Id, Settings.SerialNumber, Settings.Name);
            return new CommandsAPIController(context,
                                            deviceDefinition.ToDescriptionDocument(),
                                            deviceInfo,
                                            (x) => keyPressedCallback(Settings.Id, x));
        }

        private void Start(CancellationToken cancellationToken)
        {
            var serverUrl = new Uri(Invariant($"http://{Settings.RokuAddress.Address}:{Settings.RokuAddress.Port}/"));
            Trace.TraceInformation(Invariant($"Starting Emulated Roku at {serverUrl}"));
            server = new WebServer(new string[] { serverUrl.ToString() }, RoutingStrategy.Regex);
            var webAPIModule = new WebApiModule();
            webAPIModule.RegisterController<CommandsAPIController>();
            server.RegisterModule(new WebApiModule());
            server.Module<WebApiModule>().RegisterController(CreateWebApiModule);
            server.RunAsync(cancellationToken);
            publisher.AddDevice(deviceDefinition);
            Trace.TraceInformation(Invariant($"Started Emulated Roku at {serverUrl} with {server.Listener.IsListening}"));
        }

        public readonly EmulatedRokuSettings Settings;
        private readonly Func<Guid, string, Task> keyPressedCallback;
        private readonly SsdpRootDevice deviceDefinition;

        private readonly ISsdpDevicePublisher publisher;

        private WebServer server;

        internal class CommandsAPIController : WebApiController
        {
            public CommandsAPIController(IHttpContext context, string rokuInfo,
                string rokuDeviceInfo, Func<string, Task> keyPressedCallback)
            : base(context)
            {
                this.rokuInfo = rokuInfo;
                this.rokuDeviceInfo = rokuDeviceInfo;
                this.keyPressedCallback = keyPressedCallback;
            }

            [WebApiHandler(HttpVerbs.Get, @"/query/active-app")]
            public async Task<bool> ActiveApp()
            {
                return await XmlResponse(Resources.ActiveAppTemplate).ConfigureAwait(false);
            }

            [WebApiHandler(HttpVerbs.Get, @"/query/apps")]
            public async Task<bool> AllApps()
            {
                return await XmlResponse(Resources.AppsTemplate).ConfigureAwait(false);
            }

            [WebApiHandler(HttpVerbs.Get, @"/query/device-info")]
            public async Task<bool> DeviceInfo()
            {
                return await XmlResponse(rokuDeviceInfo).ConfigureAwait(false);
            }

            [WebApiHandler(HttpVerbs.Post, @"/input")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3400:Methods should not return constants", Justification = "<Pending>")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
            public bool Input()
            {
                return true;
            }

            [WebApiHandler(HttpVerbs.Post, @"/keydown/{key}")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
            public bool KeyDown(string key)
            {
                return true;
            }

            [WebApiHandler(HttpVerbs.Post, @"/keypress/{key}")]
            public async Task<bool> KeyPress(string key)
            {
                await keyPressedCallback(key).ConfigureAwait(false);
                return true;
            }

            [WebApiHandler(HttpVerbs.Post, @"/keyup/{key}")]
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
            public bool KeyUp(string key)
            {
                return true;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
            [WebApiHandler(HttpVerbs.Post, @"/launch/{id}")]
            public bool LaunchApp(string id)
            {
                return true;
            }

            [WebApiHandler(HttpVerbs.Get, @"/query/icon/{id}")]
            public async Task<bool> QueryIcon(string id)
            {
                var iconData = Convert.FromBase64String(Resources.IconBase64);
                Response.ContentType = @"image\png";
                Response.StatusCode = (int)System.Net.HttpStatusCode.OK;

                using (var stream = new MemoryStream(iconData))
                {
                    Response.ContentLength64 = stream.Length;
#pragma warning disable CS0618 // Type or member is obsolete
                    await Response.BinaryResponseAsync(stream).ConfigureAwait(false);
#pragma warning restore CS0618 // Type or member is obsolete
                }
                return true;
            }

            [WebApiHandler(HttpVerbs.Get, @"/")]
            public async Task<bool> Root()
            {
                return await XmlResponse(rokuInfo).ConfigureAwait(false);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
            [WebApiHandler(HttpVerbs.Post, @"/search")]
            public bool Search()
            {
                return true;
            }

            private async Task<bool> XmlResponse(string xml)
            {
                Response.StatusCode = (int)System.Net.HttpStatusCode.OK;
#pragma warning disable CS0618 // Type or member is obsolete
                return await Response.StringResponseAsync(xml, @"text/xml").ConfigureAwait(false);
#pragma warning restore CS0618 // Type or member is obsolete
            }

            private readonly string rokuDeviceInfo;
            private readonly Func<string, Task> keyPressedCallback;
            private readonly string rokuInfo;
        }
    }
}