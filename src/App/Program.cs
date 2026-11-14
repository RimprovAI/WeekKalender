using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WeekKalender.Core;

namespace WeekKalender.App
{
    internal static class Program
    {
        const string InstanceId = "WeekKalender-5C1E8B7A-2F4D-4B7E-9A51-3D2C7F0E6B19";

        [STAThread]
        static int Main(string[] args)
        {
            NativeMethods.HardenDllSearch();
            bool exitRequested = args.Any(a => a.Equals("--afsluiten", StringComparison.OrdinalIgnoreCase));

            // Eerst de signalen aanmaken (of openen), dan pas de mutex: zo kan een tweede start ze altijd vinden.
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceId + "-Toon"))
            using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + InstanceId + "-Stop"))
            {
                bool firstInstance;
                using (var mutex = new Mutex(true, @"Local\" + InstanceId, out firstInstance))
                {
                    if (!firstInstance)
                    {
                        // Er draait al een WeekKalender: die de popup laten tonen (of laten afsluiten) en zelf stoppen.
                        if (exitRequested)
                        {
                            exitSignal.Set();
                        }
                        else
                        {
                            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                            showSignal.Set();
                        }
                        return 0;
                    }
                    if (exitRequested) return 0;
                    return Run(args, showSignal, exitSignal);
                }
            }
        }

        static int Run(string[] args, EventWaitHandle showSignal, EventWaitHandle exitSignal)
        {
            string folder = AppSettings.DefaultFolder;
            Log.Init(Path.Combine(folder, "log.txt"));

            // Een kleine popup heeft geen grafische kaart nodig; zonder Direct3D-apparaat scheelt dat flink in geheugen.
            // De schaduw en afgeronde hoeken tekent Windows zelf (DWM), die blijven gewoon werken.
            if (Environment.GetEnvironmentVariable("WEEKKALENDER_GPU") != "1")
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("Onverwachte fout", e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Onverwachte fout (afsluiten)", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error("Onafgehandelde taak", e.Exception);
                e.SetObserved();
            };

            TrayApp tray = null;
            app.Startup += (s, e) =>
            {
                try
                {
                    tray = new TrayApp(AppSettings.Load(Path.Combine(folder, "instellingen.ini")));
                }
                catch (Exception ex)
                {
                    // Zonder tray-icoon zou het proces onzichtbaar blijven draaien en elke nieuwe start tegenhouden.
                    Log.Error("Starten mislukt", ex);
                    app.Shutdown(1);
                    return;
                }
                ThreadPool.RegisterWaitForSingleObject(showSignal,
                    (state, timedOut) => app.Dispatcher.BeginInvoke(new Action(() => { if (tray != null) tray.ShowPopup(); })),
                    null, Timeout.Infinite, false);
                ThreadPool.RegisterWaitForSingleObject(exitSignal,
                    (state, timedOut) => app.Dispatcher.BeginInvoke(new Action(() => { if (tray != null) tray.Exit(); })),
                    null, Timeout.Infinite, true);
                if (args.Any(a => a.Equals("--toon", StringComparison.OrdinalIgnoreCase)))
                    app.Dispatcher.BeginInvoke(new Action(tray.ShowPopup), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            app.SessionEnding += (s, e) => { if (tray != null) tray.Dispose(); };
            app.Exit += (s, e) => { if (tray != null) tray.Dispose(); };
            return app.Run();
        }
    }
}
