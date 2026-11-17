using System;
using System.Collections.Generic;
using kaliteConfig.GpuOverclock.Models;

namespace kaliteConfig.GpuOverclock.Services
{
    public sealed class OverclockChangeLogger
    {
        private readonly object _gate = new();
        private readonly List<AppliedChangeLogEntry> _tail = new();
        private const int TailLimit = 500;

        public void Log(AppliedChangeLogEntry entry)
        {
            lock (_gate)
            {
                _tail.Add(entry);
                if (_tail.Count > TailLimit) _tail.RemoveRange(0, _tail.Count - TailLimit);
            }
        }

        public IReadOnlyList<AppliedChangeLogEntry> Tail()
        {
            lock (_gate) return _tail.ToArray();
        }
    }
}
