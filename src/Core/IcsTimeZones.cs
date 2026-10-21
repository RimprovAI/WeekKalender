using System;
using System.Collections.Generic;
using System.Linq;

namespace WeekKalender.Core
{
    /// <summary>
    /// Zoekt de tijdzone bij een TZID. Volgorde: Windows-naam (zoals Outlook die gebruikt), IANA-naam (Europe/Amsterdam),
    /// de VTIMEZONE-definitie uit het bestand zelf, en als laatste de weergavenaam van Windows.
    /// </summary>
    internal static class IcsTimeZones
    {
        static readonly Dictionary<string, string> IanaToWindows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Europe/Amsterdam", "W. Europe Standard Time" },
            { "Europe/Berlin", "W. Europe Standard Time" },
            { "Europe/Rome", "W. Europe Standard Time" },
            { "Europe/Vienna", "W. Europe Standard Time" },
            { "Europe/Zurich", "W. Europe Standard Time" },
            { "Europe/Stockholm", "W. Europe Standard Time" },
            { "Europe/Oslo", "W. Europe Standard Time" },
            { "Europe/Luxembourg", "W. Europe Standard Time" },
            { "Europe/Brussels", "Romance Standard Time" },
            { "Europe/Paris", "Romance Standard Time" },
            { "Europe/Madrid", "Romance Standard Time" },
            { "Europe/Copenhagen", "Romance Standard Time" },
            { "Europe/London", "GMT Standard Time" },
            { "Europe/Dublin", "GMT Standard Time" },
            { "Europe/Lisbon", "GMT Standard Time" },
            { "Europe/Helsinki", "FLE Standard Time" },
            { "Europe/Athens", "GTB Standard Time" },
            { "Europe/Warsaw", "Central European Standard Time" },
            { "Europe/Prague", "Central Europe Standard Time" },
            { "Europe/Budapest", "Central Europe Standard Time" },
            { "America/New_York", "Eastern Standard Time" },
            { "America/Chicago", "Central Standard Time" },
            { "America/Denver", "Mountain Standard Time" },
            { "America/Los_Angeles", "Pacific Standard Time" },
            { "Asia/Tokyo", "Tokyo Standard Time" },
            { "Asia/Shanghai", "China Standard Time" },
            { "Asia/Kolkata", "India Standard Time" },
            { "Asia/Singapore", "Singapore Standard Time" },
            { "Asia/Dubai", "Arabian Standard Time" },
            { "Australia/Sydney", "AUS Eastern Standard Time" },
            { "UTC", "UTC" },
            { "Etc/UTC", "UTC" },
            { "Etc/GMT", "UTC" },
        };

        public static TimeZoneInfo Resolve(string tzid, IDictionary<string, TimeZoneInfo> defined)
        {
            string id = (tzid ?? "").Trim().Trim('"');
            if (id.Length == 0) return null;

            TimeZoneInfo zone;
            if (TryFind(id, out zone)) return zone;

            // Ook vormen als "/freeassociation.sourceforge.net/Tzfile/Europe/Amsterdam".
            string[] parts = id.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string iana = parts.Length >= 2 ? parts[parts.Length - 2] + "/" + parts[parts.Length - 1] : id;
            string windowsId;
            if ((IanaToWindows.TryGetValue(iana, out windowsId) || IanaToWindows.TryGetValue(id, out windowsId)) && TryFind(windowsId, out zone))
                return zone;

            if (defined != null && defined.TryGetValue(id, out zone)) return zone;

            foreach (var candidate in TimeZoneInfo.GetSystemTimeZones())
            {
                if (string.Equals(candidate.DisplayName, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.StandardName, id, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        static bool TryFind(string id, out TimeZoneInfo zone)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
            catch (System.Security.SecurityException) { }
            zone = null;
            return false;
        }

        sealed class Observance
        {
            public bool Daylight;
            public DateTime Start;
            public TimeSpan OffsetTo;
            public string RRule;
        }

        /// <summary>Zet VTIMEZONE-blokken om naar TimeZoneInfo-objecten (jaarlijkse regels als "laatste zondag van maart").</summary>
        public static Dictionary<string, TimeZoneInfo> ParseDefinitions(List<string> lines)
        {
            var result = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
            bool inZone = false;
            string tzid = null;
            List<Observance> observances = null;
            Observance current = null;

            foreach (string raw in lines)
            {
                ContentLine cl;
                if (!ContentLine.TryParse(raw, out cl)) continue;
                string value = cl.Value.Trim();

                if (cl.Name == "BEGIN" && value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase))
                {
                    inZone = true;
                    tzid = null;
                    observances = new List<Observance>();
                    continue;
                }
                if (!inZone) continue;

                if (cl.Name == "END" && value.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase))
                {
                    inZone = false;
                    if (tzid != null)
                    {
                        try
                        {
                            TimeZoneInfo zone = Build(tzid, observances);
                            if (zone != null) result[tzid] = zone;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Tijdzone '" + tzid + "' in .ics niet bruikbaar: " + ex.Message);
                        }
                    }
                    continue;
                }

                if (cl.Name == "BEGIN" && (value.Equals("STANDARD", StringComparison.OrdinalIgnoreCase) || value.Equals("DAYLIGHT", StringComparison.OrdinalIgnoreCase)))
                {
                    current = new Observance { Daylight = value.Equals("DAYLIGHT", StringComparison.OrdinalIgnoreCase) };
                    continue;
                }
                if (cl.Name == "END" && current != null)
                {
                    observances.Add(current);
                    current = null;
                    continue;
                }

                try
                {
                    if (cl.Name == "TZID" && current == null) tzid = value;
                    else if (current != null && cl.Name == "DTSTART") current.Start = IcsText.ParseTime(value, cl.Params, null).Value;
                    else if (current != null && cl.Name == "TZOFFSETTO") current.OffsetTo = IcsText.ParseOffset(value);
                    else if (current != null && cl.Name == "RRULE") current.RRule = value;
                }
                catch (FormatException) { }
            }
            return result;
        }

        static TimeZoneInfo Build(string id, List<Observance> observances)
        {
            Observance standard = observances.Where(o => !o.Daylight).OrderByDescending(o => o.Start).FirstOrDefault();
            Observance daylight = observances.Where(o => o.Daylight).OrderByDescending(o => o.Start).FirstOrDefault();
            if (standard == null) standard = daylight;
            if (standard == null) return null;

            string name = "ICS " + id;
            TimeZoneInfo.TransitionTime start, end;
            if (daylight == null || daylight == standard || !TryTransition(daylight, out start) || !TryTransition(standard, out end))
                return TimeZoneInfo.CreateCustomTimeZone(name, standard.OffsetTo, name, name);

            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, daylight.OffsetTo - standard.OffsetTo, start, end);
            return TimeZoneInfo.CreateCustomTimeZone(name, standard.OffsetTo, name, name, name + " zomertijd", new[] { rule });
        }

        static bool TryTransition(Observance o, out TimeZoneInfo.TransitionTime transition)
        {
            transition = default(TimeZoneInfo.TransitionTime);
            if (string.IsNullOrEmpty(o.RRule)) return false;
            var rule = RecurrenceRule.Parse(o.RRule);
            if (rule == null || rule.Freq != "YEARLY" || rule.ByMonth == null || rule.ByMonth.Count != 1 ||
                rule.ByDay == null || rule.ByDay.Count != 1)
                return false;

            int ordinal = rule.ByDay[0].Ordinal;
            int week = ordinal < 0 ? 5 : ordinal;
            if (week < 1 || week > 5) return false;
            var timeOfDay = new DateTime(1, 1, 1, o.Start.Hour, o.Start.Minute, o.Start.Second);
            transition = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(timeOfDay, rule.ByMonth[0], week, rule.ByDay[0].Day);
            return true;
        }
    }
}
