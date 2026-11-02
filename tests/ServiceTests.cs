using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using WeekKalender.Core;
using WeekKalender.Providers;

namespace WeekKalender.Tests
{
    internal sealed class FakeProvider : IAppointmentProvider
    {
        public Func<DateTime, DateTime, ProviderResult> OnFetch;
        public int Calls;

        public string Name
        {
            get { return "Nep"; }
        }

        public ProviderResult Fetch(DateTime start, DateTime end)
        {
            Interlocked.Increment(ref Calls);
            return OnFetch(start, end);
        }

        public void Dispose()
        {
        }
    }

    public static class ServiceTests
    {
        static readonly DateTime Start = new DateTime(2026, 9, 28);
        static readonly DateTime End = new DateTime(2026, 11, 9);

        static ProviderResult Ok(string subject = "Test")
        {
            return ProviderResult.Success(new[] { new Appointment { Subject = subject, Start = new DateTime(2026, 10, 21, 9, 0, 0), End = new DateTime(2026, 10, 21, 10, 0, 0) } }, "Nep");
        }

        static AppointmentService Service(IAppointmentProvider primary, IAppointmentProvider fallback = null, int timeoutMs = 5000, Func<DateTime> now = null)
        {
            return new AppointmentService(primary, fallback, null, TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMinutes(5), now ?? (() => DateTime.Now));
        }

        public static void BronGooitFout_GeeftNetteMelding()
        {
            var provider = new FakeProvider { OnFetch = (s, e) => { throw new InvalidOperationException("kapot"); } };
            using (var service = Service(provider))
            {
                ProviderResult r = service.GetAsync(Start, End).Result;
                Assert.Equal(ProviderStatus.Failed, r.Status, "status");
                Assert.Contains("kapot", r.Message);
            }
        }

        public static void BronHangt_TimeOutZonderBlokkeren()
        {
            var release = new ManualResetEventSlim(false);
            var provider = new FakeProvider { OnFetch = (s, e) => { release.Wait(10000); return Ok(); } };
            using (var service = Service(provider, timeoutMs: 300))
            {
                var sw = Stopwatch.StartNew();
                ProviderResult first = service.GetAsync(Start, End).Result;
                Assert.Equal(ProviderStatus.Timeout, first.Status, "eerste aanvraag");
                Assert.True(sw.ElapsedMilliseconds < 2000, "time-out duurde " + sw.ElapsedMilliseconds + " ms");

                Thread.Sleep(150);
                sw.Restart();
                ProviderResult second = service.GetAsync(Start.AddDays(35), End.AddDays(35)).Result;
                Assert.Equal(ProviderStatus.Timeout, second.Status, "tweede aanvraag terwijl de bron nog vastzit");
                Assert.True(sw.ElapsedMilliseconds < 200, "tweede aanvraag moet direct terugkomen, duurde " + sw.ElapsedMilliseconds + " ms");
                Assert.Equal(1, provider.Calls, "niet opnieuw in de wachtrij gezet");
                release.Set();
            }
        }

        public static void TerugvalNaarIcs()
        {
            var outlook = new FakeProvider { OnFetch = (s, e) => ProviderResult.NotAvailable("geen Outlook") };
            var ics = new FakeProvider { OnFetch = (s, e) => Ok("Uit ics") };
            using (var service = Service(outlook, ics))
            {
                ProviderResult r = service.GetAsync(Start, End).Result;
                Assert.True(r.IsOk, "terugval gelukt");
                Assert.Equal("Uit ics", r.Items[0].Subject, "afspraak uit terugval");
            }
        }

        public static void GeenBron_GeeftMeldingMaarWerktDoor()
        {
            using (var service = new AppointmentService(null, null, "Er is niets gekoppeld.", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), () => DateTime.Now))
            {
                ProviderResult r = service.GetAsync(Start, End).Result;
                Assert.Equal(ProviderStatus.NotAvailable, r.Status, "status");
                Assert.Equal("Er is niets gekoppeld.", r.Message, "melding");
            }
        }

        public static void CachePerMaand()
        {
            DateTime now = new DateTime(2026, 10, 21, 9, 0, 0);
            var provider = new FakeProvider { OnFetch = (s, e) => Ok() };
            using (var service = Service(provider, now: () => now))
            {
                service.GetAsync(Start, End).Wait();
                service.GetAsync(Start, End).Wait();
                Assert.Equal(1, provider.Calls, "tweede keer uit de cache");
                Assert.True(service.IsFresh(Start), "vers");

                service.GetAsync(Start.AddDays(35), End.AddDays(35)).Wait();
                Assert.Equal(2, provider.Calls, "andere maand wel ophalen");

                now = now.AddMinutes(6);
                ProviderResult stale;
                bool fresh;
                Assert.True(service.TryGetCached(Start, out stale, out fresh) && !fresh, "verlopen maar nog beschikbaar");
                service.GetAsync(Start, End).Wait();
                Assert.Equal(3, provider.Calls, "na 5 minuten opnieuw ophalen");
            }
        }

        public static void VoorlopigResultaatKortGecachet()
        {
            DateTime now = new DateTime(2026, 10, 21, 9, 0, 0);
            var provider = new FakeProvider { OnFetch = (s, e) => ProviderResult.Success(new Appointment[0], "Nep", provisional: true) };
            using (var service = Service(provider, now: () => now))
            {
                ProviderResult first = service.GetAsync(Start, End).Result;
                Assert.True(first.Provisional, "voorlopig doorgegeven");
                service.GetAsync(Start, End).Wait();
                Assert.Equal(1, provider.Calls, "binnen 10 s uit de cache");
                now = now.AddSeconds(11);
                service.GetAsync(Start, End).Wait();
                Assert.Equal(2, provider.Calls, "Outlook net gestart: na 10 s opnieuw ophalen");
            }
        }

        public static void FoutenKortGecachet()
        {
            DateTime now = new DateTime(2026, 10, 21, 9, 0, 0);
            var provider = new FakeProvider { OnFetch = (s, e) => ProviderResult.NotAvailable("even niet") };
            using (var service = Service(provider, now: () => now))
            {
                service.GetAsync(Start, End).Wait();
                service.GetAsync(Start, End).Wait();
                Assert.Equal(1, provider.Calls, "binnen 20 s niet opnieuw");
                now = now.AddSeconds(25);
                service.GetAsync(Start, End).Wait();
                Assert.Equal(2, provider.Calls, "na 20 s opnieuw proberen");
            }
        }
    }

    public static class OutlookTests
    {
        public static void NietGeinstalleerd_GeenCrash()
        {
            const string bogus = "WeekKalender.BestaatNiet.Application";
            Assert.False(OutlookComProvider.IsInstalled(bogus), "nep-ProgID bestaat niet");
            using (var provider = new OutlookComProvider(true, bogus))
            {
                ProviderResult r = provider.Fetch(new DateTime(2026, 9, 28), new DateTime(2026, 11, 9));
                Assert.Equal(ProviderStatus.NotAvailable, r.Status, "status");
                Assert.Contains("niet geïnstalleerd", r.Message);
            }
        }

        public static void NietOpenEnNietStarten_GeenCrash()
        {
            if (!OutlookComProvider.IsInstalled() || Process.GetProcessesByName("OUTLOOK").Length > 0)
            {
                Console.WriteLine("        (overgeslagen: Outlook draait al of is niet geïnstalleerd)");
                return;
            }
            using (var provider = new OutlookComProvider(false))
            {
                ProviderResult r = provider.Fetch(new DateTime(2026, 9, 28), new DateTime(2026, 11, 9));
                Assert.Equal(ProviderStatus.NotAvailable, r.Status, "status");
                Assert.Contains("niet geopend", r.Message);
                Assert.Equal(0, Process.GetProcessesByName("OUTLOOK").Length, "Outlook niet gestart");
            }
        }

        public static void ComFoutenVertaald()
        {
            ProviderResult busy = OutlookComProvider.Describe(new COMException("x", unchecked((int)0x80010001)));
            Assert.Equal(ProviderStatus.NotAvailable, busy.Status, "bezig");
            Assert.Contains("bezig", busy.Message);
            ProviderResult denied = OutlookComProvider.Describe(new COMException("x", unchecked((int)0x80004004)));
            Assert.Contains("beveiligingsmelding", denied.Message);
            ProviderResult start = OutlookComProvider.Describe(new COMException("x", unchecked((int)0x80080005)));
            Assert.Contains("niet worden gestart", start.Message);
            ProviderResult other = OutlookComProvider.Describe(new COMException("x", unchecked((int)0x80004005)));
            Assert.Equal(ProviderStatus.Failed, other.Status, "onbekend");
            Assert.Contains("80004005", other.Message);
        }

        public static void IcsBestand_OntbreektOfKapot_GeenCrash()
        {
            string folder = Path.Combine(Path.GetTempPath(), "WeekKalenderTests");
            Directory.CreateDirectory(folder);
            string good = Path.Combine(folder, "goed.ics");
            string bad = Path.Combine(folder, "kapot.ics");
            File.WriteAllText(good, "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:1\r\nSUMMARY:Bestand\r\nDTSTART:20261021T080000Z\r\nDTEND:20261021T090000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            File.WriteAllText(bad, "<html>geen agenda</html>");
            try
            {
                var from = new DateTime(2026, 9, 28);
                var to = new DateTime(2026, 11, 9);
                ProviderResult ok = new IcsProvider(good, TimeSpan.FromMinutes(5)).Fetch(from, to);
                Assert.True(ok.IsOk, "goed bestand: " + ok.Message);
                Assert.Equal("Bestand", ok.Items.Single().Subject, "onderwerp");

                ProviderResult missing = new IcsProvider(Path.Combine(folder, "bestaat-niet.ics"), TimeSpan.FromMinutes(5)).Fetch(from, to);
                Assert.Equal(ProviderStatus.NotAvailable, missing.Status, "ontbrekend bestand");
                Assert.Contains("niet gevonden", missing.Message);

                ProviderResult broken = new IcsProvider(bad, TimeSpan.FromMinutes(5)).Fetch(from, to);
                Assert.Equal(ProviderStatus.Failed, broken.Status, "kapot bestand");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }

    public static class SettingsTests
    {
        public static void StandaardEnEigenWaarden()
        {
            string folder = Path.Combine(Path.GetTempPath(), "WeekKalenderInstellingen");
            string file = Path.Combine(folder, "instellingen.ini");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            try
            {
                AppSettings defaults = AppSettings.Load(file);
                Assert.True(File.Exists(file), "bestand aangemaakt met uitleg");
                Assert.Equal("auto", defaults.Source, "bron");
                Assert.True(defaults.StartOutlook, "Outlook starten");
                Assert.Equal(5, defaults.CacheMinutes, "cache");

                File.WriteAllText(file, "; test\r\nBron=ics\r\nIcsBron=\"C:\\agenda.ics\"\r\nOutlookStarten=nee\r\nCacheMinuten=999\r\n");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
                Assert.True(defaults.ReloadIfChanged(), "wijziging gezien");
                Assert.Equal("ics", defaults.Source, "bron");
                Assert.Equal(@"C:\agenda.ics", defaults.IcsSource, "pad zonder aanhalingstekens");
                Assert.False(defaults.StartOutlook, "Outlook niet starten");
                Assert.Equal(120, defaults.CacheMinutes, "begrensd");
                Assert.False(defaults.ReloadIfChanged(), "geen nieuwe wijziging");

                File.WriteAllText(file, "Bron=onzin\r\n");
                Assert.Equal("auto", AppSettings.Load(file).Source, "onbekende bron wordt auto");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }

    public static class AutostartTests
    {
        const string TestRoot = @"Software\WeekKalenderTests";

        static readonly byte[] DisabledFlag = { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        static Autostart Make(string exe, string folder)
        {
            return new Autostart(exe, "WeekKalender", folder, TestRoot + @"\Run", TestRoot + @"\ApprovedRun", TestRoot + @"\ApprovedFolder");
        }

        static string RunValue()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(TestRoot + @"\Run"))
                return run == null ? null : run.GetValue("WeekKalender") as string;
        }

        static void WithSandbox(Action<string, string> test)
        {
            string folder = Path.Combine(Path.GetTempPath(), "WeekKalenderOpstarten");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            // Een bestaand bestand als 'programma', zodat de snelkoppeling een echt doel heeft.
            string exe = typeof(AutostartTests).Assembly.Location;
            try
            {
                test(exe, folder);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestRoot, false);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        public static void AanUitEnTaakbeheer()
        {
            WithSandbox((exe, folder) =>
            {
                var autostart = Make(exe, folder);
                Assert.False(autostart.IsEnabled(), "begint uit");
                autostart.Enable();
                Assert.True(File.Exists(Path.Combine(folder, "WeekKalender.lnk")), "snelkoppeling in map Opstarten");
                Assert.True(autostart.IsEnabled(), "aan");

                // Uitgezet via Taakbeheer.
                using (var approved = Registry.CurrentUser.CreateSubKey(TestRoot + @"\ApprovedFolder"))
                    approved.SetValue("WeekKalender.lnk", DisabledFlag, RegistryValueKind.Binary);
                Assert.False(autostart.IsEnabled(), "uitgeschakeld in Taakbeheer");
                autostart.Enable();
                Assert.True(autostart.IsEnabled(), "weer aan via het menu");

                var moved = Make(Path.Combine(Environment.SystemDirectory, "notepad.exe"), folder);
                Assert.False(moved.IsEnabled(), "snelkoppeling naar ander programma telt niet als aan");

                autostart.Disable();
                Assert.False(autostart.IsEnabled(), "uit");
                Assert.False(File.Exists(Path.Combine(folder, "WeekKalender.lnk")), "snelkoppeling verwijderd");
            });
        }

        public static void OudeRunWaardeWordtOmgezet()
        {
            WithSandbox((exe, folder) =>
            {
                using (var run = Registry.CurrentUser.CreateSubKey(TestRoot + @"\Run"))
                    run.SetValue("WeekKalender", "\"" + exe + "\"");
                var autostart = Make(exe, folder);
                Assert.True(autostart.MigrateFromRunKey(), "omgezet");
                Assert.True(autostart.IsEnabled(), "nu aan via map Opstarten");
                Assert.Equal(null, RunValue(), "Run-waarde opgeruimd");
                Assert.False(autostart.MigrateFromRunKey(), "tweede keer niets te doen");
            });
        }

        public static void UitgeschakeldeRunWaardeBlijftUit()
        {
            WithSandbox((exe, folder) =>
            {
                using (var run = Registry.CurrentUser.CreateSubKey(TestRoot + @"\Run"))
                    run.SetValue("WeekKalender", "\"" + exe + "\"");
                using (var approved = Registry.CurrentUser.CreateSubKey(TestRoot + @"\ApprovedRun"))
                    approved.SetValue("WeekKalender", DisabledFlag, RegistryValueKind.Binary);
                var autostart = Make(exe, folder);
                Assert.True(autostart.MigrateFromRunKey(), "opgeruimd");
                Assert.False(autostart.IsEnabled(), "niet stiekem aangezet");
                Assert.Equal(null, RunValue(), "Run-waarde opgeruimd");
            });
        }
    }

    /// <summary>Handmatige controle tegen de echte Outlook-agenda. Toont alleen aantallen en tijden, geen onderwerpen.</summary>
    internal static class OutlookProbe
    {
        public static int Run()
        {
            Console.WriteLine("Klassiek Outlook geïnstalleerd: " + (OutlookComProvider.IsInstalled() ? "ja" : "nee"));
            bool wasRunning = Process.GetProcessesByName("OUTLOOK").Length > 0;
            Console.WriteLine("Outlook draaide al: " + (wasRunning ? "ja" : "nee"));

            MonthGrid grid = MonthGrid.For(DateTime.Today);
            using (var provider = new OutlookComProvider(true, idleRelease: TimeSpan.Zero))
            {
                var sw = Stopwatch.StartNew();
                ProviderResult r = provider.Fetch(grid.FirstCell, grid.LastCell.AddDays(1));
                Console.WriteLine("Deze maand: " + r.Status + " in " + sw.ElapsedMilliseconds + " ms" + (r.Message != null ? " - " + r.Message : ""));
                if (r.IsOk)
                {
                    Console.WriteLine("  afspraken in raster: " + r.Items.Count + ", terugkerend: " + r.Items.Count(a => a.IsRecurring) +
                                      ", hele dag: " + r.Items.Count(a => a.AllDay) + ", met EntryID: " + r.Items.Count(a => a.OpenKey != null));
                    Console.WriteLine("  dagen met afspraken: " + AppointmentDay.DaysWithItems(r.Items, grid.FirstCell, grid.LastCell).Count);
                    Console.WriteLine("  vandaag: " + AppointmentDay.For(r.Items, DateTime.Today).Count + " afspraak/afspraken");
                }
                MonthGrid next = grid.AddMonths(1);
                sw.Restart();
                ProviderResult r2 = provider.Fetch(next.FirstCell, next.LastCell.AddDays(1));
                Console.WriteLine("Volgende maand: " + r2.Status + " in " + sw.ElapsedMilliseconds + " ms, " + r2.Items.Count + " afspraken");
                provider.ReleaseIfIdle();
            }

            if (!wasRunning)
            {
                var sw = Stopwatch.StartNew();
                while (Process.GetProcessesByName("OUTLOOK").Length > 0 && sw.ElapsedMilliseconds < 30000) Thread.Sleep(500);
                bool stillRunning = Process.GetProcessesByName("OUTLOOK").Length > 0;
                Console.WriteLine("Outlook na loslaten " + (stillRunning ? "nog actief na 30 s" : "afgesloten na " + sw.ElapsedMilliseconds + " ms"));
            }
            return 0;
        }
    }
}
