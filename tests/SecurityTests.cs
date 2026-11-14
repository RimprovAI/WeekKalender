using System;
using System.IO;
using System.Linq;
using WeekKalender.Core;
using WeekKalender.Providers;

namespace WeekKalender.Tests
{
    public static class SecurityTests
    {
        public static void OutlookAlleenLezen_SchrijvendeAanroepenGeweigerd()
        {
            var dummy = new object();
            foreach (string member in new[] { "Save", "SaveAs", "Delete", "Move", "Send", "Copy", "Close", "Respond", "ClearRecurrencePattern", "Forward", "Quit" })
            {
                Assert.False(OutlookCom.IsAllowed(member), member + " mag niet op de lijst staan");
                Assert.Throws<InvalidOperationException>(() => OutlookCom.Call(dummy, member));
            }
            foreach (string property in new[] { "Subject", "Body", "Start", "End", "Location", "Categories", "BusyStatus" })
                Assert.Throws<InvalidOperationException>(() => OutlookCom.Set(dummy, property, "x"));
            Assert.Throws<InvalidOperationException>(() => OutlookCom.Get(dummy, "Body"));
        }

        public static void AgendaLink_AlleenHttps()
        {
            var from = new DateTime(2026, 9, 28);
            var to = new DateTime(2026, 11, 9);
            foreach (string url in new[] { "http://voorbeeld.invalid/agenda.ics", "ftp://voorbeeld.invalid/agenda.ics", "file://server/share/agenda.ics" })
            {
                ProviderResult r = new IcsProvider(url, TimeSpan.FromMinutes(5)).Fetch(from, to);
                Assert.Equal(ProviderStatus.NotAvailable, r.Status, url);
                Assert.Contains("beveiligde", r.Message);
            }
        }

        public static void AgendaBestand_TeGrootGeweigerd()
        {
            string file = Path.Combine(Path.GetTempPath(), "WeekKalenderGroot.ics");
            File.WriteAllText(file, "BEGIN:VCALENDAR\r\n" + new string('X', 5000) + "\r\nEND:VCALENDAR\r\n");
            try
            {
                ProviderResult r = new IcsProvider(file, TimeSpan.FromMinutes(5), maxBytes: 1000).Fetch(new DateTime(2026, 9, 28), new DateTime(2026, 11, 9));
                Assert.Equal(ProviderStatus.Failed, r.Status, "status");
                Assert.Contains("te groot", r.Message);
            }
            finally
            {
                File.Delete(file);
            }
        }

        public static void OudeReeksen_SnelEnGelijkAanVolledigDoorlopen()
        {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            string[] rules =
            {
                "FREQ=DAILY", "FREQ=DAILY;INTERVAL=3", "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,TH", "FREQ=WEEKLY;INTERVAL=3;WKST=SU;BYDAY=SU,WE",
                "FREQ=MONTHLY;INTERVAL=2;BYDAY=-1FR", "FREQ=MONTHLY;BYMONTHDAY=31", "FREQ=YEARLY;INTERVAL=2", "FREQ=YEARLY;BYMONTH=11;BYDAY=2TU",
            };
            foreach (string rule in rules)
            {
                // Met COUNT wordt altijd vanaf het begin geteld; zonder COUNT worden oude perioden overgeslagen. Het resultaat moet gelijk zijn.
                string events(string r) => "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:a\r\nSUMMARY:x\r\nDTSTART;TZID=W. Europe Standard Time:19990104T090000\r\nDURATION:PT1H\r\nRRULE:" + r + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
                var from = new DateTime(2026, 10, 26);
                var to = new DateTime(2026, 12, 7);
                var skipped = IcsCalendar.Parse(events(rule)).Expand(from, to, zone).Select(a => a.Start).OrderBy(d => d).ToList();
                var counted = IcsCalendar.Parse(events(rule + ";COUNT=99999")).Expand(from, to, zone).Select(a => a.Start).OrderBy(d => d).ToList();
                Assert.Equal(string.Join(",", counted.Select(d => d.ToString("MMdd HHmm"))), string.Join(",", skipped.Select(d => d.ToString("MMdd HHmm"))), rule);
            }
            var daily = IcsCalendar.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:a\r\nSUMMARY:x\r\nDTSTART:19700101T080000Z\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) daily.Expand(new DateTime(2026, 10, 26), new DateTime(2026, 12, 7), zone);
            Assert.True(sw.ElapsedMilliseconds < 1500, "200 maanden van een reeks sinds 1970 duurde " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
