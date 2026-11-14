using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace WeekKalender.Core
{
    internal struct WeekdayNum
    {
        public int Ordinal;   // 0 = elke, 2 = tweede, -1 = laatste
        public DayOfWeek Day;
    }

    /// <summary>
    /// RRULE (RFC 5545) voor de patronen die Outlook en andere agenda's gebruiken:
    /// FREQ=DAILY/WEEKLY/MONTHLY/YEARLY met INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY, BYMONTH, BYSETPOS en WKST.
    /// </summary>
    internal sealed class RecurrenceRule
    {
        static readonly Regex ByDayPattern = new Regex(@"^([+-]?\d{1,2})?(MO|TU|WE|TH|FR|SA|SU)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public string Freq;
        public int Interval = 1;
        public int? Count;
        public IcsTime? Until;
        public List<WeekdayNum> ByDay;
        public List<int> ByMonthDay;
        public List<int> ByMonth;
        public List<int> BySetPos;
        public DayOfWeek WeekStart = DayOfWeek.Monday;

        public static RecurrenceRule Parse(string text)
        {
            var rule = new RecurrenceRule();
            foreach (string part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq).Trim().ToUpperInvariant();
                string value = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "FREQ":
                        rule.Freq = value.ToUpperInvariant();
                        break;
                    case "INTERVAL":
                        int interval;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) && interval > 0) rule.Interval = interval;
                        break;
                    case "COUNT":
                        int count;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0) rule.Count = count;
                        break;
                    case "UNTIL":
                        try { rule.Until = IcsText.ParseTime(value, new Dictionary<string, string>(), null); }
                        catch (FormatException) { }
                        break;
                    case "BYDAY":
                        rule.ByDay = value.Split(',').Select(ParseByDay).Where(d => d.HasValue).Select(d => d.Value).ToList();
                        if (rule.ByDay.Count == 0) rule.ByDay = null;
                        break;
                    case "BYMONTHDAY":
                        rule.ByMonthDay = Ints(value);
                        break;
                    case "BYMONTH":
                        rule.ByMonth = Ints(value);
                        break;
                    case "BYSETPOS":
                        rule.BySetPos = Ints(value);
                        break;
                    case "WKST":
                        var wkst = ParseByDay(value);
                        if (wkst.HasValue) rule.WeekStart = wkst.Value.Day;
                        break;
                }
            }
            return rule.Freq == null ? null : rule;
        }

        static WeekdayNum? ParseByDay(string text)
        {
            Match m = ByDayPattern.Match(text.Trim());
            if (!m.Success) return null;
            int ordinal = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
            DayOfWeek day;
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "MO": day = DayOfWeek.Monday; break;
                case "TU": day = DayOfWeek.Tuesday; break;
                case "WE": day = DayOfWeek.Wednesday; break;
                case "TH": day = DayOfWeek.Thursday; break;
                case "FR": day = DayOfWeek.Friday; break;
                case "SA": day = DayOfWeek.Saturday; break;
                default: day = DayOfWeek.Sunday; break;
            }
            return new WeekdayNum { Ordinal = ordinal, Day = day };
        }

        static List<int> Ints(string value)
        {
            var list = new List<int>();
            foreach (string part in value.Split(','))
            {
                int n;
                if (int.TryParse(part.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n) && n != 0) list.Add(n);
            }
            return list.Count == 0 ? null : list;
        }
    }

    internal static class RecurrenceExpander
    {
        const int MaxPeriods = 100000;

        /// <summary>
        /// Begintijden (wandklok, zelfde soort tijd als dtStart) van alle voorkomens, oplopend, tot en met <paramref name="limit"/>.
        /// COUNT telt vanaf het allereerste voorkomen, ook als dat ver voor het gevraagde bereik ligt.
        /// </summary>
        public static IEnumerable<DateTime> Occurrences(DateTime dtStart, RecurrenceRule rule, DateTime? until, DateTime limit)
        {
            return Occurrences(dtStart, rule, until, dtStart, limit);
        }

        /// <summary>
        /// Als hierboven, maar slaat perioden over die ruim voor <paramref name="from"/> liggen. Een reeks die jaren geleden
        /// begon, wordt zo niet bij elke maand opnieuw vanaf het begin doorlopen. Bij COUNT kan dat niet: dan moet er geteld worden.
        /// </summary>
        public static IEnumerable<DateTime> Occurrences(DateTime dtStart, RecurrenceRule rule, DateTime? until, DateTime from, DateTime limit)
        {
            int emitted = 0;
            TimeSpan time = dtStart.TimeOfDay;
            int first = rule.Count.HasValue ? 0 : FirstPeriod(dtStart, rule, from);
            for (int p = first; p < first + MaxPeriods; p++)
            {
                DateTime periodStart = DateTime.MaxValue;
                List<DateTime> days = null;
                bool supported = true, overflow = false;
                try
                {
                    switch (rule.Freq)
                    {
                        case "DAILY":
                            periodStart = dtStart.Date.AddDays((double)p * rule.Interval);
                            days = Daily(periodStart, rule);
                            break;
                        case "WEEKLY":
                            periodStart = StartOfWeek(dtStart.Date, rule.WeekStart).AddDays(7.0 * p * rule.Interval);
                            days = Weekly(periodStart, dtStart, rule);
                            break;
                        case "MONTHLY":
                            periodStart = new DateTime(dtStart.Year, dtStart.Month, 1).AddMonths(p * rule.Interval);
                            days = Monthly(periodStart, dtStart, rule);
                            break;
                        case "YEARLY":
                            periodStart = new DateTime(dtStart.Year, 1, 1).AddYears(p * rule.Interval);
                            days = Yearly(periodStart.Year, dtStart, rule);
                            break;
                        default:
                            supported = false;
                            break;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    overflow = true;
                }

                if (!supported)
                {
                    // Onbekende frequentie (HOURLY e.d.): alleen het eerste voorkomen tonen.
                    yield return dtStart;
                    yield break;
                }
                if (overflow || periodStart > limit) yield break;
                if (until.HasValue && periodStart > until.Value) yield break;

                foreach (DateTime day in days)
                {
                    DateTime candidate = day + time;
                    if (candidate < dtStart) continue;
                    if (until.HasValue && candidate > until.Value) yield break;
                    if (rule.Count.HasValue && emitted >= rule.Count.Value) yield break;
                    if (candidate > limit) yield break;
                    emitted++;
                    yield return candidate;
                }
            }
        }

        /// <summary>Nummer van de periode die één periode voor 'from' ligt (nooit negatief).</summary>
        static int FirstPeriod(DateTime dtStart, RecurrenceRule rule, DateTime from)
        {
            if (from <= dtStart) return 0;
            long periods;
            switch (rule.Freq)
            {
                case "DAILY":
                    periods = (long)(from.Date - dtStart.Date).TotalDays / rule.Interval;
                    break;
                case "WEEKLY":
                    periods = (long)(from.Date - StartOfWeek(dtStart.Date, rule.WeekStart)).TotalDays / (7L * rule.Interval);
                    break;
                case "MONTHLY":
                    periods = ((from.Year - dtStart.Year) * 12L + from.Month - dtStart.Month) / rule.Interval;
                    break;
                case "YEARLY":
                    periods = (from.Year - dtStart.Year) / rule.Interval;
                    break;
                default:
                    return 0;
            }
            return (int)Math.Max(0, Math.Min(int.MaxValue / 2, periods - 1));
        }

        static DateTime StartOfWeek(DateTime date, DayOfWeek weekStart)
        {
            return date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));
        }

        static List<DateTime> Daily(DateTime day, RecurrenceRule rule)
        {
            bool ok = (rule.ByMonth == null || rule.ByMonth.Contains(day.Month))
                      && (rule.ByMonthDay == null || rule.ByMonthDay.Any(md => MonthDayMatches(day, md)))
                      && (rule.ByDay == null || rule.ByDay.Any(b => b.Day == day.DayOfWeek));
            return ok ? new List<DateTime> { day } : new List<DateTime>();
        }

        static bool MonthDayMatches(DateTime day, int monthDay)
        {
            int dim = DateTime.DaysInMonth(day.Year, day.Month);
            return monthDay > 0 ? day.Day == monthDay : day.Day == dim + monthDay + 1;
        }

        static List<DateTime> Weekly(DateTime weekStart, DateTime dtStart, RecurrenceRule rule)
        {
            var list = new List<DateTime>(7);
            for (int i = 0; i < 7; i++)
            {
                DateTime day = weekStart.AddDays(i);
                bool dayOk = rule.ByDay != null ? rule.ByDay.Any(b => b.Day == day.DayOfWeek) : day.DayOfWeek == dtStart.DayOfWeek;
                if (dayOk && (rule.ByMonth == null || rule.ByMonth.Contains(day.Month))) list.Add(day);
            }
            return ApplySetPos(list, rule.BySetPos);
        }

        static List<DateTime> Monthly(DateTime monthStart, DateTime dtStart, RecurrenceRule rule)
        {
            if (rule.ByMonth != null && !rule.ByMonth.Contains(monthStart.Month)) return new List<DateTime>();
            return ApplySetPos(DaysInMonth(monthStart.Year, monthStart.Month, dtStart, rule), rule.BySetPos);
        }

        static List<DateTime> Yearly(int year, DateTime dtStart, RecurrenceRule rule)
        {
            var result = new List<DateTime>();
            if (rule.ByMonth == null && rule.ByDay != null && rule.ByMonthDay == null)
            {
                // Bijvoorbeeld BYDAY=20MO: de twintigste maandag van het jaar.
                var first = new DateTime(year, 1, 1);
                var allDays = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Select(i => first.AddDays(i)).ToList();
                foreach (var b in rule.ByDay) result.AddRange(NthWeekday(allDays, b));
            }
            else
            {
                var months = rule.ByMonth ?? new List<int> { dtStart.Month };
                foreach (int month in months)
                    if (month >= 1 && month <= 12) result.AddRange(DaysInMonth(year, month, dtStart, rule));
            }
            return ApplySetPos(result.Distinct().OrderBy(d => d).ToList(), rule.BySetPos);
        }

        static List<DateTime> DaysInMonth(int year, int month, DateTime dtStart, RecurrenceRule rule)
        {
            int dim = DateTime.DaysInMonth(year, month);
            var result = new List<DateTime>();
            if (rule.ByMonthDay != null)
            {
                foreach (int md in rule.ByMonthDay)
                {
                    int day = md > 0 ? md : dim + md + 1;
                    if (day >= 1 && day <= dim) result.Add(new DateTime(year, month, day));
                }
                if (rule.ByDay != null) result = result.Where(d => rule.ByDay.Any(b => b.Day == d.DayOfWeek)).ToList();
            }
            else if (rule.ByDay != null)
            {
                var allDays = Enumerable.Range(1, dim).Select(d => new DateTime(year, month, d)).ToList();
                foreach (var b in rule.ByDay) result.AddRange(NthWeekday(allDays, b));
            }
            else if (dtStart.Day <= dim)
            {
                // Bijvoorbeeld de 31e: maanden zonder 31 dagen worden overgeslagen (RFC 5545).
                result.Add(new DateTime(year, month, dtStart.Day));
            }
            return result.Distinct().OrderBy(d => d).ToList();
        }

        static IEnumerable<DateTime> NthWeekday(List<DateTime> days, WeekdayNum b)
        {
            var matching = days.Where(d => d.DayOfWeek == b.Day).ToList();
            if (b.Ordinal == 0) return matching;
            int index = b.Ordinal > 0 ? b.Ordinal - 1 : matching.Count + b.Ordinal;
            return index >= 0 && index < matching.Count ? new[] { matching[index] } : new DateTime[0];
        }

        static List<DateTime> ApplySetPos(List<DateTime> days, List<int> setPos)
        {
            if (setPos == null || days.Count == 0) return days;
            var result = new List<DateTime>();
            foreach (int pos in setPos)
            {
                int index = pos > 0 ? pos - 1 : days.Count + pos;
                if (index >= 0 && index < days.Count) result.Add(days[index]);
            }
            return result.Distinct().OrderBy(d => d).ToList();
        }
    }
}
