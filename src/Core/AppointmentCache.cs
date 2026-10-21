using System;
using System.Collections.Generic;
using System.Linq;

namespace WeekKalender.Core
{
    /// <summary>
    /// Korte cache per maandraster (sleutel = eerste dag van het raster). Verlopen gegevens blijven opvraagbaar,
    /// zodat de popup direct iets kan tonen terwijl op de achtergrond wordt ververst.
    /// </summary>
    public sealed class AppointmentCache
    {
        sealed class Entry
        {
            public ProviderResult Result;
            public DateTime StoredAt;
        }

        readonly Dictionary<DateTime, Entry> _entries = new Dictionary<DateTime, Entry>();
        readonly object _lock = new object();
        readonly Func<DateTime> _now;
        readonly int _maxEntries;

        public AppointmentCache(TimeSpan okLifetime, TimeSpan failureLifetime, Func<DateTime> now, int maxEntries = 12)
        {
            OkLifetime = okLifetime;
            FailureLifetime = failureLifetime;
            _now = now ?? (() => DateTime.Now);
            _maxEntries = maxEntries;
        }

        public static readonly TimeSpan ProvisionalLifetime = TimeSpan.FromSeconds(10);

        public TimeSpan OkLifetime { get; set; }
        public TimeSpan FailureLifetime { get; set; }

        public void Put(DateTime key, ProviderResult result)
        {
            if (result == null) return;
            lock (_lock)
            {
                _entries[key.Date] = new Entry { Result = result, StoredAt = _now() };
                if (_entries.Count > _maxEntries)
                {
                    var oldest = _entries.OrderBy(e => e.Value.StoredAt).First().Key;
                    _entries.Remove(oldest);
                }
            }
        }

        /// <summary>Geeft ook verlopen gegevens terug; <paramref name="fresh"/> zegt of ze nog geldig zijn.</summary>
        public bool TryGet(DateTime key, out ProviderResult result, out bool fresh)
        {
            lock (_lock)
            {
                Entry entry;
                if (!_entries.TryGetValue(key.Date, out entry))
                {
                    result = null;
                    fresh = false;
                    return false;
                }
                result = entry.Result;
                TimeSpan lifetime = !entry.Result.IsOk ? FailureLifetime
                    : entry.Result.Provisional ? ProvisionalLifetime
                    : OkLifetime;
                fresh = _now() - entry.StoredAt < lifetime;
                return true;
            }
        }

        public bool IsFresh(DateTime key)
        {
            ProviderResult result;
            bool fresh;
            return TryGet(key, out result, out fresh) && fresh;
        }

        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }
    }
}
