using System;
using System.Collections.Generic;
using System.Linq;

namespace WeekKalender.Core
{
    public sealed class Appointment
    {
        public string Subject { get; set; }
        public string Location { get; set; }
        public DateTime Start { get; set; }

        /// <summary>Exclusief: een hele-dag-afspraak op 21 oktober eindigt op 22 oktober 00:00.</summary>
        public DateTime End { get; set; }

        public bool AllDay { get; set; }
        public bool IsRecurring { get; set; }

        /// <summary>Sleutel waarmee de bron de afspraak kan openen (Outlook EntryID); null als openen niet kan.</summary>
        public string OpenKey { get; set; }
    }

    public static class AppointmentDay
    {
        /// <summary>Afspraken die (deels) op de gegeven dag vallen: hele-dag-afspraken eerst, daarna op begintijd.</summary>
        public static List<Appointment> For(IEnumerable<Appointment> items, DateTime day)
        {
            DateTime dayStart = day.Date;
            DateTime dayEnd = dayStart.AddDays(1);
            return items
                .Where(a => Overlaps(a, dayStart, dayEnd))
                .OrderBy(a => a.AllDay ? 0 : 1)
                .ThenBy(a => a.Start)
                .ThenBy(a => a.End)
                .ThenBy(a => a.Subject, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static bool Overlaps(Appointment a, DateTime from, DateTime to)
        {
            if (a.Start >= to) return false;
            if (a.End > from) return true;
            // Afspraak zonder duur telt op het moment dat hij begint.
            return a.End <= a.Start && a.Start >= from;
        }

        /// <summary>Aantal afspraken per dag tussen from en to (inclusief); dagen zonder afspraken ontbreken.</summary>
        public static Dictionary<DateTime, int> CountsPerDay(IEnumerable<Appointment> items, DateTime from, DateTime to)
        {
            var counts = new Dictionary<DateTime, int>();
            DateTime first = from.Date, last = to.Date;
            foreach (var a in items)
            {
                DateTime startDay = a.Start.Date;
                DateTime endDay = a.End > a.Start ? a.End.AddTicks(-1).Date : startDay;
                if (startDay < first) startDay = first;
                if (endDay > last) endDay = last;
                for (DateTime d = startDay; d <= endDay; d = d.AddDays(1))
                {
                    int n;
                    counts.TryGetValue(d, out n);
                    counts[d] = n + 1;
                }
            }
            return counts;
        }

        /// <summary>Alle dagen tussen from en to (inclusief) waarop minstens één afspraak valt.</summary>
        public static HashSet<DateTime> DaysWithItems(IEnumerable<Appointment> items, DateTime from, DateTime to)
        {
            var days = new HashSet<DateTime>();
            DateTime first = from.Date, last = to.Date;
            foreach (var a in items)
            {
                DateTime startDay = a.Start.Date;
                DateTime endDay = a.End > a.Start ? a.End.AddTicks(-1).Date : startDay;
                if (startDay < first) startDay = first;
                if (endDay > last) endDay = last;
                for (DateTime d = startDay; d <= endDay; d = d.AddDays(1)) days.Add(d);
            }
            return days;
        }
    }

    public enum ProviderStatus
    {
        Ok,
        NotAvailable,
        Failed,
        Timeout
    }

    public sealed class ProviderResult
    {
        static readonly IReadOnlyList<Appointment> NoItems = new Appointment[0];

        ProviderResult(ProviderStatus status, IReadOnlyList<Appointment> items, string message, string source)
        {
            Status = status;
            Items = items ?? NoItems;
            Message = message;
            Source = source;
        }

        public ProviderStatus Status { get; private set; }
        public IReadOnlyList<Appointment> Items { get; private set; }

        /// <summary>Korte Nederlandse uitleg voor de gebruiker als Status niet Ok is.</summary>
        public string Message { get; private set; }

        public string Source { get; private set; }

        /// <summary>
        /// Voorlopig: de bron is net gestart en kan nog aan het bijwerken zijn (klassiek Outlook synchroniseert na het starten).
        /// Wordt kort gecachet en daarna opnieuw opgehaald.
        /// </summary>
        public bool Provisional { get; private set; }

        public bool IsOk
        {
            get { return Status == ProviderStatus.Ok; }
        }

        public static ProviderResult Success(IReadOnlyList<Appointment> items, string source, bool provisional = false)
        {
            return new ProviderResult(ProviderStatus.Ok, items, null, source) { Provisional = provisional };
        }

        public static ProviderResult NotAvailable(string message)
        {
            return new ProviderResult(ProviderStatus.NotAvailable, null, message, null);
        }

        public static ProviderResult Failed(string message)
        {
            return new ProviderResult(ProviderStatus.Failed, null, message, null);
        }

        public static ProviderResult TimedOut(string message)
        {
            return new ProviderResult(ProviderStatus.Timeout, null, message, null);
        }
    }
}
