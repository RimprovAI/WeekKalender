using System;
using System.Collections.Generic;
using System.Linq;

namespace WeekKalender.Core
{
    /// <summary>Tijdstip uit een .ics-bestand: datum (hele dag), UTC, tijd in een tijdzone of 'zwevende' lokale tijd.</summary>
    public struct IcsTime
    {
        public DateTime Value;
        public bool IsDate;
        public bool IsUtc;
        public TimeZoneInfo Zone;

        /// <summary>Wandkloktijd in <paramref name="target"/>.</summary>
        public DateTime ToZone(TimeZoneInfo target)
        {
            if (IsDate) return Value.Date;
            if (IsUtc) return Unspecified(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Value, DateTimeKind.Utc), target));
            if (Zone == null) return Value;
            return ConvertWall(Value, Zone, target);
        }

        /// <summary>Omgekeerde van ToZone: een wandkloktijd in <paramref name="target"/> uitgedrukt in de tijd van dit tijdstip.</summary>
        internal DateTime FromZone(DateTime wallInTarget, TimeZoneInfo target)
        {
            if (IsDate || (!IsUtc && Zone == null)) return wallInTarget;
            if (IsUtc) return Unspecified(TimeZoneInfo.ConvertTimeToUtc(Valid(wallInTarget, target), target));
            return ConvertWall(wallInTarget, target, Zone);
        }

        internal static DateTime ConvertWall(DateTime wall, TimeZoneInfo from, TimeZoneInfo to)
        {
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(Valid(wall, from), from);
            return Unspecified(TimeZoneInfo.ConvertTimeFromUtc(utc, to));
        }

        static DateTime Valid(DateTime wall, TimeZoneInfo zone)
        {
            DateTime w = Unspecified(wall);
            // Een tijd in het 'gat' bij de overgang naar zomertijd bestaat niet; schuif een uur op.
            return zone.IsInvalidTime(w) ? w.AddHours(1) : w;
        }

        static DateTime Unspecified(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        }
    }

    public sealed class IcsEvent
    {
        public readonly List<IcsTime> ExDates = new List<IcsTime>();
        public readonly List<IcsTime> RDates = new List<IcsTime>();
        public string Uid;
        public string Summary;
        public string Location;
        public IcsTime Start;
        public IcsTime? End;
        public TimeSpan? Duration;
        public string RRule;
        public IcsTime? RecurrenceId;
        public bool Cancelled;
        internal bool HasStart;
        internal bool AllDayHint;

        public bool AllDay
        {
            get { return Start.IsDate; }
        }
    }

    /// <summary>Leest een iCalendar-bestand (bijvoorbeeld een gepubliceerde Outlook-agenda) en zet afspraken uit over een periode.</summary>
    public sealed class IcsCalendar
    {
        const int MaxEvents = 50000;
        const int MaxResults = 20000;

        readonly List<IcsEvent> _events;

        IcsCalendar(List<IcsEvent> events)
        {
            _events = events;
        }

        public IReadOnlyList<IcsEvent> Events
        {
            get { return _events; }
        }

        public static IcsCalendar Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            List<string> lines = IcsText.Unfold(text);
            if (!lines.Any(l => l.Trim().Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("Dit is geen iCalendar-bestand (BEGIN:VCALENDAR ontbreekt).");

            Dictionary<string, TimeZoneInfo> zones = IcsTimeZones.ParseDefinitions(lines);
            var events = new List<IcsEvent>();
            IcsEvent current = null;
            int depth = 0;

            foreach (string raw in lines)
            {
                ContentLine cl;
                if (!ContentLine.TryParse(raw, out cl)) continue;
                if (cl.Name == "BEGIN")
                {
                    if (current != null) depth++;
                    else if (cl.Value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new IcsEvent();
                        depth = 0;
                    }
                    continue;
                }
                if (cl.Name == "END")
                {
                    if (current == null) continue;
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }
                    if (cl.Value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                    {
                        Finish(current);
                        if (current.HasStart && events.Count < MaxEvents) events.Add(current);
                        current = null;
                    }
                    continue;
                }
                if (current == null || depth > 0) continue;   // VALARM e.d. overslaan

                try
                {
                    Apply(current, cl, zones);
                }
                catch (FormatException) { }   // onleesbare regel overslaan, de rest gewoon tonen
                catch (ArgumentException) { }
            }
            return new IcsCalendar(events);
        }

        static void Apply(IcsEvent e, ContentLine cl, Dictionary<string, TimeZoneInfo> zones)
        {
            switch (cl.Name)
            {
                case "UID":
                    e.Uid = cl.Value.Trim();
                    break;
                case "SUMMARY":
                    e.Summary = IcsText.Unescape(cl.Value);
                    break;
                case "LOCATION":
                    e.Location = IcsText.Unescape(cl.Value);
                    break;
                case "DTSTART":
                    e.Start = IcsText.ParseTime(cl.Value, cl.Params, zones);
                    e.HasStart = true;
                    break;
                case "DTEND":
                    e.End = IcsText.ParseTime(cl.Value, cl.Params, zones);
                    break;
                case "DURATION":
                    e.Duration = IcsText.ParseDuration(cl.Value);
                    break;
                case "RRULE":
                    e.RRule = cl.Value.Trim();
                    break;
                case "EXDATE":
                    foreach (string part in cl.Value.Split(','))
                        if (part.Trim().Length > 0) e.ExDates.Add(IcsText.ParseTime(part, cl.Params, zones));
                    break;
                case "RDATE":
                    string valueType;
                    if (cl.Params.TryGetValue("VALUE", out valueType) && valueType.Equals("PERIOD", StringComparison.OrdinalIgnoreCase)) break;
                    foreach (string part in cl.Value.Split(','))
                        if (part.Trim().Length > 0) e.RDates.Add(IcsText.ParseTime(part, cl.Params, zones));
                    break;
                case "RECURRENCE-ID":
                    e.RecurrenceId = IcsText.ParseTime(cl.Value, cl.Params, zones);
                    break;
                case "STATUS":
                    e.Cancelled = cl.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);
                    break;
                case "X-MICROSOFT-CDO-ALLDAYEVENT":
                    e.AllDayHint = cl.Value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        static void Finish(IcsEvent e)
        {
            // Outlook markeert hele-dag-afspraken soms met een tijd van 00:00 plus deze vlag.
            if (e.AllDayHint && !e.Start.IsDate && e.Start.Value.TimeOfDay == TimeSpan.Zero)
            {
                e.Start = new IcsTime { Value = e.Start.Value.Date, IsDate = true };
                if (e.End.HasValue) e.End = new IcsTime { Value = e.End.Value.Value.Date, IsDate = true };
            }
        }

        /// <summary>Alle afspraken die (deels) in [rangeStart, rangeEnd) vallen, in de tijd van <paramref name="target"/>.</summary>
        public List<Appointment> Expand(DateTime rangeStart, DateTime rangeEnd, TimeZoneInfo target)
        {
            if (target == null) target = TimeZoneInfo.Local;
            var result = new List<Appointment>();

            // Losse uitzonderingen (verplaatst of geannuleerd voorkomen) vervangen het originele voorkomen.
            var replaced = new HashSet<string>();
            foreach (IcsEvent e in _events)
                if (e.RecurrenceId.HasValue && e.Uid != null)
                    replaced.Add(OccurrenceKey(e.Uid, e.RecurrenceId.Value.ToZone(target), e.RecurrenceId.Value.IsDate));

            foreach (IcsEvent e in _events)
            {
                TimeSpan duration = Duration(e, target);
                if (e.RecurrenceId.HasValue)
                {
                    if (!e.Cancelled) AddIfInRange(result, e, e.Start.ToZone(target), duration, false, rangeStart, rangeEnd);
                    continue;
                }
                if (e.Cancelled) continue;

                RecurrenceRule rule = e.RRule != null ? RecurrenceRule.Parse(e.RRule) : null;
                IEnumerable<DateTime> starts;
                if (rule == null)
                {
                    starts = new[] { e.Start.Value };
                }
                else
                {
                    DateTime limit = e.Start.FromZone(rangeEnd, target).AddDays(2);
                    DateTime? until = rule.Until.HasValue ? UntilInEventTime(rule.Until.Value, e.Start, target) : (DateTime?)null;
                    DateTime from = e.Start.FromZone(rangeStart, target) - duration - TimeSpan.FromDays(2);
                    starts = RecurrenceExpander.Occurrences(e.Start.Value, rule, until, from, limit);
                }
                starts = starts.Concat(e.RDates.Select(r => e.Start.FromZone(r.ToZone(target), target)));

                var excluded = new HashSet<DateTime>(e.ExDates.Select(x => Normalize(x.ToZone(target), e.AllDay)));
                var seen = new HashSet<DateTime>();
                foreach (DateTime wall in starts)
                {
                    if (result.Count >= MaxResults) return result;   // begrenzing tegen een (kwaadwillig) enorm bestand
                    IcsTime occurrence = e.Start;
                    occurrence.Value = wall;
                    DateTime local = occurrence.ToZone(target);
                    DateTime key = Normalize(local, e.AllDay);
                    if (!seen.Add(key) || excluded.Contains(key)) continue;
                    if (e.Uid != null && replaced.Contains(OccurrenceKey(e.Uid, local, e.AllDay))) continue;
                    AddIfInRange(result, e, local, duration, rule != null, rangeStart, rangeEnd);
                }
            }
            return result;
        }

        static void AddIfInRange(List<Appointment> list, IcsEvent e, DateTime start, TimeSpan duration, bool recurring, DateTime rangeStart, DateTime rangeEnd)
        {
            var appointment = new Appointment
            {
                Subject = string.IsNullOrWhiteSpace(e.Summary) ? "(geen onderwerp)" : e.Summary.Trim(),
                Location = string.IsNullOrWhiteSpace(e.Location) ? null : e.Location.Trim(),
                Start = start,
                End = start + duration,
                AllDay = e.AllDay,
                IsRecurring = recurring,
            };
            if (AppointmentDay.Overlaps(appointment, rangeStart, rangeEnd)) list.Add(appointment);
        }

        static TimeSpan Duration(IcsEvent e, TimeZoneInfo target)
        {
            if (e.AllDay)
            {
                if (e.End.HasValue) return TimeSpan.FromDays(Math.Max(1, (e.End.Value.Value.Date - e.Start.Value.Date).Days));
                if (e.Duration.HasValue) return TimeSpan.FromDays(Math.Max(1, Math.Round(e.Duration.Value.TotalDays)));
                return TimeSpan.FromDays(1);
            }
            if (e.End.HasValue)
            {
                TimeSpan span = e.End.Value.ToZone(target) - e.Start.ToZone(target);
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
            if (e.Duration.HasValue && e.Duration.Value > TimeSpan.Zero) return e.Duration.Value;
            return TimeSpan.Zero;
        }

        static DateTime UntilInEventTime(IcsTime until, IcsTime start, TimeZoneInfo target)
        {
            if (until.IsDate || (start.IsDate && until.IsUtc)) return until.Value.Date.AddDays(1).AddTicks(-1);
            if (until.IsUtc)
            {
                if (start.IsUtc) return until.Value;
                TimeZoneInfo zone = start.Zone ?? target;
                return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(until.Value, DateTimeKind.Utc), zone), DateTimeKind.Unspecified);
            }
            return until.Value;
        }

        static DateTime Normalize(DateTime value, bool allDay)
        {
            return allDay ? value.Date : new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute);
        }

        static string OccurrenceKey(string uid, DateTime local, bool allDay)
        {
            return uid + "|" + Normalize(local, allDay).Ticks;
        }
    }
}
