using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

[assembly: TargetFramework(".NETFramework,Version=v4.8.1", FrameworkDisplayName = ".NET Framework 4.8.1")]

namespace WeekKalender.Tests
{
    /// <summary>
    /// Minimale testrunner (geen NuGet nodig): voert alle publieke statische void-methodes uit in klassen die op "Tests" eindigen.
    /// Gebruik: WeekKalender.Tests.exe            alle tests
    ///          WeekKalender.Tests.exe --outlook  leest de echte Outlook-agenda (alleen aantallen, geen inhoud)
    /// </summary>
    internal static class TestRunner
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--outlook")) return OutlookProbe.Run();

            List<MethodInfo> tests = typeof(TestRunner).Assembly.GetTypes()
                .Where(t => t.IsClass && t.Name.EndsWith("Tests", StringComparison.Ordinal))
                .OrderBy(t => t.Name)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                    .OrderBy(m => m.MetadataToken))
                .ToList();

            int failed = 0;
            var total = Stopwatch.StartNew();
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                try
                {
                    test.Invoke(null, null);
                    Console.WriteLine("  ok    " + name);
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Console.WriteLine("  FOUT  " + name);
                    Console.WriteLine("        " + (ex.InnerException ?? ex).Message);
                }
            }
            Console.WriteLine();
            Console.WriteLine((tests.Count - failed) + " van " + tests.Count + " tests geslaagd (" + total.ElapsedMilliseconds + " ms).");
            return failed == 0 ? 0 : 1;
        }
    }

    internal static class Assert
    {
        public static void Equal<T>(T expected, T actual, string what = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception((what != null ? what + ": " : "") + "verwacht <" + expected + ">, kreeg <" + actual + ">");
        }

        public static void True(bool condition, string what)
        {
            if (!condition) throw new Exception(what);
        }

        public static void False(bool condition, string what)
        {
            if (condition) throw new Exception(what);
        }

        public static void Contains(string expected, string actual)
        {
            if (actual == null || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("verwacht dat <" + actual + "> '" + expected + "' bevat");
        }

        public static TException Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            throw new Exception("verwacht " + typeof(TException).Name);
        }
    }
}
