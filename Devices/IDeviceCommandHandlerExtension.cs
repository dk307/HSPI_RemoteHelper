﻿using Hspi.Connector;
using Hspi.Utils;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Devices
{
    internal static class IDeviceCommandHandlerExtension
    {
        public static async Task HandleCommandIgnoreException(this IDeviceCommandHandler handler, string commandId, CancellationToken token)
        {
            try
            {
                await handler.HandleCommand(commandId, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex.IsCancelException())
                {
                    throw;
                }
                Trace.TraceWarning(Invariant($"Command for {handler.DeviceType} to {commandId} failed with {ex.GetFullMessage()}"));
            }
        }
    };
}