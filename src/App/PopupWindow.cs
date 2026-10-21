using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using WeekKalender.Core;
using WeekKalender.Providers;

namespace WeekKalender.App
{
    /// <summary>
    /// De agenda-popup in Windows 11-stijl. Het venster wordt één keer gemaakt en daarna verborgen en opnieuw getoond,
    /// zodat openen direct gaat.
    /// </summary>
    internal sealed class PopupWindow : Window
    {
        const double PopupWidth = 344;
        const double PreferredHeight = 628;
        const double MinPopupHeight = 380;
        const double ScreenMargin = 12;
        const string LoadingText = "Afspraken laden…";
        const string SyncingText = "Outlook is net gestart en werkt de agenda nog bij.";

        static readonly FontFamily UiFont = new FontFamily("Segoe UI");
        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly HashSet<DateTime> NoDays = new HashSet<DateTime>();

        readonly AppointmentService _service;
        readonly DayCell[] _cells = new DayCell[MonthGrid.Days];
        readonly TextBlock[] _weekLabels = new TextBlock[MonthGrid.Rows];
        readonly DispatcherTimer _slowLoadTimer;
        readonly DispatcherTimer _syncRefreshTimer;
        ThemeInfo _theme;
        Palette _p;

        MonthGrid _grid;
        DateTime _today;
        DateTime _selected;
        ProviderResult _result;
        HashSet<DateTime> _busyDays = NoDays;
        bool _loading;
        bool _noticeIsError;
        int _loadVersion;
        int _wheelDelta;

        TextBlock _monthTitle, _dayTitle, _dayWeek, _status, _notice, _weekPlaceholder;
        StackPanel _list;
        ScrollViewer _scroll;
        Border _thumb, _weekBoxBorder;
        TextBox _weekBox;

        public PopupWindow(AppointmentService service, ThemeInfo theme)
        {
            _service = service;
            _theme = theme;
            _p = Palette.Create(theme);

            Title = "WeekKalender";
            Width = PopupWidth;
            Height = PreferredHeight;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            FontFamily = UiFont;
            FontSize = 14;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // Randloos, maar met de schaduw, afgeronde hoeken en achtergrond van DWM.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });

            _slowLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _slowLoadTimer.Tick += (s, e) =>
            {
                _slowLoadTimer.Stop();
                if (_loading && _result == null) SetStatus(LoadingText + " Dit duurt even als Outlook nog moet opstarten.");
            };

            // Zolang Outlook net gestart is, de afspraken af en toe opnieuw ophalen.
            _syncRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _syncRefreshTimer.Tick += (s, e) =>
            {
                _syncRefreshTimer.Stop();
                if (IsVisible && _grid != null) LoadAppointments();
            };

            SourceInitialized += (s, e) => ApplyBackdrop();
            Deactivated += OnDeactivated;
            PreviewKeyDown += OnPreviewKeyDown;
            BuildUi();
        }

        public DateTime LastAutoHideUtc { get; private set; }

        // ---------------------------------------------------------------- tonen en verbergen

        /// <summary>Maakt het venster alvast aan (buiten beeld), zodat de eerste klik niet hoeft te wachten.</summary>
        public void Prewarm()
        {
            try
            {
                _today = DateTime.Today;
                _grid = MonthGrid.For(_today);
                _selected = _today;
                Render();
                ShowActivated = false;
                Show();
                Hide();
                ShowActivated = true;
            }
            catch (Exception ex)
            {
                Log.Error("Popup voorbereiden", ex);
            }
        }

        public void ShowPopup(NativeMethods.POINT anchor)
        {
            _today = DateTime.Today;
            ShowNotice(null, false);
            _weekBox.Text = "";
            _grid = null;
            ShowMonth(MonthGrid.For(_today), _today);

            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            PositionWindow(hwnd, anchor);
            Show();
            Activate();
            NativeMethods.SetForegroundWindow(hwnd);
            // Tweede keer: na het tonen kloppen DPI en grootte zeker (anders scherm met andere schaling).
            PositionWindow(hwnd, anchor);
            Keyboard.Focus(this);
        }

        public void HidePopup()
        {
            _syncRefreshTimer.Stop();
            if (IsVisible) Hide();
        }

        void OnDeactivated(object sender, EventArgs e)
        {
            if (!IsVisible) return;
            LastAutoHideUtc = DateTime.UtcNow;
            HidePopup();
        }

        /// <summary>Wordt elke halve minuut aangeroepen; werkt 'vandaag' bij als de popup over middernacht open staat.</summary>
        public void OnTick()
        {
            if (_grid != null && DateTime.Today != _today)
            {
                _today = DateTime.Today;
                Render();
            }
        }

        public void ApplyTheme(ThemeInfo theme)
        {
            _theme = theme;
            _p = Palette.Create(theme);
            string weekText = _weekBox != null ? _weekBox.Text : "";
            BuildUi();
            _weekBox.Text = weekText;
            ApplyBackdrop();
            Render();
        }

        void PositionWindow(IntPtr hwnd, NativeMethods.POINT anchor)
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(anchor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = NativeMethods.MONITORINFO.Create();
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

            TaskbarEdge edge;
            RectI area = PopupPlacement.EffectiveArea(info.rcMonitor.ToRectI(), info.rcWork.ToRectI(), NativeMethods.GetTaskbarRect(), out edge);
            double scale = NativeMethods.GetMonitorDpi(monitor) / 96.0;
            int margin = (int)Math.Round(ScreenMargin * scale);
            double height = Math.Max(MinPopupHeight, Math.Min(PreferredHeight, (area.Height - 2 * margin) / scale));
            Height = height;
            int widthPx = (int)Math.Round(PopupWidth * scale);
            int heightPx = (int)Math.Round(height * scale);
            PointI position = PopupPlacement.Place(area, edge, widthPx, heightPx, margin);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, position.X, position.Y, widthPx, heightPx, NativeMethods.SWP_NOACTIVATE);
        }

        void ApplyBackdrop()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Zonder systeemmenu tekent DWM geen sluitknop in het (onzichtbare) titelbalkgebied.
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_SYSMENU) != 0) NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_SYSMENU);

            int dark = _theme.SystemLight ? 0 : 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corners = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

            // Acrylic zoals de flyouts van Windows 11 (vanaf 22H2), tenzij transparantie-effecten uit staan.
            bool backdrop = false;
            if (_theme.Transparency && OsBuild() >= 22621)
            {
                int type = NativeMethods.DWMSBT_TRANSIENTWINDOW;
                backdrop = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
            }
            else
            {
                int none = NativeMethods.DWMSBT_NONE;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            }

            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (backdrop)
            {
                Background = Brushes.Transparent;
                if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            else
            {
                Background = _p.Solid;
                if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = _p.SolidColor;
            }
        }

        static int OsBuild()
        {
            try
            {
                object value = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "0");
                int build;
                return int.TryParse(value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out build) ? build : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // ---------------------------------------------------------------- opbouw

        void BuildUi()
        {
            var root = new Grid { Margin = new Thickness(12) };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });

            var calendar = new StackPanel { Background = Brushes.Transparent };
            calendar.Children.Add(BuildHeader());
            calendar.Children.Add(BuildWeekdayRow());
            calendar.Children.Add(BuildDays());
            calendar.PreviewMouseWheel += OnCalendarWheel;
            Place(root, calendar, 0);

            Place(root, Separator(), 1);

            var dayHeader = new DockPanel { Margin = new Thickness(4, 0, 4, 6) };
            _dayWeek = Text(12, _p.Secondary);
            _dayWeek.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(_dayWeek, Dock.Right);
            _dayTitle = Text(14, _p.Text, FontWeights.SemiBold);
            dayHeader.Children.Add(_dayWeek);
            dayHeader.Children.Add(_dayTitle);
            Place(root, dayHeader, 2);

            Place(root, BuildList(), 3);

            _notice = Text(12, _p.Secondary);
            _notice.TextWrapping = TextWrapping.Wrap;
            _notice.TextTrimming = TextTrimming.None;
            _notice.Margin = new Thickness(4, 6, 4, 0);
            _notice.Visibility = Visibility.Collapsed;
            Place(root, _notice, 4);

            Place(root, Separator(), 5);
            Place(root, BuildFooter(), 6);
            Content = root;
        }

        UIElement BuildHeader()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _monthTitle = Text(14, _p.Text, FontWeights.SemiBold);
            _monthTitle.VerticalAlignment = VerticalAlignment.Center;
            _monthTitle.Margin = new Thickness(8, 0, 0, 0);
            Place(grid, _monthTitle, 0, 0);
            Place(grid, IconButton("", "Vorige maand", () => ChangeMonth(-1)), 0, 1);
            Place(grid, IconButton("", "Volgende maand", () => ChangeMonth(1)), 0, 2);
            return grid;
        }

        UIElement BuildWeekdayRow()
        {
            Grid grid = EightColumns(1, 28);
            for (int c = 0; c < 8; c++)
            {
                TextBlock label = Text(12, c == 0 ? _p.Dimmed : _p.Text);
                label.Text = c == 0 ? "wk" : DutchText.DayShort(c - 1);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                Place(grid, label, 0, c);
            }
            return grid;
        }

        UIElement BuildDays()
        {
            Grid grid = EightColumns(MonthGrid.Rows, 40);
            for (int r = 0; r < MonthGrid.Rows; r++)
            {
                TextBlock week = Text(12, _p.Dimmed);
                week.HorizontalAlignment = HorizontalAlignment.Center;
                week.VerticalAlignment = VerticalAlignment.Center;
                _weekLabels[r] = week;
                Place(grid, week, r, 0);

                for (int c = 0; c < 7; c++)
                {
                    var cell = new DayCell();
                    cell.MouseEnter += (s, e) => RenderCell((DayCell)s);
                    cell.MouseLeave += (s, e) => RenderCell((DayCell)s);
                    cell.MouseLeftButtonUp += (s, e) => Select(((DayCell)s).Date);
                    cell.Invoked += (s, e) => Select(((DayCell)s).Date);
                    _cells[r * 7 + c] = cell;
                    Place(grid, cell, r, c + 1);
                }
            }
            return grid;
        }

        UIElement BuildList()
        {
            var area = new Grid();
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });

            _status = Text(13, _p.Secondary);
            _status.TextWrapping = TextWrapping.Wrap;
            _status.TextTrimming = TextTrimming.None;
            _status.Margin = new Thickness(4, 2, 4, 4);
            _list = new StackPanel();
            var content = new StackPanel();
            content.Children.Add(_status);
            content.Children.Add(_list);

            _scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
            _scroll.ScrollChanged += (s, e) => UpdateThumb();

            // Dun schuifbalkje zoals in Windows 11, alleen zichtbaar als de lijst niet past.
            var track = new Canvas { Margin = new Thickness(3, 2, 0, 2) };
            _thumb = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = _p.Dimmed, Visibility = Visibility.Collapsed };
            track.Children.Add(_thumb);

            Place(area, _scroll, 0, 0);
            Place(area, track, 0, 1);
            return area;
        }

        UIElement BuildFooter()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Place(grid, TextButton("Vandaag", GoToToday), 0, 0);

            _weekBox = new TextBox
            {
                FontSize = 13,
                Foreground = _p.Text,
                CaretBrush = _p.Text,
                SelectionBrush = _p.AccentFill,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxLength = 16,
                Template = PlainTextBoxTemplate(),
            };
            AutomationProperties.SetName(_weekBox, "Weeknummer");
            _weekBox.ContextMenuOpening += (s, e) => e.Handled = true;   // geen Engelstalig standaardmenu
            _weekBox.TextChanged += (s, e) =>
            {
                _weekPlaceholder.Visibility = _weekBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_noticeIsError) ShowNotice(null, false);
            };
            _weekBox.GotKeyboardFocus += (s, e) => StyleWeekBox(true);
            _weekBox.LostKeyboardFocus += (s, e) => StyleWeekBox(false);

            _weekPlaceholder = Text(13, _p.Dimmed);
            _weekPlaceholder.Text = "Weeknummer";
            _weekPlaceholder.IsHitTestVisible = false;
            _weekPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            _weekPlaceholder.Margin = new Thickness(10, 0, 6, 0);

            var boxContent = new Grid();
            boxContent.Children.Add(_weekPlaceholder);
            boxContent.Children.Add(_weekBox);
            _weekBoxBorder = new Border
            {
                Width = 104,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                Background = _p.ControlFill,
                Child = boxContent,
                Margin = new Thickness(0, 0, 6, 0),
            };
            StyleWeekBox(false);
            Place(grid, _weekBoxBorder, 0, 2);
            Place(grid, TextButton("Ga naar week", GoToWeek), 0, 3);
            return grid;
        }

        void StyleWeekBox(bool focused)
        {
            _weekBoxBorder.BorderBrush = focused ? _p.AccentFill : _p.ControlStroke;
            _weekBoxBorder.BorderThickness = focused ? new Thickness(1, 1, 1, 2) : new Thickness(1);
        }

        static ControlTemplate PlainTextBoxTemplate()
        {
            var template = new ControlTemplate(typeof(TextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 6, 0));
            host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            template.VisualTree = host;
            return template;
        }

        FlatButton IconButton(string glyph, string name, Action click)
        {
            var button = new FlatButton
            {
                Width = 32,
                Height = 32,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = IconFont,
                    FontSize = 12,
                    Foreground = _p.Secondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            button.SetBrushes(Brushes.Transparent, _p.Hover, _p.Pressed);
            button.Click += (s, e) => click();
            AutomationProperties.SetName(button, name);
            return button;
        }

        FlatButton TextButton(string label, Action click)
        {
            TextBlock text = Text(13, _p.Text);
            text.Text = label;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var button = new FlatButton
            {
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = _p.ControlStroke,
                Child = text,
            };
            button.SetBrushes(_p.ControlFill, _p.ControlHover, _p.ControlPressed);
            button.Click += (s, e) => click();
            AutomationProperties.SetName(button, label);
            return button;
        }

        Border Separator()
        {
            return new Border { Height = 1, Background = _p.Separator, Margin = new Thickness(-12, 8, -12, 8) };
        }

        static Grid EightColumns(int rows, double rowHeight)
        {
            var grid = new Grid();
            for (int c = 0; c < 8; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowHeight) });
            return grid;
        }

        static TextBlock Text(double size, Brush foreground, FontWeight? weight = null)
        {
            return new TextBlock
            {
                FontSize = size,
                Foreground = foreground,
                FontWeight = weight ?? FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        static void Place(Grid grid, UIElement element, int row, int column = 0)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        // ---------------------------------------------------------------- navigatie

        void ChangeMonth(int delta)
        {
            MonthGrid grid = _grid.AddMonths(delta);
            // In de maand van vandaag is vandaag geselecteerd, anders de 1e.
            DateTime select = grid.IsInMonth(_today) ? _today : new DateTime(grid.Year, grid.Month, 1);
            ShowMonth(grid, select);
        }

        void Select(DateTime date)
        {
            if (date.Year < 1900 || date.Year > 2200) return;
            if (_grid != null && _grid.IsInMonth(date))
            {
                _selected = date.Date;
                Render();
            }
            else
            {
                ShowMonth(MonthGrid.For(date), date);
            }
        }

        void GoToToday()
        {
            _today = DateTime.Today;
            ShowNotice(null, false);
            ShowMonth(MonthGrid.For(_today), _today);
        }

        void GoToWeek()
        {
            int defaultYear = _grid != null ? _grid.Year : _today.Year;
            WeekInputResult input = WeekInput.Parse(_weekBox.Text, defaultYear);
            if (!input.Ok)
            {
                ShowNotice(input.Error, true);
                _weekBox.Focus();
                _weekBox.SelectAll();
                return;
            }
            ShowNotice(null, false);
            DateTime monday = IsoWeek.FirstDayOfWeek(input.Year, input.Week);
            ShowMonth(MonthGrid.For(monday), monday);
        }

        void ShowMonth(MonthGrid grid, DateTime select)
        {
            bool sameGrid = _grid != null && _grid.FirstCell == grid.FirstCell && _grid.Month == grid.Month;
            _grid = grid;
            _selected = select.Date;
            if (sameGrid) Render();
            else LoadAppointments();
        }

        void OnCalendarWheel(object sender, MouseWheelEventArgs e)
        {
            _wheelDelta += e.Delta;
            while (_wheelDelta >= 120)
            {
                _wheelDelta -= 120;
                ChangeMonth(-1);
            }
            while (_wheelDelta <= -120)
            {
                _wheelDelta += 120;
                ChangeMonth(1);
            }
            e.Handled = true;
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HidePopup();
                e.Handled = true;
                return;
            }
            if (Keyboard.FocusedElement == _weekBox)
            {
                if (e.Key == Key.Enter)
                {
                    GoToWeek();
                    e.Handled = true;
                }
                return;
            }
            if (_grid == null) return;
            if ((e.Key >= Key.D0 && e.Key <= Key.D9) || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9))
            {
                // Direct een weeknummer typen: het cijfer komt in het weekveld terecht.
                _weekBox.Focus();
                return;
            }
            switch (e.Key)
            {
                case Key.Left: Select(_selected.AddDays(-1)); break;
                case Key.Right: Select(_selected.AddDays(1)); break;
                case Key.Up: Select(_selected.AddDays(-7)); break;
                case Key.Down: Select(_selected.AddDays(7)); break;
                case Key.PageUp: ChangeMonth(-1); break;
                case Key.PageDown: ChangeMonth(1); break;
                case Key.Home: GoToToday(); break;
                default: return;
            }
            e.Handled = true;
        }

        // ---------------------------------------------------------------- afspraken laden

        async void LoadAppointments()
        {
            MonthGrid grid = _grid;
            int version = ++_loadVersion;
            DateTime start = grid.FirstCell, end = grid.LastCell.AddDays(1);

            ProviderResult cached;
            bool fresh;
            if (_service.TryGetCached(start, out cached, out fresh))
            {
                _result = cached;
                _loading = !fresh;
                Render();
                if (fresh)
                {
                    ShowSyncState(cached);
                    Prefetch(grid);
                    return;
                }
            }
            else
            {
                _result = null;
                _loading = true;
                Render();
                _slowLoadTimer.Stop();
                _slowLoadTimer.Start();
            }

            ProviderResult result;
            try
            {
                result = await _service.GetAsync(start, end);
            }
            catch (Exception ex)
            {
                Log.Error("Afspraken laden", ex);
                result = ProviderResult.Failed("Afspraken konden niet worden geladen.");
            }
            if (version != _loadVersion) return;

            _slowLoadTimer.Stop();
            // Mislukt verversen terwijl er al (oudere) afspraken staan: die laten staan.
            if (result.IsOk || _result == null || !_result.IsOk) _result = result;
            _loading = false;
            Render();
            ShowSyncState(result);
            if (result.IsOk) Prefetch(grid);
        }

        void ShowSyncState(ProviderResult result)
        {
            if (result.IsOk && result.Provisional)
            {
                // Een foutmelding (bijv. van het weekveld) gaat voor.
                if (!_noticeIsError || _notice.Visibility != Visibility.Visible) ShowNotice(SyncingText, false);
                _syncRefreshTimer.Stop();
                _syncRefreshTimer.Start();
            }
            else if (_notice.Text == SyncingText)
            {
                ShowNotice(null, false);
            }
        }

        /// <summary>Vorige en volgende maand alvast ophalen, zodat bladeren direct gaat.</summary>
        async void Prefetch(MonthGrid grid)
        {
            foreach (MonthGrid neighbour in new[] { grid.AddMonths(1), grid.AddMonths(-1) })
            {
                if (!IsVisible || _service.IsFresh(neighbour.FirstCell)) continue;
                try
                {
                    await _service.GetAsync(neighbour.FirstCell, neighbour.LastCell.AddDays(1));
                }
                catch (Exception ex)
                {
                    Log.Warn("Vooruit laden: " + ex.Message);
                }
            }
        }

        async void OpenAppointment(Appointment appointment)
        {
            ShowNotice("Afspraak wordt geopend in Outlook…", false);
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            ProviderResult result;
            try
            {
                result = await _service.OpenAsync(appointment);
            }
            catch (Exception ex)
            {
                Log.Error("Afspraak openen", ex);
                result = ProviderResult.Failed("De afspraak kon niet worden geopend.");
            }
            if (result.IsOk)
            {
                ShowNotice(null, false);
                HidePopup();
            }
            else
            {
                ShowNotice(result.Message, true);
            }
        }

        // ---------------------------------------------------------------- tekenen

        void Render()
        {
            if (_grid == null) return;
            _monthTitle.Text = DutchText.Capitalize(DutchText.MonthYear(_grid.Year, _grid.Month));
            _busyDays = _result != null && _result.IsOk ? AppointmentDay.DaysWithItems(_result.Items, _grid.FirstCell, _grid.LastCell) : NoDays;

            int selectedRow = _grid.RowOf(_selected);
            for (int r = 0; r < MonthGrid.Rows; r++)
            {
                TextBlock label = _weekLabels[r];
                label.Text = _grid.WeekNumber(r).ToString(CultureInfo.InvariantCulture);
                label.Foreground = r == selectedRow ? _p.AccentText : _p.Dimmed;
                label.FontWeight = r == selectedRow ? FontWeights.SemiBold : FontWeights.Normal;
            }
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].Date = _grid.FirstCell.AddDays(i);
                RenderCell(_cells[i]);
            }
            RenderDay();
        }

        void RenderCell(DayCell cell)
        {
            if (_grid == null) return;
            DateTime date = cell.Date;
            bool inMonth = _grid.IsInMonth(date);
            bool isToday = date == _today;
            bool isSelected = date == _selected;

            cell.Label.Text = date.Day.ToString(CultureInfo.InvariantCulture);
            cell.Circle.Fill = isToday ? _p.AccentFill : cell.IsMouseOver ? _p.Hover : Brushes.Transparent;
            cell.Circle.Stroke = isSelected && !isToday ? _p.AccentFill : null;
            cell.InnerRing.Stroke = isSelected && isToday ? _p.OnAccent : null;
            cell.Label.Foreground = isToday ? _p.OnAccent : inMonth ? _p.Text : _p.Dimmed;
            cell.Label.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            bool busy = _busyDays.Contains(date);
            cell.Dot.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            cell.Dot.Fill = isToday ? _p.OnAccent : inMonth ? _p.Secondary : _p.Dimmed;
            AutomationProperties.SetName(cell, DutchText.LongDate(date) + (busy ? ", met afspraken" : ""));
        }

        void RenderDay()
        {
            _dayTitle.Text = DutchText.Capitalize(DutchText.DayTitle(_selected, _today));
            _dayWeek.Text = "week " + IsoWeek.GetWeek(_selected).ToString(CultureInfo.InvariantCulture);
            _list.Children.Clear();

            if (_result == null)
            {
                SetStatus(_loading ? LoadingText : "");
            }
            else if (!_result.IsOk)
            {
                SetStatus(_result.Message);
            }
            else
            {
                List<Appointment> items = AppointmentDay.For(_result.Items, _selected);
                SetStatus(items.Count == 0 ? "Geen afspraken" : null);
                foreach (Appointment appointment in items) _list.Children.Add(BuildItem(appointment));
            }
            _scroll.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateThumb));
        }

        UIElement BuildItem(Appointment appointment)
        {
            var bar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = _p.AccentFill,
                Margin = new Thickness(0, 2, 10, 2),
            };
            DockPanel.SetDock(bar, Dock.Left);

            var lines = new StackPanel();
            TextBlock time = Text(12, _p.Secondary);
            time.Text = DutchText.TimeRange(appointment, _selected);
            lines.Children.Add(time);
            TextBlock subject = Text(14, _p.Text);
            subject.Text = appointment.Subject;
            subject.TextWrapping = TextWrapping.Wrap;
            subject.TextTrimming = TextTrimming.None;
            lines.Children.Add(subject);
            if (!string.IsNullOrEmpty(appointment.Location))
            {
                TextBlock location = Text(12, _p.Secondary);
                location.Text = appointment.Location;
                location.TextWrapping = TextWrapping.Wrap;
                location.TextTrimming = TextTrimming.None;
                lines.Children.Add(location);
            }

            var row = new DockPanel();
            row.Children.Add(bar);
            row.Children.Add(lines);

            var button = new FlatButton { Child = row, Padding = new Thickness(6, 6, 8, 6), Margin = new Thickness(0, 0, 0, 2) };
            button.SetBrushes(Brushes.Transparent, _p.Hover, _p.Pressed);
            if (_service.CanOpen(appointment))
            {
                button.Click += (s, e) => OpenAppointment(appointment);
                AutomationProperties.SetName(button, appointment.Subject + ", openen in Outlook");
            }
            else
            {
                button.Interactive = false;
                AutomationProperties.SetName(button, appointment.Subject);
            }
            return button;
        }

        void SetStatus(string text)
        {
            _status.Text = text ?? "";
            _status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void ShowNotice(string text, bool isError)
        {
            if (_notice == null) return;
            _noticeIsError = isError && !string.IsNullOrEmpty(text);
            _notice.Text = text ?? "";
            _notice.Foreground = isError ? _p.Error : _p.Secondary;
            _notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void UpdateThumb()
        {
            double viewport = _scroll.ViewportHeight, extent = _scroll.ExtentHeight;
            if (viewport <= 0 || extent <= viewport + 1)
            {
                _thumb.Visibility = Visibility.Collapsed;
                return;
            }
            _thumb.Visibility = Visibility.Visible;
            double height = Math.Max(20, viewport * viewport / extent);
            _thumb.Height = height;
            Canvas.SetTop(_thumb, (viewport - height) * (_scroll.VerticalOffset / (extent - viewport)));
        }
    }
}
