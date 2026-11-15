using System;
using System.Globalization;

namespace WeekKalender.Core
{
    /// <summary>Nederlandse namen en notaties, los van de taal van Windows.</summary>
    public static class DutchText
    {
        static readonly string[] Days = { "maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag", "zondag" };
        static readonly string[] DaysShort = { "ma", "di", "wo", "do", "vr", "za", "zo" };
        static readonly string[] Months =
        {
            "januari", "februari", "maart", "april", "mei", "juni",
            "juli", "augustus", "september", "oktober", "november", "december"
        };
        static readonly string[] MonthsShort = { "jan", "feb", "mrt", "apr", "mei", "jun", "jul", "aug", "sep", "okt", "nov", "dec" };

        public static string DayName(DateTime date)
        {
            return Days[IsoWeek.DayIndex(date)];
        }

        /// <summary>0 = ma … 6 = zo.</summary>
        public static string DayShort(int index)
        {
            return DaysShort[index];
        }

        public static string MonthName(int month)
        {
            return Months[month - 1];
        }

        /// <summary>"jan" … "dec".</summary>
        public static string MonthShort(int month)
        {
            return MonthsShort[month - 1];
        }

        /// <summary>"Over 25 min" of "Over 1 u 10 min" voor een afspraak die binnen drie uur begint; anders null.</summary>
        public static string StartsIn(DateTime start, DateTime now)
        {
            TimeSpan wait = start - now;
            if (wait <= TimeSpan.Zero || wait > TimeSpan.FromHours(3)) return null;
            int minutes = (int)Math.Ceiling(wait.TotalMinutes);
            if (minutes < 60) return "Over " + minutes.ToString(CultureInfo.InvariantCulture) + " min";
            int rest = minutes % 60;
            return "Over " + (minutes / 60).ToString(CultureInfo.InvariantCulture) + " u" + (rest == 0 ? "" : " " + rest.ToString(CultureInfo.InvariantCulture) + " min");
        }

        public static string MonthYear(int year, int month)
        {
            return Months[month - 1] + " " + year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"woensdag 21 oktober 2026"</summary>
        public static string LongDate(DateTime date)
        {
            return DayName(date) + " " + date.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[date.Month - 1] + " " +
                   date.Year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"woensdag 21 oktober 2026 – week 43"</summary>
        public static string Tooltip(DateTime date)
        {
            return LongDate(date) + " – week " + IsoWeek.GetWeek(date).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"woensdag 21 oktober", met jaartal als het niet het huidige jaar is.</summary>
        public static string DayTitle(DateTime date, DateTime today)
        {
            string text = DayName(date) + " " + date.Day.ToString(CultureInfo.InvariantCulture) + " " + Months[date.Month - 1];
            return date.Year == today.Year ? text : text + " " + date.Year.ToString(CultureInfo.InvariantCulture);
        }

        public static string Capitalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public static string Time(DateTime time)
        {
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        static string ShortDateTime(DateTime time)
        {
            return time.Day.ToString(CultureInfo.InvariantCulture) + " " + MonthsShort[time.Month - 1] + " " + Time(time);
        }

        /// <summary>Tijdsaanduiding van een afspraak zoals getoond op de gegeven dag.</summary>
        public static string TimeRange(Appointment appointment, DateTime day)
        {
            if (appointment.AllDay) return "Hele dag";
            DateTime dayStart = day.Date;
            bool startsToday = appointment.Start.Date == dayStart;
            bool endsToday = appointment.End <= dayStart.AddDays(1);
            if (startsToday && endsToday)
            {
                return appointment.End <= appointment.Start
                    ? Time(appointment.Start)
                    : Time(appointment.Start) + "–" + Time(appointment.End);
            }
            return (startsToday ? Time(appointment.Start) : ShortDateTime(appointment.Start)) + " – " +
                   (endsToday ? Time(appointment.End) : ShortDateTime(appointment.End));
        }
    }
}
