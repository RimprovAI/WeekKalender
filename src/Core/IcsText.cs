using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WeekKalender.Core
{
    /// <summary>Eén regel uit een iCalendar-bestand: NAAM;PARAM=waarde:WAARDE.</summary>
    internal struct ContentLine
    {
        public string Name;
        public Dictionary<string, string> Params;
        public string Value;

        public static bool TryParse(string line, out ContentLine result)
        {
            result = default(ContentLine);
            bool quoted = false;
            int colon = -1;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') quoted = !quoted;
                else if (c == ':' && !quoted)
                {
                    colon = i;
                    break;
                }
            }
            if (colon <= 0) return false;

            List<string> head = SplitOutsideQuotes(line.Substring(0, colon), ';');
            result.Name = head[0].Trim().ToUpperInvariant();
            result.Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < head.Count; i++)
            {
                int eq = head[i].IndexOf('=');
                if (eq > 0) result.Params[head[i].Substring(0, eq).Trim()] = head[i].Substring(eq + 1).Trim().Trim('"');
            }
            result.Value = line.Substring(colon + 1);
            return true;
        }

        static List<string> SplitOutsideQuotes(string text, char separator)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            foreach (char c in text)
            {
                if (c == '"') quoted = !quoted;
                if (c == separator && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }
    }

    internal static class IcsText
    {
        static readonly Regex DurationPattern = new Regex(
            @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Plakt vervolgregels (beginnend met spatie of tab) aan de vorige regel.</summary>
        public static List<string> Unfold(string text)
        {
            var lines = new List<string>();
            StringBuilder current = null;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (current != null && line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                    {
                        current.Append(line, 1, line.Length - 1);
                        continue;
                    }
                    if (current != null) lines.Add(current.ToString());
                    current = new StringBuilder(line);
                }
            }
            if (current != null) lines.Add(current.ToString());
            return lines;
        }

        public static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\' && i + 1 < value.Length)
                {
                    char next = value[++i];
                    sb.Append(next == 'n' || next == 'N' ? '\n' : next);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static IcsTime ParseTime(string value, IDictionary<string, string> parameters, IDictionary<string, TimeZoneInfo> zones)
        {
            string v = value.Trim();
            string valueType;
            parameters.TryGetValue("VALUE", out valueType);
            var time = new IcsTime();

            if (string.Equals(valueType, "DATE", StringComparison.OrdinalIgnoreCase) || (v.Length == 8 && v.IndexOf('T') < 0))
            {
                time.Value = DateTime.ParseExact(v.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture);
                time.IsDate = true;
                return time;
            }

            if (v.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            {
                time.IsUtc = true;
                v = v.Substring(0, v.Length - 1);
            }
            time.Value = DateTime.ParseExact(v, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None);

            string tzid;
            if (!time.IsUtc && parameters.TryGetValue("TZID", out tzid)) time.Zone = IcsTimeZones.Resolve(tzid, zones);
            return time;
        }

        public static TimeSpan ParseDuration(string value)
        {
            Match m = DurationPattern.Match(value.Trim());
            if (!m.Success) throw new FormatException("Ongeldige DURATION: " + value);
            var span = TimeSpan.FromDays(7 * Int(m.Groups[2]) + Int(m.Groups[3]))
                       + new TimeSpan(Int(m.Groups[4]), Int(m.Groups[5]), Int(m.Groups[6]));
            return m.Groups[1].Value == "-" ? span.Negate() : span;
        }

        /// <summary>"+0100", "-0500" of "+013000".</summary>
        public static TimeSpan ParseOffset(string value)
        {
            string v = value.Trim();
            if (v.Length < 5) throw new FormatException("Ongeldige UTC-afwijking: " + value);
            int sign = v[0] == '-' ? -1 : 1;
            int hours = int.Parse(v.Substring(1, 2), CultureInfo.InvariantCulture);
            int minutes = int.Parse(v.Substring(3, 2), CultureInfo.InvariantCulture);
            int seconds = v.Length >= 7 ? int.Parse(v.Substring(5, 2), CultureInfo.InvariantCulture) : 0;
            return TimeSpan.FromSeconds(sign * (hours * 3600 + minutes * 60 + seconds));
        }

        static int Int(Group g)
        {
            return g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
