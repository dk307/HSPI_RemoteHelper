using NullGuard;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static System.FormattableString;

namespace Hspi.Devices
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ADBShellDDCommand : DeviceCommand
    {
        public ADBShellDDCommand(string id, DirectInputKeys key, int? fixedValue = null, DirectInputKeys? modifier = null)
            : base(id, BuildCommand(key, modifier), fixedValue: fixedValue)
        {
            DirectInputKey = key;
        }

        private enum EventValue
        {
            KeyDown = 1,
            KeyUp = 0,
        }

        public DirectInputKeys DirectInputKey { get; }

        private static string BuildCommand(DirectInputKeys key, DirectInputKeys? modifier)
        {
            StringBuilder stb = new StringBuilder();
            stb.Append("echo -e -n '");

            if (modifier.HasValue)
            {
                stb.Append(GetString(GetEventBytes(modifier.Value, EventValue.KeyDown)));
                stb.Append(GetString(GetBytes(new InputEvent())));
            }

            stb.Append(GetString(GetEventBytes(key, EventValue.KeyDown)));
            stb.Append(GetString(GetBytes(new InputEvent())));

            stb.Append(GetString(GetEventBytes(key, EventValue.KeyUp)));
            stb.Append(GetString(GetBytes(new InputEvent())));

            if (modifier.HasValue)
            {
                stb.Append(GetString(GetEventBytes(modifier.Value, EventValue.KeyUp)));
                stb.Append(GetString(GetBytes(new InputEvent())));
            }

            stb.Append(Invariant($@"' | dd of=/dev/input/event{0}"));
            return stb.ToString();
        }

        private static byte[] GetBytes(InputEvent inputEvent)
        {
            int size = Marshal.SizeOf(inputEvent);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(inputEvent, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        private static byte[] GetEventBytes(DirectInputKeys key, EventValue eventValue)
        {
            InputEvent inputEvent = new InputEvent()
            {
                Type = 1,
                Code = (short)key,
                Value = (int)eventValue,
            };

            return GetBytes(inputEvent);
        }

        private static string GetString(byte[] data)
        {
            StringBuilder stb = new StringBuilder();
            foreach (byte b in data)
            {
                stb.AppendFormat(CultureInfo.InvariantCulture, @"\x{0:x2}", b);
            }
            return stb.ToString();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private unsafe struct InputEvent
        {
            public fixed byte Timestamp[16];
            public short Type;
            public short Code;
            public int Value;
        }
    }
}