using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WeekKalender.Core;

namespace WeekKalender.Providers
{
    /// <summary>
    /// Leest de standaardagenda van klassiek Outlook via het Outlook-objectmodel (COM), inclusief terugkerende afspraken.
    ///
    /// ALLEEN LEZEN: deze klasse roept nooit Save, Delete, Move, Send, Copy of Close aan en wijzigt geen eigenschappen
    /// van agenda-items. build.ps1 controleert dat bij elke build. Open() toont een afspraak in een Outlook-venster;
    /// wat de gebruiker daar doet, gebeurt in Outlook zelf.
    ///
    /// Alle aanroepen gebeuren op de STA-thread van AppointmentService.
    /// </summary>
    public sealed class OutlookComProvider : IAppointmentProvider
    {
        public const string DefaultProgId = "Outlook.Application";
        const int OlFolderCalendar = 9;
        const int OlAppointmentClass = 26;
        const int MaxItems = 5000;

        readonly string _progId;
        readonly bool _allowStart;
        readonly TimeSpan _idleRelease;
        readonly Func<DateTime> _now;
        static readonly TimeSpan SyncGracePeriod = TimeSpan.FromSeconds(90);

        object _app;
        bool _startedByUs;
        DateTime _startedAtUtc;
        DateTime _lastUse;

        public OutlookComProvider(bool allowStart, string progId = DefaultProgId, TimeSpan? idleRelease = null, Func<DateTime> now = null)
        {
            _allowStart = allowStart;
            _progId = progId;
            _idleRelease = idleRelease ?? TimeSpan.FromMinutes(5);
            _now = now ?? (() => DateTime.Now);
        }

        public string Name
        {
            get { return "Outlook"; }
        }

        public static bool IsInstalled(string progId = DefaultProgId)
        {
            try { return Type.GetTypeFromProgID(progId, false) != null; }
            catch (Exception) { return false; }
        }

        public ProviderResult Fetch(DateTime start, DateTime end)
        {
            try
            {
                string reason;
                object app = Acquire(out reason);
                if (app == null) return ProviderResult.NotAvailable(reason);
                List<Appointment> items = ReadRange(app, start, end);
                // Net door ons gestart: Outlook haalt dan nog wijzigingen op van de server.
                bool provisional = _startedByUs && DateTime.UtcNow - _startedAtUtc < SyncGracePeriod;
                return ProviderResult.Success(items, Name, provisional);
            }
            catch (COMException ex)
            {
                Log.Warn("Outlook lezen: " + ex.Message + " (0x" + ex.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ")");
                ReleaseApp();
                return Describe(ex);
            }
            catch (Exception ex)
            {
                Log.Error("Outlook lezen", ex);
                ReleaseApp();
                return ProviderResult.Failed("De Outlook-agenda kon niet worden gelezen.");
            }
            finally
            {
                Done();
            }
        }

        /// <summary>Toont de afspraak (bij een reeks: dit ene voorkomen) in een Outlook-venster.</summary>
        public ProviderResult Open(Appointment appointment)
        {
            dynamic ns = null, item = null, pattern = null, occurrence = null;
            try
            {
                string reason;
                object app = Acquire(out reason);
                if (app == null) return ProviderResult.NotAvailable(reason);
                ns = ((dynamic)app).GetNamespace("MAPI");
                item = ns.GetItemFromID(appointment.OpenKey);
                dynamic target = item;
                if (appointment.IsRecurring)
                {
                    try
                    {
                        pattern = item.GetRecurrencePattern();
                        occurrence = pattern.GetOccurrence(appointment.Start);
                        target = occurrence;
                    }
                    catch (COMException)
                    {
                        // Voorkomen niet gevonden: dan de reeks zelf tonen.
                    }
                }
                target.Display(false);
                return ProviderResult.Success(null, Name);
            }
            catch (COMException ex)
            {
                Log.Warn("Outlook openen: " + ex.Message + " (0x" + ex.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ")");
                ReleaseApp();
                return Describe(ex);
            }
            catch (Exception ex)
            {
                Log.Error("Outlook openen", ex);
                ReleaseApp();
                return ProviderResult.Failed("De afspraak kon niet worden geopend in Outlook.");
            }
            finally
            {
                Release(occurrence);
                Release(pattern);
                Release(item);
                Release(ns);
                Done();
            }
        }

        /// <summary>Laat een door ons gestart Outlook los na een tijd zonder gebruik, zodat het zichzelf afsluit.</summary>
        public void ReleaseIfIdle()
        {
            if (_app != null && _now() - _lastUse > _idleRelease)
            {
                ReleaseApp();
                Log.Info("Outlook-verbinding vrijgegeven na inactiviteit");
            }
        }

        public void Dispose()
        {
            ReleaseApp();
        }

        void Done()
        {
            _lastUse = _now();
            // Een Outlook dat de gebruiker zelf open had, houden we niet vast.
            if (!_startedByUs) ReleaseApp();
        }

        object Acquire(out string reason)
        {
            reason = null;
            if (_app != null)
            {
                try
                {
                    object version = ((dynamic)_app).Version;   // leeft Outlook nog?
                    return _app;
                }
                catch (Exception)
                {
                    ReleaseApp();
                }
            }

            Type type = Type.GetTypeFromProgID(_progId, false);
            if (type == null)
            {
                reason = "Klassiek Outlook is niet geïnstalleerd. Koppel eventueel een .ics-agenda (zie README).";
                return null;
            }

            try
            {
                _app = Marshal.GetActiveObject(_progId);
                _startedByUs = false;
                return _app;
            }
            catch (COMException)
            {
                // Draait niet (of nog niet geregistreerd).
            }

            bool running = IsOutlookProcessRunning();
            if (!running && !_allowStart)
            {
                reason = "Klassiek Outlook is niet geopend.";
                return null;
            }

            _app = Activator.CreateInstance(type);
            _startedByUs = !running;
            _startedAtUtc = DateTime.UtcNow;
            Log.Info(_startedByUs ? "Klassiek Outlook op de achtergrond gestart" : "Verbonden met draaiend Outlook");
            return _app;
        }

        static List<Appointment> ReadRange(object app, DateTime start, DateTime end)
        {
            var result = new List<Appointment>();
            dynamic ns = null, folder = null, items = null, restricted = null;
            try
            {
                ns = ((dynamic)app).GetNamespace("MAPI");
                folder = ns.GetDefaultFolder(OlFolderCalendar);
                items = folder.Items;
                // Volgorde is belangrijk: eerst sorteren, dan herhalingen aanzetten, dan filteren.
                items.Sort("[Start]");
                items.IncludeRecurrences = true;
                restricted = Restrict(items, start.AddDays(-1), end.AddDays(1));

                dynamic item = restricted.GetFirst();
                int guard = 0;
                while (item != null && guard++ < MaxItems)
                {
                    try
                    {
                        Appointment appointment = ToAppointment(item);
                        if (appointment != null && AppointmentDay.Overlaps(appointment, start, end)) result.Add(appointment);
                    }
                    finally
                    {
                        Release(item);
                    }
                    item = restricted.GetNext();
                }
            }
            finally
            {
                Release(restricted);
                Release(items);
                Release(folder);
                Release(ns);
            }
            return result;
        }

        static dynamic Restrict(dynamic items, DateTime from, DateTime to)
        {
            // Outlook verwacht datums in de korte datumnotatie van de gebruiker; als dat niet lukt, de Amerikaanse notatie.
            var attempts = new[]
            {
                Tuple.Create("g", CultureInfo.CurrentCulture),
                Tuple.Create("MM/dd/yyyy hh:mm tt", CultureInfo.GetCultureInfo("en-US")),
            };
            COMException last = null;
            foreach (var attempt in attempts)
            {
                string filter = "[Start] <= '" + to.ToString(attempt.Item1, attempt.Item2) + "' AND [End] >= '" + from.ToString(attempt.Item1, attempt.Item2) + "'";
                try
                {
                    return items.Restrict(filter);
                }
                catch (COMException ex)
                {
                    last = ex;
                    Log.Warn("Outlook-filter geweigerd (" + attempt.Item2.Name + "): " + ex.Message);
                }
            }
            throw last;
        }

        static Appointment ToAppointment(dynamic item)
        {
            if ((int)item.Class != OlAppointmentClass) return null;
            string subject = (string)item.Subject;
            string location = (string)item.Location;
            return new Appointment
            {
                Subject = string.IsNullOrWhiteSpace(subject) ? "(geen onderwerp)" : subject.Trim(),
                Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
                Start = (DateTime)item.Start,
                End = (DateTime)item.End,
                AllDay = (bool)item.AllDayEvent,
                IsRecurring = (bool)item.IsRecurring,
                OpenKey = (string)item.EntryID,
            };
        }

        /// <summary>Vertaalt COM-fouten naar een korte Nederlandse melding.</summary>
        public static ProviderResult Describe(COMException ex)
        {
            switch ((uint)ex.ErrorCode)
            {
                case 0x80010001: // RPC_E_CALL_REJECTED
                case 0x8001010A: // RPC_E_SERVERCALL_RETRYLATER
                    return ProviderResult.NotAvailable("Outlook is bezig of wacht op een melding. Probeer het zo opnieuw.");
                case 0x80004004: // E_ABORT: beveiligingsmelding geweigerd
                case 0x80070005: // E_ACCESSDENIED
                    return ProviderResult.NotAvailable("Outlook weigert de toegang (beveiligingsmelding). De agenda wordt niet gelezen.");
                case 0x80080005: // CO_E_SERVER_EXEC_FAILURE
                    return ProviderResult.NotAvailable("Klassiek Outlook kon niet worden gestart.");
                case 0x800706BA: // RPC_S_SERVER_UNAVAILABLE
                case 0x800706BE: // RPC_S_CALL_FAILED
                case 0x80010108: // RPC_E_DISCONNECTED
                    return ProviderResult.NotAvailable("De verbinding met Outlook is verbroken. Probeer het opnieuw.");
                default:
                    return ProviderResult.Failed("De Outlook-agenda kon niet worden gelezen (fout 0x" +
                                                 ex.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) + ").");
            }
        }

        static bool IsOutlookProcessRunning()
        {
            Process[] processes = Process.GetProcessesByName("OUTLOOK");
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }

        void ReleaseApp()
        {
            if (_app == null) return;
            try { Marshal.FinalReleaseComObject(_app); }
            catch (Exception) { }
            _app = null;
            _startedByUs = false;
            // Tussenliggende COM-objecten van 'dynamic' opruimen, zodat Outlook echt losgelaten wordt.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        static void Release(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject)) return;
            try { Marshal.ReleaseComObject(comObject); }
            catch (Exception) { }
        }
    }
}
