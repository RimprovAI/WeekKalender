using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace WeekKalender.Providers
{
    /// <summary>
    /// De enige plek waar het programma Outlook aanroept. Elke aanroep gaat langs een vaste lijst van toegestane
    /// namen: eigenschappen om te lezen, een handvol methodes om te zoeken en te tonen, en één instelling op de
    /// zoeklijst. Alles wat iets zou kunnen opslaan, wijzigen, verwijderen of versturen staat er niet in en wordt
    /// geweigerd voordat Outlook het te zien krijgt.
    /// </summary>
    public static class OutlookCom
    {
        static readonly HashSet<string> Readable = new HashSet<string>(StringComparer.Ordinal)
        {
            "Version", "Items", "Class", "Subject", "Location", "Start", "End", "AllDayEvent", "IsRecurring", "EntryID",
        };

        static readonly HashSet<string> Callable = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetNamespace", "GetDefaultFolder", "Sort", "Restrict", "GetFirst", "GetNext",
            "GetItemFromID", "GetRecurrencePattern", "GetOccurrence", "Display",
        };

        /// <summary>Alleen op de zoeklijst (Items), niet op een afspraak: herhalingen uitklappen.</summary>
        static readonly HashSet<string> Settable = new HashSet<string>(StringComparer.Ordinal) { "IncludeRecurrences" };

        public static object Get(object target, string property)
        {
            Require(Readable, property);
            return Invoke(target, property, BindingFlags.GetProperty, null);
        }

        public static object Call(object target, string method, params object[] arguments)
        {
            Require(Callable, method);
            return Invoke(target, method, BindingFlags.InvokeMethod, arguments);
        }

        public static void Set(object target, string property, object value)
        {
            Require(Settable, property);
            Invoke(target, property, BindingFlags.SetProperty, new[] { value });
        }

        public static bool IsAllowed(string member)
        {
            return Readable.Contains(member) || Callable.Contains(member) || Settable.Contains(member);
        }

        public static void Release(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject)) return;
            try { Marshal.ReleaseComObject(comObject); }
            catch (Exception) { }
        }

        static void Require(HashSet<string> allowed, string member)
        {
            if (!allowed.Contains(member))
                throw new InvalidOperationException("Outlook-aanroep '" + member + "' is niet toegestaan: de koppeling is alleen-lezen.");
        }

        static object Invoke(object target, string member, BindingFlags kind, object[] arguments)
        {
            try
            {
                return target.GetType().InvokeMember(member, kind, null, target, arguments, CultureInfo.CurrentCulture);
            }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException == null) throw;
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
