using System;
using System.Collections.Generic;
using System.Linq;
using WeekKalender.Core;

namespace WeekKalender.Tests
{
    public static class IcsTests
    {
        static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        static List<Appointment> Expand(string events, DateTime from, DateTime to, string extra = "")
        {
            string text = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//NL\r\n" + extra + events + "END:VCALENDAR\r\n";
            return IcsCalendar.Parse(text).Expand(from, to, Amsterdam).OrderBy(a => a.Start).ToList();
        }

        static string Ev(params string[] lines)
        {
            return "BEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\n";
        }

        static string Times(IEnumerable<Appointment> items)
        {
            return string.Join(",", items.Select(a => a.Start.ToString("MM-dd HH:mm")));
        }

        static readonly DateTime Oct = new DateTime(2026, 10, 1);
        static readonly DateTime Nov = new DateTime(2026, 11, 1);

        public static void EnkeleAfspraakMetTijdzone()
        {
            var items = Expand(Ev("UID:a", "SUMMARY:Overleg\\, team", "LOCATION:Kamer 1\\; zaal",
                                  "DTSTART;TZID=W. Europe Standard Time:20261021T090000",
                                  "DTEND;TZID=W. Europe Standard Time:20261021T100000"), Oct, Nov);
            Assert.Equal(1, items.Count, "aantal");
            Assert.Equal("Overleg, team", items[0].Subject, "onderwerp");
            Assert.Equal("Kamer 1; zaal", items[0].Location, "locatie");
            Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), items[0].Start, "begin");
            Assert.Equal(new DateTime(2026, 10, 21, 10, 0, 0), items[0].End, "einde");
            Assert.Equal(null, items[0].OpenKey, "niet te openen");
        }

        public static void UtcEnAndereTijdzones()
        {
            var items = Expand(
                Ev("UID:u", "SUMMARY:UTC", "DTSTART:20261021T080000Z", "DTEND:20261021T090000Z") +
                Ev("UID:i", "SUMMARY:IANA", "DTSTART;TZID=Europe/Amsterdam:20261021T110000", "DURATION:PT30M") +
                Ev("UID:p", "SUMMARY:Seattle", "DTSTART;TZID=Pacific Standard Time:20261021T090000", "DTEND;TZID=Pacific Standard Time:20261021T093000") +
                Ev("UID:w", "SUMMARY:Winter", "DTSTART:20261110T080000Z", "DTEND:20261110T090000Z"),
                Oct, new DateTime(2026, 12, 1));
            Assert.Equal("10-21 10:00,10-21 11:00,10-21 18:00,11-10 09:00", Times(items), "omgerekend naar Nederlandse tijd");
            Assert.Equal(new DateTime(2026, 10, 21, 11, 30, 0), items[1].End, "DURATION");
        }

        public static void EigenVTimezone()
        {
            string zone =
                "BEGIN:VTIMEZONE\r\nTZID:Mijn zone\r\n" +
                "BEGIN:STANDARD\r\nDTSTART:16011028T030000\r\nRRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=10\r\nTZOFFSETFROM:+0200\r\nTZOFFSETTO:+0100\r\nEND:STANDARD\r\n" +
                "BEGIN:DAYLIGHT\r\nDTSTART:16010325T020000\r\nRRULE:FREQ=YEARLY;BYDAY=-1SU;BYMONTH=3\r\nTZOFFSETFROM:+0100\r\nTZOFFSETTO:+0200\r\nEND:DAYLIGHT\r\n" +
                "END:VTIMEZONE\r\n";
            var items = Expand(
                Ev("UID:z1", "SUMMARY:Zomer", "DTSTART;TZID=Mijn zone:20261021T110000", "DTEND;TZID=Mijn zone:20261021T120000") +
                Ev("UID:z2", "SUMMARY:Winter", "DTSTART;TZID=Mijn zone:20261102T110000", "DTEND;TZID=Mijn zone:20261102T120000"),
                Oct, new DateTime(2026, 12, 1), zone);
            Assert.Equal("10-21 11:00,11-02 11:00", Times(items), "eigen tijdzonedefinitie met zomertijd");
        }

        public static void HeleDag()
        {
            var items = Expand(Ev("UID:d", "SUMMARY:Congres", "DTSTART;VALUE=DATE:20261021", "DTEND;VALUE=DATE:20261023"), Oct, Nov);
            Assert.Equal(1, items.Count, "aantal");
            Assert.True(items[0].AllDay, "hele dag");
            Assert.Equal(1, AppointmentDay.For(items, new DateTime(2026, 10, 21)).Count, "op 21");
            Assert.Equal(1, AppointmentDay.For(items, new DateTime(2026, 10, 22)).Count, "op 22");
            Assert.Equal(0, AppointmentDay.For(items, new DateTime(2026, 10, 23)).Count, "niet op 23");

            var outlookStyle = Expand(Ev("UID:o", "SUMMARY:Vrij", "DTSTART;TZID=W. Europe Standard Time:20261030T000000",
                                         "DTEND;TZID=W. Europe Standard Time:20261031T000000", "X-MICROSOFT-CDO-ALLDAYEVENT:TRUE"), Oct, Nov);
            Assert.True(outlookStyle[0].AllDay, "Outlook-markering voor hele dag");
        }

        public static void WekelijksMetAantal()
        {
            var items = Expand(Ev("UID:w", "SUMMARY:Sport", "DTSTART;TZID=W. Europe Standard Time:20261005T093000",
                                  "DTEND;TZID=W. Europe Standard Time:20261005T103000", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4"), Oct, Nov);
            Assert.Equal("10-05 09:30,10-07 09:30,10-12 09:30,10-14 09:30", Times(items), "maandag en woensdag, 4 keer");
            Assert.True(items.All(a => a.IsRecurring), "terugkerend");
        }

        public static void TweeWekelijks()
        {
            var items = Expand(Ev("UID:b", "SUMMARY:Sprint", "DTSTART;TZID=W. Europe Standard Time:20261001T140000",
                                  "DURATION:PT1H", "RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=TH"), Oct, new DateTime(2026, 11, 13));
            Assert.Equal("10-01 14:00,10-15 14:00,10-29 14:00,11-12 14:00", Times(items), "om de week, ook na de zomertijd");
        }

        public static void MaandelijksLaatsteVrijdag()
        {
            var items = Expand(Ev("UID:m", "SUMMARY:Borrel", "DTSTART;TZID=W. Europe Standard Time:20261030T160000",
                                  "DTEND;TZID=W. Europe Standard Time:20261030T180000", "RRULE:FREQ=MONTHLY;BYDAY=-1FR"),
                               Oct, new DateTime(2027, 1, 1));
            Assert.Equal("10-30 16:00,11-27 16:00,12-25 16:00", Times(items), "laatste vrijdag");
        }

        public static void MaandelijksLaatsteWerkdag()
        {
            var items = Expand(Ev("UID:l", "SUMMARY:Uren", "DTSTART;TZID=W. Europe Standard Time:20261030T120000",
                                  "DURATION:PT15M", "RRULE:FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=3"),
                               Oct, new DateTime(2027, 3, 1));
            Assert.Equal("10-30 12:00,11-30 12:00,12-31 12:00", Times(items), "laatste werkdag");
        }

        public static void MaandelijksOpDe31e()
        {
            var items = Expand(Ev("UID:31", "SUMMARY:Einde maand", "DTSTART;VALUE=DATE:20261031", "RRULE:FREQ=MONTHLY"),
                               Oct, new DateTime(2027, 2, 1));
            Assert.Equal("10-31 00:00,12-31 00:00,01-31 00:00", Times(items), "maanden zonder 31e overslaan");
        }

        public static void JaarlijksOp29Februari()
        {
            var items = Expand(Ev("UID:f", "SUMMARY:Schrikkeldag", "DTSTART;VALUE=DATE:20240229", "RRULE:FREQ=YEARLY"),
                               new DateTime(2025, 1, 1), new DateTime(2029, 1, 1));
            Assert.Equal(1, items.Count, "alleen in schrikkeljaren");
            Assert.Equal(new DateTime(2028, 2, 29), items[0].Start, "2028");
        }

        public static void UitzonderingenEnTotEnMet()
        {
            var items = Expand(Ev("UID:x", "SUMMARY:Stand-up", "DTSTART;TZID=W. Europe Standard Time:20261005T090000",
                                  "DTEND;TZID=W. Europe Standard Time:20261005T091500",
                                  "RRULE:FREQ=WEEKLY;UNTIL=20261102T080000Z",
                                  "EXDATE;TZID=W. Europe Standard Time:20261019T090000"),
                               Oct, new DateTime(2026, 12, 1));
            Assert.Equal("10-05 09:00,10-12 09:00,10-26 09:00,11-02 09:00", Times(items), "19 okt overgeslagen, 2 nov nog mee (UNTIL inclusief)");
        }

        public static void VerplaatstEnGeannuleerdVoorkomen()
        {
            var items = Expand(
                Ev("UID:r", "SUMMARY:Overleg", "DTSTART;TZID=W. Europe Standard Time:20261012T140000",
                   "DTEND;TZID=W. Europe Standard Time:20261012T150000", "RRULE:FREQ=DAILY;COUNT=4") +
                Ev("UID:r", "SUMMARY:Overleg (verplaatst)", "RECURRENCE-ID;TZID=W. Europe Standard Time:20261013T140000",
                   "DTSTART;TZID=W. Europe Standard Time:20261013T160000", "DTEND;TZID=W. Europe Standard Time:20261013T170000") +
                Ev("UID:r", "SUMMARY:Overleg", "RECURRENCE-ID;TZID=W. Europe Standard Time:20261014T140000", "STATUS:CANCELLED",
                   "DTSTART;TZID=W. Europe Standard Time:20261014T140000", "DTEND;TZID=W. Europe Standard Time:20261014T150000") +
                Ev("UID:c", "SUMMARY:Afgezegd", "STATUS:CANCELLED", "DTSTART:20261020T080000Z"),
                Oct, Nov);
            Assert.Equal("10-12 14:00,10-13 16:00,10-15 14:00", Times(items), "verplaatst en geannuleerd");
            Assert.Equal("Overleg (verplaatst)", items[1].Subject, "onderwerp van de uitzondering");
        }

        public static void VouwenEnAlarmen()
        {
            string ev = "BEGIN:VEVENT\r\nUID:v\r\nSUMMARY:Lange\r\n  titel\r\nDTSTART:20261021T080000Z\r\n" +
                        "BEGIN:VALARM\r\nACTION:DISPLAY\r\nSUMMARY:Niet dit\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nEND:VEVENT\r\n";
            var items = Expand(ev, Oct, Nov);
            Assert.Equal(1, items.Count, "aantal");
            Assert.Equal("Lange titel", items[0].Subject, "gevouwen regel en alarm genegeerd");
        }

        public static void OngeldigBestand()
        {
            Assert.Throws<FormatException>(() => IcsCalendar.Parse("Dit is geen agenda"));
            var items = Expand(Ev("UID:k", "SUMMARY:Kapotte datum", "DTSTART:2026-10-21") + Ev("UID:g", "SUMMARY:Goed", "DTSTART:20261021T080000Z"), Oct, Nov);
            Assert.Equal("Goed", string.Join(",", items.Select(a => a.Subject)), "kapotte afspraak overgeslagen, rest blijft");
        }
    }
}
