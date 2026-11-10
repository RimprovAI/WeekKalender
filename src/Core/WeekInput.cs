using System.Globalization;
using System.Text.RegularExpressions;

namespace WeekKalender.Core
{
    public struct WeekInputResult
    {
        public bool Ok;
        public int Week;
        public int Year;
        public string Error;
    }

    /// <summary>
    /// Leest de invoer van "Ga naar week": "43", "w43", "week 43 2027", "43-2027", "43/2027", "2027-W43", "2027 43".
    /// Zonder jaartal geldt het jaar van de getoonde maand.
    /// </summary>
    public static class WeekInput
    {
        /// <summary>Bereik van de agenda: ook bladeren en dagen selecteren houden zich hieraan.</summary>
        public const int MinYear = 1900;
        public const int MaxYear = 2200;

        static readonly Regex WeekFirst = new Regex(@"^(?:(?:week|wk|w)\s*)?(\d{1,3})(?:\s*[-/,. ]\s*(\d{4}))?$", RegexOptions.CultureInvariant);
        static readonly Regex YearFirst = new Regex(@"^(\d{4})\s*[-/ ]?\s*(?:week|wk|w)?\s*(\d{1,3})$", RegexOptions.CultureInvariant);

        public static WeekInputResult Parse(string input, int defaultYear)
        {
            var result = new WeekInputResult();
            string text = (input ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0)
            {
                result.Error = "Vul een weeknummer in.";
                return result;
            }

            int week, year;
            Match m = WeekFirst.Match(text);
            if (m.Success)
            {
                week = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                year = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : defaultYear;
            }
            else
            {
                m = YearFirst.Match(text);
                if (!m.Success)
                {
                    result.Error = "Onbekende invoer. Typ bijvoorbeeld 43 of 43 2027.";
                    return result;
                }
                year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                week = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            if (year < MinYear || year > MaxYear)
            {
                result.Error = "Kies een jaar tussen " + MinYear + " en " + MaxYear + ".";
                return result;
            }

            int weeks = IsoWeek.WeeksInYear(year);
            if (week < 1 || week > weeks)
            {
                result.Error = "Week " + week + " bestaat niet in " + year + "; dat jaar heeft " + weeks + " weken.";
                return result;
            }

            result.Ok = true;
            result.Week = week;
            result.Year = year;
            return result;
        }
    }
}
