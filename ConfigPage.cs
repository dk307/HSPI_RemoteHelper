﻿using HomeSeerAPI;
using NullGuard;
using Scheduler;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Helper class to generate configuration page for plugin
    /// </summary>
    /// <seealso cref="Scheduler.PageBuilderAndMenu.clsPageBuilder" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ConfigPage : PageBuilderAndMenu.clsPageBuilder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigPage" /> class.
        /// </summary>
        /// <param name="HS">The hs.</param>
        /// <param name="pluginConfig">The plugin configuration.</param>
        public ConfigPage(IHSApplication HS, PluginConfig pluginConfig) : base(pageName)
        {
            this.HS = HS;
            this.pluginConfig = pluginConfig;
        }

        /// <summary>
        /// Gets the name of the web page.
        /// </summary>
        public static string Name => pageName;

        /// <summary>
        /// Get the web page string for the configuration page.
        /// </summary>
        /// <returns>
        /// System.String.
        /// </returns>
        public string GetWebPage(string queryString)
        {
            try
            {
                NameValueCollection parts = HttpUtility.ParseQueryString(queryString);

                string pageType = parts[PageTypeId];

                reset();

                AddHeader(HS.GetPageHeader(Name, "Configuration", string.Empty, string.Empty, false, false));

                System.Text.StringBuilder stb = new System.Text.StringBuilder();
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivStart("pluginpage", ""));
                switch (pageType)
                {
                    case EditDevicePageType:
                        DeviceType deviceId = GetDeviceId(parts);
                        pluginConfig.Devices.TryGetValue(deviceId, out var device);
                        stb.Append(BuildAddNewWebPageBody(device)); break;

                    default:
                    case null:
                        stb.Append(BuildMainWebPageBody()); break;
                }
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivEnd());
                AddBody(stb.ToString());

                AddFooter(HS.GetPageFooter());
                suppressDefaultFooter = true;

                return BuildPage();
            }
            catch (Exception)
            {
                return "error";
            }
        }

        /// <summary>
        /// The user has selected a control on the configuration web page.
        /// The post data is provided to determine the control that initiated the post and the state of the other controls.
        /// </summary>
        /// <param name="data">The post data.</param>
        /// <param name="user">The name of logged in user.</param>
        /// <param name="userRights">The rights of the logged in user.</param>
        /// <returns>Any serialized data that needs to be passed back to the web page, generated by the clsPageBuilder class.</returns>
        public string PostBackProc(string data, [AllowNull]string user, int userRights)
        {
            NameValueCollection parts = HttpUtility.ParseQueryString(data);

            string form = parts["id"];

            if (form == NameToIdWithPrefix(SaveDeviceName))
            {
                StringBuilder results = new StringBuilder();

                // Validate
                IPAddress ipAddress = null;
                if (string.IsNullOrWhiteSpace(parts[DeviceIPId]) ||
                    !IPAddress.TryParse(parts[DeviceIPId], out ipAddress))
                {
                    results.AppendLine("IP Address is not Valid.<br>");
                }

                string name = parts[NameId];
                if (string.IsNullOrWhiteSpace(name))
                {
                    results.AppendLine("Name is not Valid.<br>");
                }

                if (results.Length > 0)
                {
                    this.divToUpdate.Add(SaveErrorDivId, results.ToString());
                }
                else
                {
                    DeviceType deviceId = GetDeviceId(parts);

                    var additionalValues = new Dictionary<string, string>();
                    foreach (var item in pluginConfig.Devices[deviceId].AdditionalValues)
                    {
                        additionalValues[item.Key] = parts[item.Key];
                    }

                    var enabledPorts = new SortedSet<int>();
                    foreach (var item in Enumerable.Range(1, PortsMax))
                    {
                        if (parts[NameToId(item.ToString(CultureInfo.InvariantCulture))] == "checked")
                        {
                            enabledPorts.Add(item);
                        }
                    }

                    var device = new DeviceControlConfig(deviceId, parts[NameId], ipAddress, additionalValues,
                                                         parts[NameToId(EnabledId)] == "checked");

                    try
                    {
                        using (var tmp = device.Create()) { }
                        this.pluginConfig.UpdateDevice(device);
                        this.pluginConfig.FireConfigChanged();
                        this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}")));
                    }
                    catch (Exception ex)
                    {
                        this.divToUpdate.Add(SaveErrorDivId, Invariant($"Some Values are invalid with Error:{ex.GetFullMessage()}"));
                    }
                }
            }
            else if (form == NameToIdWithPrefix(CancelDeviceName))
            {
                this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}")));
            }
            else if (form == NameToIdWithPrefix(DebugLoggingId))
            {
                this.pluginConfig.DebugLogging = parts[NameToId(DebugLoggingId)] == "checked";
            }

            return base.postBackProc(Name, data, user, userRights);
        }

        private static DeviceType GetDeviceId(NameValueCollection parts)
        {
            return (DeviceType)Enum.Parse(typeof(DeviceType), parts[DeviceIdId]);
        }

        protected static string HtmlTextBox(string name, string defaultText, int size = 25, string type = "text", bool @readonly = false)
        {
            return Invariant($"<input type=\'{type}\' id=\'{NameToIdWithPrefix(name)}\' size=\'{size}\' name=\'{name}\' value=\'{defaultText}\' {(@readonly ? "readonly" : string.Empty)}>");
        }

        protected string FormCheckBox(string name, string label, bool @checked, bool autoPostBack = false)
        {
            var cb = new clsJQuery.jqCheckBox(name, label, PageName, true, true)
            {
                id = NameToIdWithPrefix(name),
                @checked = @checked,
                autoPostBack = autoPostBack,
            };
            return cb.Build();
        }

        protected string FormPageButton(string name, string label)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, true)
            {
                id = NameToIdWithPrefix(name),
            };

            return b.Build();
        }

        protected string PageTypeButton(string name, string label, string type, string deviceId = null)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, false)
            {
                id = NameToIdWithPrefix(name),
                url = Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{PageTypeId}={HttpUtility.UrlEncode(type)}&{DeviceIdId}={HttpUtility.UrlEncode(deviceId ?? string.Empty)}"),
            };

            return b.Build();
        }

        private static string NameToId(string name)
        {
            return name.Replace(' ', '_');
        }

        private static string NameToIdWithPrefix(string name)
        {
            return Invariant($"{IdPrefix}{NameToId(name)}");
        }

        private string BuildAddNewWebPageBody(DeviceControlConfig deviceConfig)
        {
            string id = deviceConfig.DeviceType.ToString();
            StringBuilder stb = new StringBuilder();

            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmDeviceChange", "IdChange", "Post"));

            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td style='width:25%'></td><td style='width:20%'></td><td style='width:55%'></td></tr>");
            stb.Append(Invariant($"<tr><td class='tableheader' colspan=3>Edit {deviceConfig.DeviceType}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Name:</td><td class='tablecell' colspan=2>{HtmlTextBox(NameId, deviceConfig.Name)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>DeviceIP:</td><td class='tablecell' colspan=2>{HtmlTextBox(DeviceIPId, deviceConfig.DeviceIP.ToString())}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Enabled:</td><td class='tablecell' colspan=2>{FormCheckBox(EnabledId, string.Empty, deviceConfig.Enabled, false)}</td></tr>"));

            foreach (var item in deviceConfig.AdditionalValues)
            {
                stb.Append(Invariant($"<tr><td class='tablecell'>{item.Key}:</td><td class='tablecell' colspan=2>{HtmlTextBox(item.Key.ToString(), item.Value)}</td></tr>"));
            }

            stb.Append(Invariant($"<tr><td colspan=3>{HtmlTextBox(DeviceIdId, id, type: "hidden")}<div id='{SaveErrorDivId}' style='color:Red'></div></td><td></td></tr>"));
            stb.Append(Invariant($"<tr><td colspan=3>{FormPageButton(SaveDeviceName, "Save")}"));

            stb.Append(FormPageButton(CancelDeviceName, "Cancel"));
            stb.Append(Invariant($"</td><td></td></tr>"));
            stb.Append("<tr height='5'><td colspan=3></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            return stb.ToString();
        }

        private string BuildMainWebPageBody()
        {
            StringBuilder stb = new StringBuilder();

            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td colspan=5></td></tr>");
            stb.Append("<tr><td class='tableheader' colspan=5>Devices</td></tr>");
            stb.Append(@"<tr>" +
                        "<td class='tablecolumn'>Type</td>" +
                        "<td class='tablecolumn'>Name</td>" +
                        "<td class='tablecolumn'>Device IP Address</td>" +
                        "<td class='tablecolumn'>Additional Values</td>" +
                         "<td class='tablecolumn'></td></tr>");

            foreach (var device in pluginConfig.Devices)
            {
                stb.Append(@"<tr>");
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.DeviceType}</td>"));
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.Name}</td>"));
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.DeviceIP}</td>"));
                stb.Append(@"<td class='tablecell'>");
                foreach (var item in device.Value.AdditionalValues)
                {
                    stb.Append(Invariant($"{item.Key}:{item.Value}<br>"));
                }

                stb.Append("</td>");
                stb.Append(Invariant($"<td class='tablecell'>{PageTypeButton(Invariant($"Edit{device.Key}"), "Edit", EditDevicePageType, deviceId: device.Key.ToString())}</ td ></ tr > "));
            }

            stb.Append("<tr height='5'><td colspan=5></td></tr>");
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmSettings", "Id", "Post"));
            stb.Append(Invariant($"<tr><td colspan=5>Debug Logging Enabled:{FormCheckBox(DebugLoggingId, string.Empty, this.pluginConfig.DebugLogging, true)}</ td ></ tr > "));
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            stb.Append(Invariant($"<tr><td colspan=5></td></tr>"));
            stb.Append(@"<tr height='5'><td colspan=5></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");

            return stb.ToString();
        }

        private const string EnabledId = "EnabledId";
        private const string CancelDeviceName = "CancelDeviceName";
        private const string DebugLoggingId = "DebugLoggingId";
        private const string DeviceIdId = "DeviceIdId";
        private const string DeviceIPId = "DeviceIPId";
        private const string EditDevicePageType = "addNew";
        private const string IdPrefix = "id_";
        private const string NameId = "NameId";
        private const string PageTypeId = "type";
        private const int PortsMax = 8;
        private const string SaveDeviceName = "SaveButton";
        private const string SaveErrorDivId = "message_id";
        private static readonly string pageName = Invariant($"{PluginData.PluginName} Configuration").Replace(' ', '_');
        private readonly IHSApplication HS;
        private readonly PluginConfig pluginConfig;
    }
}