using System;
using System.Threading;
using System.Threading.Tasks;
using WeekKalender.Core;

namespace WeekKalender.Providers
{
    /// <summary>
    /// Haalt afspraken op een achtergrondthread op, met cache per maandraster en een time-out.
    /// De interface blokkeert nooit: als Outlook vastloopt (bijvoorbeeld door een melding) komt er een nette melding terug.
    /// </summary>
    public sealed class AppointmentService : IDisposable
    {
        static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(20);

        readonly object _gate = new object();
        readonly StaWorker _worker;
        readonly AppointmentCache _cache;
        readonly TimeSpan _timeout;
        readonly Timer _idleTimer;
        IAppointmentProvider _primary;
        IAppointmentProvider _fallback;
        string _unconfiguredMessage;
        bool _disposed;

        public AppointmentService(IAppointmentProvider primary, IAppointmentProvider fallback, string unconfiguredMessage,
                                  TimeSpan timeout, TimeSpan cacheLifetime, Func<DateTime> now)
        {
            _primary = primary;
            _fallback = fallback;
            _unconfiguredMessage = unconfiguredMessage ?? "Er is geen agenda gekoppeld.";
            _timeout = timeout;
            _cache = new AppointmentCache(cacheLifetime, FailureLifetime, now);
            _worker = new StaWorker("Agendalezer", useMessageFilter: true);
            _idleTimer = new Timer(_ => ReleaseIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public static AppointmentService FromSettings(AppSettings settings)
        {
            ProviderSetup setup = ProviderSetup.Create(settings);
            return new AppointmentService(setup.Primary, setup.Fallback, setup.Message, TimeSpan.FromSeconds(60),
                                          TimeSpan.FromMinutes(settings.CacheMinutes), () => DateTime.Now);
        }

        public void Reconfigure(AppSettings settings)
        {
            ProviderSetup setup = ProviderSetup.Create(settings);
            IAppointmentProvider oldPrimary, oldFallback;
            lock (_gate)
            {
                oldPrimary = _primary;
                oldFallback = _fallback;
                _primary = setup.Primary;
                _fallback = setup.Fallback;
                _unconfiguredMessage = setup.Message ?? "Er is geen agenda gekoppeld.";
            }
            _cache.OkLifetime = TimeSpan.FromMinutes(settings.CacheMinutes);
            _cache.Clear();
            _worker.Post(() =>
            {
                DisposeQuietly(oldPrimary);
                DisposeQuietly(oldFallback);
            });
        }

        public bool CanOpen(Appointment appointment)
        {
            lock (_gate) return appointment.OpenKey != null && _primary is OutlookComProvider;
        }

        public bool TryGetCached(DateTime rangeStart, out ProviderResult result, out bool fresh)
        {
            return _cache.TryGet(rangeStart, out result, out fresh);
        }

        public bool IsFresh(DateTime rangeStart)
        {
            return _cache.IsFresh(rangeStart);
        }

        public void InvalidateCache()
        {
            _cache.Clear();
        }

        public async Task<ProviderResult> GetAsync(DateTime start, DateTime end)
        {
            ProviderResult cached;
            bool fresh;
            if (_cache.TryGet(start, out cached, out fresh) && fresh) return cached;

            IAppointmentProvider primary, fallback;
            string message;
            lock (_gate)
            {
                primary = _primary;
                fallback = _fallback;
                message = _unconfiguredMessage;
            }
            if (primary == null) return ProviderResult.NotAvailable(message);

            // Zit de vorige opdracht nog vast, dan niet nog een opdracht achter in de rij zetten.
            if (_worker.IsBusyLongerThan(_timeout)) return ProviderResult.TimedOut(TimeoutMessage(primary));

            Task<ProviderResult> work = _worker.Run(() => FetchWithFallback(primary, fallback, start, end));
            Task finished = await Task.WhenAny(work, Task.Delay(_timeout)).ConfigureAwait(true);
            ProviderResult result;
            if (finished != work)
            {
                Log.Warn(primary.Name + " reageerde niet binnen " + _timeout.TotalSeconds + " s");
                result = ProviderResult.TimedOut(TimeoutMessage(primary));
                // Komt het antwoord later alsnog, dan bewaren we het voor de volgende keer.
                _ = work.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion && t.Result.IsOk) _cache.Put(start, t.Result); },
                                      TaskScheduler.Default);
            }
            else
            {
                try
                {
                    result = await work.ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    Log.Error("Afspraken ophalen", ex);
                    result = ProviderResult.Failed("Afspraken konden niet worden geladen.");
                }
            }
            _cache.Put(start, result);
            return result;
        }

        public async Task<ProviderResult> OpenAsync(Appointment appointment)
        {
            OutlookComProvider outlook;
            lock (_gate) outlook = _primary as OutlookComProvider;
            if (outlook == null || appointment.OpenKey == null) return ProviderResult.NotAvailable("Deze afspraak kan niet worden geopend.");
            if (_worker.IsBusyLongerThan(_timeout)) return ProviderResult.TimedOut(TimeoutMessage(outlook));

            Task<ProviderResult> work = _worker.Run(() => outlook.Open(appointment));
            Task finished = await Task.WhenAny(work, Task.Delay(_timeout)).ConfigureAwait(true);
            if (finished != work) return ProviderResult.TimedOut(TimeoutMessage(outlook));
            try
            {
                return await work.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error("Afspraak openen", ex);
                return ProviderResult.Failed("De afspraak kon niet worden geopend.");
            }
        }

        static ProviderResult FetchWithFallback(IAppointmentProvider primary, IAppointmentProvider fallback, DateTime start, DateTime end)
        {
            ProviderResult result = SafeFetch(primary, start, end);
            if (!result.IsOk && fallback != null)
            {
                ProviderResult second = SafeFetch(fallback, start, end);
                if (second.IsOk) return second;
            }
            return result;
        }

        static ProviderResult SafeFetch(IAppointmentProvider provider, DateTime start, DateTime end)
        {
            try
            {
                return provider.Fetch(start, end) ?? ProviderResult.Failed("Geen antwoord van " + provider.Name + ".");
            }
            catch (Exception ex)
            {
                Log.Error(provider.Name + " ophalen", ex);
                return ProviderResult.Failed("Afspraken konden niet worden geladen: " + ex.Message);
            }
        }

        static string TimeoutMessage(IAppointmentProvider provider)
        {
            return provider is OutlookComProvider
                ? "Outlook reageert niet. Staat er een melding of venster open in Outlook?"
                : "De agenda reageert niet op tijd.";
        }

        void ReleaseIdle()
        {
            if (_disposed || _worker.IsBusyLongerThan(TimeSpan.Zero)) return;
            OutlookComProvider outlook;
            lock (_gate) outlook = _primary as OutlookComProvider;
            if (outlook != null) _worker.Post(outlook.ReleaseIfIdle);
        }

        static void DisposeQuietly(IDisposable disposable)
        {
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception ex) { Log.Warn("Opruimen: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _idleTimer.Dispose();
            IAppointmentProvider primary, fallback;
            lock (_gate)
            {
                primary = _primary;
                fallback = _fallback;
                _primary = null;
                _fallback = null;
            }
            // COM-objecten loslaten op dezelfde thread waarop ze zijn gemaakt.
            _worker.Post(() =>
            {
                DisposeQuietly(primary);
                DisposeQuietly(fallback);
            });
            _worker.Dispose();
        }
    }

    /// <summary>Kiest de bron op basis van de instellingen en wat er op de pc aanwezig is.</summary>
    public sealed class ProviderSetup
    {
        public IAppointmentProvider Primary;
        public IAppointmentProvider Fallback;
        public string Message;

        public static ProviderSetup Create(AppSettings settings)
        {
            bool hasIcs = !string.IsNullOrWhiteSpace(settings.IcsSource);
            TimeSpan icsLifetime = TimeSpan.FromMinutes(Math.Max(5, settings.CacheMinutes));
            switch (settings.Source)
            {
                case AppSettings.SourceNone:
                    return new ProviderSetup { Message = "De agendakoppeling staat uit (Bron=geen in instellingen.ini)." };
                case AppSettings.SourceIcs:
                    return hasIcs
                        ? new ProviderSetup { Primary = new IcsProvider(settings.IcsSource, icsLifetime) }
                        : new ProviderSetup { Message = "Bron=ics, maar IcsBron is leeg in instellingen.ini." };
                case AppSettings.SourceOutlook:
                    return new ProviderSetup { Primary = new OutlookComProvider(settings.StartOutlook) };
                default:
                    IAppointmentProvider ics = hasIcs ? new IcsProvider(settings.IcsSource, icsLifetime) : null;
                    if (OutlookComProvider.IsInstalled())
                        return new ProviderSetup { Primary = new OutlookComProvider(settings.StartOutlook), Fallback = ics };
                    if (ics != null) return new ProviderSetup { Primary = ics };
                    return new ProviderSetup
                    {
                        Message = "Klassiek Outlook is niet gevonden. Koppel een .ics-agenda om afspraken te zien (zie README)."
                    };
            }
        }
    }
}
