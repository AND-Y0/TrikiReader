using System;
using System.Diagnostics;

namespace TrikiReader
{
    public sealed class UiUpdateGate
    {
        private readonly object _sync = new();
        private readonly long _minimumIntervalTicks;
        private bool _isUpdatePending;
        private long _lastScheduledTimestamp = long.MinValue;

        public UiUpdateGate()
            : this(TimeSpan.Zero)
        {
        }

        public UiUpdateGate(TimeSpan minimumInterval)
        {
            if (minimumInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumInterval), "Minimum interval cannot be negative.");
            }

            _minimumIntervalTicks = (long)Math.Ceiling(minimumInterval.TotalSeconds * Stopwatch.Frequency);
        }

        public bool TryBeginSchedule()
        {
            return TryBeginSchedule(Stopwatch.GetTimestamp());
        }

        public bool TryBeginSchedule(long timestamp)
        {
            lock (_sync)
            {
                if (_isUpdatePending)
                {
                    return false;
                }

                if (_lastScheduledTimestamp != long.MinValue &&
                    timestamp - _lastScheduledTimestamp < _minimumIntervalTicks)
                {
                    return false;
                }

                _isUpdatePending = true;
                _lastScheduledTimestamp = timestamp;
                return true;
            }
        }

        public void Complete()
        {
            lock (_sync)
            {
                _isUpdatePending = false;
            }
        }
    }
}
