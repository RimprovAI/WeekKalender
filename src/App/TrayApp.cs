using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WeekKalender.Core;
using WeekKalender.Providers;

namespace WeekKalender.App
{
    /// <summary>
    /// Het icoon in het systeemvak: weeknummer tekenen, klikken afhandelen, menu tonen en bijwerken na middernacht,
    /// slaapstand, thema- of schaalwijziging en een herstart van Verkenner.
    /// </summary>
    internal sealed class TrayApp : IDisposable
    {
        const int IconId = 1;
        const int CallbackMessage = NativeMethods.WM_APP + 1;
        const int CommandAutostart = 1;
        const int CommandExit = 2;

        readonly AppSettings _settings;
        readonly Autostart _autostart;
        readonly HwndSource _window;
        readonly int _taskbarCreatedMessage;
        readonly DispatcherTimer _timer;
        readonly DispatcherTimer _trimTimer;
        readonly AppointmentService _service;
        readonly PopupWindow _popup;
        ThemeInfo _theme;
        IntPtr _icon;
        bool _iconAdded;
        int _iconWeek = -1, _iconSize = -1;
        bool _iconLight;
        DateTime _iconDate;
        bool _iconFailureLogged;
        int _lastToggleTick;
        bool _toggledBefore;
        bool _refreshPending;
        bool _clockChanged;
        bool _disposed;

        public TrayApp(AppSettings settings)
        {
            _settings = settings;
            _autostart = new Autostart(Assembly.GetEntryAssembly().Location);
            _theme = ThemeInfo.Read(settings.Theme);

            // Verborgen venster (geen message-only venster: die krijgen geen broadcasts als TaskbarCreated en WM_SETTINGCHANGE).
            var parameters = new HwndSourceParameters("WeekKalender")
            {
                WindowStyle = unchecked((int)0x80000000),   // WS_POPUP, niet zichtbaar
                ExtendedWindowStyle = 0x00000080,           // WS_EX_TOOLWINDOW
                Width = 0,
                Height = 0,
            };
            _window = new HwndSource(parameters);
            _window.AddHook(WndProc);
            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            _service = AppointmentService.FromSettings(settings);
            _popup = new PopupWindow(_service, _theme);

            // Geen vaste polling: de timer loopt tot vlak na middernacht (met een vangnet van hooguit 10 minuten voor
            // het geval een ontwaak-melding gemist wordt), of kort als het icoon nog niet geplaatst kon worden.
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += (s, e) => Refresh(false);
            UpdateIcon(true);
            ScheduleTimer();

            // Als de popup een tijdje dicht is, ongebruikt geheugen teruggeven aan Windows.
            _trimTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
            _trimTimer.Tick += (s, e) => TrimMemory();
            _popup.IsVisibleChanged += (s, e) =>
            {
                _trimTimer.Stop();
                if (!_popup.IsVisible) _trimTimer.Start();
            };

            _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                _popup.Prewarm();
                _trimTimer.Start();
            }));
            Log.Info("Gestart, bron: " + settings.Source);
        }

        void TrimMemory()
        {
            _trimTimer.Stop();
            if (_disposed || _popup.IsVisible) return;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            NativeMethods.TrimWorkingSet();
        }

        public void ShowPopup()
        {
            if (_popup.IsVisible)
            {
                _popup.Activate();
                return;
            }
            if (_settings.ReloadIfChanged())
            {
                Log.Info("Instellingen gewijzigd, bron: " + _settings.Source + ", thema: " + _settings.Theme);
                _service.Reconfigure(_settings);
                Refresh(false);
            }
            _popup.ShowPopup(IconAnchor());
        }

        void TogglePopup()
        {
            if (_popup.IsVisible)
            {
                _popup.HidePopup();
                return;
            }
            // Een klik op het icoon terwijl de popup open is, sluit de popup al via 'deactiveren'; dan niet meteen weer openen.
            if ((DateTime.UtcNow - _popup.LastAutoHideUtc).TotalMilliseconds < 400) return;
            ShowPopup();
        }

        NativeMethods.POINT IconAnchor()
        {
            var id = new NativeMethods.NOTIFYICONIDENTIFIER
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER)),
                hWnd = _window.Handle,
                uID = IconId,
            };
            NativeMethods.RECT rect;
            if (NativeMethods.Shell_NotifyIconGetRect(ref id, out rect) == 0)
                return new NativeMethods.POINT((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
            NativeMethods.POINT cursor;
            NativeMethods.GetCursorPos(out cursor);
            return cursor;
        }

        IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == CallbackMessage)
            {
                int ev = (int)((long)lParam & 0xFFFF);
                if (ev == NativeMethods.NIN_SELECT || ev == NativeMethods.NIN_KEYSELECT)
                {
                    // Enter op het icoon en een dubbelklik geven de melding twee keer kort na elkaar: dat telt als één keer.
                    int now = Environment.TickCount;
                    bool repeat = _toggledBefore && unchecked((uint)(now - _lastToggleTick)) < 250;
                    _lastToggleTick = now;
                    _toggledBefore = true;
                    if (!repeat) TogglePopup();
                }
                else if (ev == NativeMethods.WM_CONTEXTMENU)
                {
                    long w = (long)wParam;
                    ShowMenu((short)(w & 0xFFFF), (short)((w >> 16) & 0xFFFF));
                }
                handled = true;
                return IntPtr.Zero;
            }

            if (message == _taskbarCreatedMessage)
            {
                // Verkenner is herstart: icoon opnieuw toevoegen.
                _iconAdded = false;
                UpdateIcon(true);
                ScheduleTimer();
                return IntPtr.Zero;
            }

            switch (message)
            {
                case NativeMethods.WM_TIMECHANGE:
                    _clockChanged = true;
                    RefreshSoon();
                    break;
                case NativeMethods.WM_SETTINGCHANGE:
                case NativeMethods.WM_DISPLAYCHANGE:
                case NativeMethods.WM_DPICHANGED:
                    RefreshSoon();
                    break;
                case NativeMethods.WM_POWERBROADCAST:
                    int power = (int)(long)wParam;
                    if (power == NativeMethods.PBT_APMRESUMEAUTOMATIC || power == NativeMethods.PBT_APMRESUMESUSPEND)
                    {
                        _clockChanged = true;
                        RefreshSoon();
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        /// <summary>Windows stuurt instellingsmeldingen vaak in reeksen; die worden samengenomen tot één keer bijwerken.</summary>
        void RefreshSoon()
        {
            if (_refreshPending || _disposed) return;
            _refreshPending = true;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                if (_clockChanged)
                {
                    _clockChanged = false;
                    TimeZoneInfo.ClearCachedData();
                    CultureInfo.CurrentCulture.ClearCachedData();
                    _service.InvalidateCache();
                }
                Refresh(false);
            }));
        }

        void Refresh(bool forceIcon)
        {
            if (_disposed) return;
            ThemeInfo theme = ThemeInfo.Read(_settings.Theme);
            if (!theme.SameAs(_theme))
            {
                _theme = theme;
                _popup.ApplyTheme(theme);
                forceIcon = true;
            }
            UpdateIcon(forceIcon);
            _popup.OnTick();
            ScheduleTimer();
        }

        void ScheduleTimer()
        {
            DateTime now = DateTime.Now;
            TimeSpan untilMidnight = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(2);
            TimeSpan limit = _iconAdded ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(5);
            _timer.Stop();
            _timer.Interval = untilMidnight < limit ? untilMidnight : limit;
            _timer.Start();
        }

        void UpdateIcon(bool force)
        {
            DateTime today = DateTime.Today;
            int week = IsoWeek.GetWeek(today);
            int size = NativeMethods.GetTrayIconSize();
            bool light = _theme.SystemLight;
            if (!force && _iconAdded && week == _iconWeek && size == _iconSize && light == _iconLight && today == _iconDate) return;

            IntPtr icon;
            try
            {
                icon = IconRenderer.Create(week, size, light);
            }
            catch (Exception ex)
            {
                Log.Error("Icoon tekenen", ex);
                return;
            }

            NativeMethods.NOTIFYICONDATA data = NewIconData();
            data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
            data.uCallbackMessage = CallbackMessage;
            data.hIcon = icon;
            data.szTip = DutchText.Tooltip(today);

            // Altijd eerst bijwerken proberen, ook na een mislukte poging: het icoon kan er dan nog gewoon staan,
            // en toevoegen mislukt zolang het bestaat.
            bool ok = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
            if (!ok)
            {
                ok = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
                if (ok)
                {
                    data.uTimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
                    NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
                }
            }
            if (ok)
            {
                _iconFailureLogged = false;
            }
            else if (!_iconFailureLogged)
            {
                // Bij het aanmelden is de taakbalk soms nog niet klaar; de timer probeert het om de paar seconden opnieuw.
                Log.Warn("Icoon kon (nog) niet in het systeemvak worden gezet; dit wordt opnieuw geprobeerd");
                _iconFailureLogged = true;
            }

            if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
            _icon = icon;
            _iconAdded = ok;
            _iconWeek = week;
            _iconSize = size;
            _iconLight = light;
            _iconDate = today;
        }

        NativeMethods.NOTIFYICONDATA NewIconData()
        {
            return new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
                hWnd = _window.Handle,
                uID = IconId,
                szTip = "",
                szInfo = "",
                szInfoTitle = "",
            };
        }

        void ShowMenu(int x, int y)
        {
            _popup.HidePopup();
            IntPtr menu = NativeMethods.CreatePopupMenu();
            try
            {
                bool autostart = SafeIsAutostartEnabled();
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING | (autostart ? NativeMethods.MF_CHECKED : 0), (IntPtr)CommandAutostart, "Autostart met Windows");
                NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
                NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (IntPtr)CommandExit, "Afsluiten");

                // Zonder voorgrond verdwijnt het menu niet bij klikken ernaast (bekende eigenaardigheid van Windows).
                NativeMethods.SetForegroundWindow(_window.Handle);
                int flags = NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY | NativeMethods.TPM_WORKAREA;
                flags |= MenuOpensDownward(x, y) ? NativeMethods.TPM_TOPALIGN : NativeMethods.TPM_BOTTOMALIGN;
                int command = NativeMethods.TrackPopupMenuEx(menu, flags, x, y, _window.Handle, IntPtr.Zero);
                NativeMethods.PostMessage(_window.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (command == CommandAutostart) ToggleAutostart(autostart);
                else if (command == CommandExit) Exit();
            }
            finally
            {
                NativeMethods.DestroyMenu(menu);
            }
        }

        static bool MenuOpensDownward(int x, int y)
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(x, y), NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = NativeMethods.MONITORINFO.Create();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return false;
            TaskbarEdge edge;
            PopupPlacement.EffectiveArea(info.rcMonitor.ToRectI(), info.rcWork.ToRectI(), NativeMethods.GetTaskbarRect(), out edge);
            return edge == TaskbarEdge.Top;
        }

        bool SafeIsAutostartEnabled()
        {
            try
            {
                return _autostart.IsEnabled();
            }
            catch (Exception ex)
            {
                Log.Warn("Autostart lezen: " + ex.Message);
                return false;
            }
        }

        void ToggleAutostart(bool currentlyEnabled)
        {
            try
            {
                if (currentlyEnabled) _autostart.Disable();
                else _autostart.Enable();
                Log.Info("Autostart " + (currentlyEnabled ? "uit" : "aan"));
            }
            catch (Exception ex)
            {
                Log.Error("Autostart wijzigen", ex);
            }
        }

        public void Exit()
        {
            Dispose();
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _trimTimer.Stop();
            // Altijd proberen te verwijderen; het icoon kan er staan terwijl de laatste update mislukte.
            NativeMethods.NOTIFYICONDATA data = NewIconData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _iconAdded = false;
            try { _popup.Close(); }
            catch (Exception) { }
            _service.Dispose();
            _window.Dispose();
            if (_icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
            Log.Info("Afgesloten");
        }
    }
}
