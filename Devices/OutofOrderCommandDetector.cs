using NullGuard;

namespace Hspi.Devices
{
    using System.Collections.Generic;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class OutofOrderCommandDetector
    {
        public OutofOrderCommandDetector(string downCommandId, string upCommandId)
        {
            this.downCommandId = downCommandId;
            this.upCommandId = upCommandId;
        }

        public bool ShouldIgnore(string commandId)
        {
            lock (lockObject)
            {
                if ((commandId != upCommandId) && (commandId != downCommandId))
                {
                    commands.Clear();
                    return false;
                }

                bool ignore = false;
                if (commandId == upCommandId)
                {
                    int index = commands.FindLastIndex((x) => x == downCommandId);
                    if (index != -1)
                    {
                        commands.RemoveAt(index);
                    }
                    else
                    {
                        commands.Add(commandId);
                    }
                }
                else
                {
                    // downCommandId
                    int index = commands.FindLastIndex((x) => x == upCommandId);
                    if (index != -1)
                    {
                        // found a matching up command for up command
                        commands.RemoveAt(index);
                        ignore = true;
                    }
                    else
                    {
                        commands.Add(commandId);
                    }
                }
                return ignore;
            }
        }

        private readonly List<string> commands = new List<string>();
        private readonly object lockObject = new object();
        private readonly string downCommandId;
        private readonly string upCommandId;
    }
}