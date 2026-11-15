using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using WeekKalender.Core;
using WeekKalender.Providers;

namespace WeekKalender.App
{
    /// <summary>
    /// De agenda-popup "Michalendar" in de eigen huisstijl. Het venster wordt één keer gemaakt en daarna
    /// verborgen en opnieuw getoond, zodat openen direct gaat.
    /// </summary>
    internal sealed class PopupWindow : Window
    {
        public const string AppTitle = "Michalendar";

        const double PopupWidth = 344;
        const double PreferredHeight = 690;
        const double MinPopupHeight = 430;
        const double ScreenMargin = 12;
        const string LoadingText = "Afspraken laden…";
        const string SyncingText = "Outlook is net gestart en werkt de agenda nog bij.";

        // Lettertype Basis Grotesque Pro met Arial als terugval. Staat het merklettertype op de pc, dan wordt het gebruikt.
        static readonly FontFamily UiFont = new FontFamily("Basis Grotesque Pro, Arial");
        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly string ChevronUp = ((char)0xE70E).ToString();
        static readonly string ChevronDown = ((char)0xE70D).ToString();
        static readonly CornerRadius Pill = new CornerRadius(16);
        static readonly Dictionary<DateTime, int> NoCounts = new Dictionary<DateTime, int>();

        /// <summary>Wat er op de meldingsregel staat; bepaalt wie hem mag overschrijven of wissen.</summary>
        enum NoticeKind
        {
            None,
            Info,            // lopende actie van de gebruiker
            Error,           // fout bij een actie van de gebruiker (weekveld, afspraak openen)
            Syncing,         // Outlook is net gestart
            RefreshFailed,   // verversen mislukt, oudere afspraken blijven staan
        }

        readonly AppointmentService _service;
        readonly DayCell[] _cells = new DayCell[MonthGrid.Days];
        readonly TextBlock[] _weekLabels = new TextBlock[MonthGrid.Rows];
        readonly DispatcherTimer _slowLoadTimer;
        readonly DispatcherTimer _syncRefreshTimer;
        Palette _p;

        MonthGrid _grid;
        DateTime _today;
        DateTime _selected;
        ProviderResult _result;
        Dictionary<DateTime, int> _busyDays = NoCounts;
        bool _loading;
        NoticeKind _noticeKind;
        int _loadVersion;
        int _wheelDelta;

        TextBlock _monthTitle, _dayTitle, _dayWeek, _status, _notice, _weekPlaceholder, _titleWeek, _titleDate;
        Border _weekRow;
        Grid _picker;
        int _pickerYear;
        readonly TranslateTransform _weekRowShift = new TranslateTransform();
        Grid _daysGrid;
        StackPanel _empty;
        Style _focusStyle;
        ProviderResult _listResult;
        DateTime _listDay;
        bool _weekRowPlaced;
        StackPanel _list;
        ScrollViewer _scroll;
        Border _thumb, _weekBoxBorder;
        TextBox _weekBox;

        public PopupWindow(AppointmentService service, ThemeInfo theme)
        {
            _service = service;
            _p = Palette.Create(theme.PopupLight);

            Title = AppTitle;
            Width = PopupWidth;
            Height = PreferredHeight;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            Background = _p.Solid;
            FontFamily = UiFont;
            FontSize = 14;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            // Randloos, maar met de schaduw en afgeronde hoeken van Windows (DWM).
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

            SourceInitialized += (s, e) => ApplyWindowStyle();
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
            ClearNotice();
            HidePicker();
            _weekBox.Text = "";
            _grid = null;
            ShowMonth(MonthGrid.For(_today), _today);

            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            PositionWindow(hwnd, anchor);
            Show();
            AnimateIn();
            Activate();
            NativeMethods.SetForegroundWindow(hwnd);
            // Tweede keer: na het tonen kloppen DPI en grootte zeker (anders scherm met andere schaling).
            PositionWindow(hwnd, anchor);
            Keyboard.Focus(this);
        }

        /// <summary>Inhoud komt in een tiende seconde zacht omhoog in beeld; uit als Windows-animaties uit staan.</summary>
        void AnimateIn()
        {
            var content = Content as FrameworkElement;
            if (content == null || !SystemParameters.ClientAreaAnimation) return;
            var shift = new TranslateTransform(0, 10);
            content.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(130);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, duration) { EasingFunction = ease });
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
            if (_p.Dark == !theme.PopupLight) return;
            _p = Palette.Create(theme.PopupLight);
            string weekText = _weekBox.Text;
            BuildUi();
            _noticeKind = NoticeKind.None;
            _weekBox.Text = weekText;
            ApplyWindowStyle();
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

        void ApplyWindowStyle()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Zonder systeemmenu tekent DWM geen sluitknop in het (onzichtbare) titelbalkgebied.
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_SYSMENU) != 0) NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_SYSMENU);

            int dark = _p.Dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corners = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

            // Effen huisstijlkleur als achtergrond (geen doorschijnend effect).
            Background = _p.Solid;
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = _p.SolidColor;
        }

        // ---------------------------------------------------------------- opbouw

        /// <summary>Focusring in merkrood voor wie met het toetsenbord werkt.</summary>
        Style BuildFocusStyle()
        {
            var ring = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
            ring.SetValue(System.Windows.Shapes.Shape.StrokeProperty, _p.AccentFill);
            ring.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.0);
            ring.SetValue(System.Windows.Shapes.Rectangle.RadiusXProperty, 15.0);
            ring.SetValue(System.Windows.Shapes.Rectangle.RadiusYProperty, 15.0);
            ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-3));
            ring.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var style = new Style();
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = ring }));
            return style;
        }

        void BuildUi()
        {
            _focusStyle = BuildFocusStyle();
            var root = new Grid { Margin = new Thickness(12, 10, 12, 12) };
            foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });

            var calendar = new StackPanel { Background = Brushes.Transparent };
            calendar.Children.Add(BuildHeader());
            calendar.Children.Add(BuildWeekdayRow());
            calendar.Children.Add(BuildDays());
            calendar.PreviewMouseWheel += OnCalendarWheel;
            Place(root, calendar, 0);

            // Maandkiezer: ligt over de kalender heen en is alleen zichtbaar na een klik op de maandnaam.
            _picker = new Grid { Background = _p.Solid, Visibility = Visibility.Collapsed };
            Place(root, _picker, 0);

            Place(root, Separator(), 1);

            var dayHeader = new DockPanel { Margin = new Thickness(4, 0, 2, 8) };
            _dayWeek = Text(12, _p.Text, FontWeights.Bold);
            var weekBadge = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = _p.ControlFill,
                Padding = new Thickness(9, 3, 9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _dayWeek,
            };
            DockPanel.SetDock(weekBadge, Dock.Right);
            _dayTitle = Text(15, _p.Text, FontWeights.Bold);
            _dayTitle.VerticalAlignment = VerticalAlignment.Center;
            dayHeader.Children.Add(weekBadge);
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

            var outer = new DockPanel();
            UIElement titleBar = BuildTitleBar();
            DockPanel.SetDock(titleBar, Dock.Top);
            outer.Children.Add(titleBar);
            outer.Children.Add(root);
            Content = outer;
        }

        /// <summary>Titel in wit op merkrood, zoals het woordmerk.</summary>
        UIElement BuildTitleBar()
        {
            TextBlock name = Text(18, _p.OnAccent, FontWeights.Bold);
            name.Text = AppTitle;
            _titleDate = Text(12, _p.OnAccentSoft);
            _titleDate.Margin = new Thickness(0, 1, 0, 0);
            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 12, 1) };
            title.Children.Add(name);
            title.Children.Add(_titleDate);

            // Rechts het weeknummer van vandaag, ook als je door andere maanden bladert.
            _titleWeek = Text(12, _p.OnAccent, FontWeights.Bold);
            var badge = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(10, 3, 10, 4),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _titleWeek,
            };
            DockPanel.SetDock(badge, Dock.Right);
            var bar = new DockPanel();
            bar.Children.Add(badge);
            bar.Children.Add(title);
            return new Border { Height = 60, Background = _p.TitleFill, Child = bar };
        }

        UIElement BuildHeader()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // De maandnaam is een knop: klik om snel een andere maand of een ander jaar te kiezen.
            _monthTitle = Text(16, _p.Text, FontWeights.Bold);
            _monthTitle.VerticalAlignment = VerticalAlignment.Center;
            var caret = new TextBlock { Text = ChevronDown, FontFamily = IconFont, FontSize = 8, Foreground = _p.Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 2, 0, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(_monthTitle);
            titleRow.Children.Add(caret);
            var titleButton = new FlatButton { Child = titleRow, CornerRadius = Pill, Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, PressScale = 0.98 };
            titleButton.SetBrushes(Brushes.Transparent, _p.Hover, _p.Pressed);
            titleButton.FocusVisualStyle = _focusStyle;
            titleButton.Click += (s, e) => TogglePicker();
            AutomationProperties.SetName(titleButton, "Maand kiezen");
            Place(grid, titleButton, 0, 0);
            FlatButton previous = IconButton(ChevronUp, "Vorige maand", () => ChangeMonth(-1));
            previous.Margin = new Thickness(0, 0, 6, 0);
            Place(grid, previous, 0, 1);
            Place(grid, IconButton(ChevronDown, "Volgende maand", () => ChangeMonth(1)), 0, 2);
            return grid;
        }

        UIElement BuildWeekdayRow()
        {
            Grid grid = EightColumns(1, 28);
            for (int c = 0; c < 8; c++)
            {
                TextBlock label = Text(12, c == 0 ? _p.WeekNumber : _p.Secondary, FontWeights.Bold);
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
            _weekRow = new Border { CornerRadius = new CornerRadius(18), Background = _p.Hover, Margin = new Thickness(0, 2, 0, 2), IsHitTestVisible = false, RenderTransform = _weekRowShift };
            Grid.SetColumnSpan(_weekRow, 8);
            grid.Children.Add(_weekRow);
            _weekRowPlaced = false;
            _daysGrid = grid;
            for (int r = 0; r < MonthGrid.Rows; r++)
            {
                TextBlock week = Text(12, _p.WeekNumber);
                week.HorizontalAlignment = HorizontalAlignment.Center;
                week.VerticalAlignment = VerticalAlignment.Center;
                _weekLabels[r] = week;
                // Klik op het weeknummer kiest de eerste dag van die week binnen deze maand.
                int row = r;
                var weekHit = new Border { Background = Brushes.Transparent, Child = week, Cursor = Cursors.Hand };
                weekHit.MouseLeftButtonUp += (s, e) => SelectWeekRow(row);
                Place(grid, weekHit, r, 0);

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

            // Rustige lege toestand: een agendapictogram met de tekst eronder.
            var emptyIcon = new TextBlock { Text = ((char)0xE787).ToString(), FontFamily = IconFont, FontSize = 30, Foreground = _p.Dimmed, HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock emptyText = Text(13, _p.Secondary);
            emptyText.Text = "Geen afspraken";
            emptyText.HorizontalAlignment = HorizontalAlignment.Center;
            emptyText.Margin = new Thickness(0, 8, 0, 0);
            _empty = new StackPanel { Margin = new Thickness(0, 26, 0, 0), Visibility = Visibility.Collapsed };
            _empty.Children.Add(emptyIcon);
            _empty.Children.Add(emptyText);

            var content = new StackPanel();
            content.Children.Add(_status);
            content.Children.Add(_empty);
            content.Children.Add(_list);

            _scroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
            };
            _scroll.ScrollChanged += (s, e) => UpdateThumb();

            // Dun schuifbalkje, alleen zichtbaar als de lijst niet past.
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

            Place(grid, TextButton("Vandaag", GoToToday, false), 0, 0);

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
                if (_noticeKind == NoticeKind.Error) ClearNotice();
            };
            _weekBox.GotKeyboardFocus += (s, e) => StyleWeekBox(true);
            _weekBox.LostKeyboardFocus += (s, e) => StyleWeekBox(false);

            _weekPlaceholder = Text(12, _p.Dimmed);
            _weekPlaceholder.Text = "Weeknummer";
            _weekPlaceholder.IsHitTestVisible = false;
            _weekPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            _weekPlaceholder.Margin = new Thickness(14, 0, 8, 0);

            var boxContent = new Grid();
            boxContent.Children.Add(_weekPlaceholder);
            boxContent.Children.Add(_weekBox);
            _weekBoxBorder = new Border
            {
                Width = 108,
                Height = 32,
                CornerRadius = Pill,
                Background = _p.FieldFill,
                Child = boxContent,
                Margin = new Thickness(0, 0, 6, 0),
            };
            StyleWeekBox(false);
            Place(grid, _weekBoxBorder, 0, 2);
            Place(grid, TextButton("Ga naar week", GoToWeek, true), 0, 3);
            return grid;
        }

        void StyleWeekBox(bool focused)
        {
            _weekBoxBorder.BorderBrush = focused ? _p.AccentFill : _p.FieldStroke;
            _weekBoxBorder.BorderThickness = new Thickness(focused ? 1.5 : 1);
        }

        static ControlTemplate PlainTextBoxTemplate()
        {
            var template = new ControlTemplate(typeof(TextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 8, 0));
            host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            template.VisualTree = host;
            return template;
        }

        /// <summary>Ronde knop met een pijltje, in de stijl van de pilvormige knoppen.</summary>
        FlatButton IconButton(string glyph, string name, Action click)
        {
            var button = new FlatButton
            {
                Width = 32,
                Height = 32,
                CornerRadius = Pill,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = IconFont,
                    FontSize = 11,
                    Foreground = _p.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            button.SetBrushes(_p.ControlFill, _p.ControlHover, _p.ControlPressed);
            button.FocusVisualStyle = _focusStyle;
            button.Click += (s, e) => click();
            AutomationProperties.SetName(button, name);
            return button;
        }

        /// <summary>Pilvormige knop: rood met wit voor de hoofdactie, zand voor de rest.</summary>
        FlatButton TextButton(string label, Action click, bool primary)
        {
            TextBlock text = Text(13, primary ? _p.OnAccent : _p.Text, FontWeights.Bold);
            text.Text = label;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            var button = new FlatButton
            {
                Height = 32,
                CornerRadius = Pill,
                Padding = new Thickness(14, 0, 14, 0),
                Child = text,
            };
            if (primary) button.SetBrushes(_p.AccentFill, _p.AccentHover, _p.AccentPressed);
            else button.SetBrushes(_p.ControlFill, _p.ControlHover, _p.ControlPressed);
            button.FocusVisualStyle = _focusStyle;
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
            // Zelfde grenzen als het weekveld en het selecteren van een dag.
            if (grid.Year < WeekInput.MinYear || grid.Year > WeekInput.MaxYear) return;
            // In de maand van vandaag is vandaag geselecteerd, anders de 1e.
            DateTime select = grid.IsInMonth(_today) ? _today : new DateTime(grid.Year, grid.Month, 1);
            ShowMonth(grid, select);
        }

        void Select(DateTime date)
        {
            if (date.Year < WeekInput.MinYear || date.Year > WeekInput.MaxYear) return;
            if (_grid != null && _grid.IsInMonth(date))
            {
                bool changed = _selected != date.Date;
                _selected = date.Date;
                Render();
                if (changed && _grid.Contains(date)) _cells[(int)(date.Date - _grid.FirstCell).TotalDays].Pop();
            }
            else
            {
                ShowMonth(MonthGrid.For(date), date);
            }
        }

        void SelectWeekRow(int row)
        {
            for (int c = 0; c < 7; c++)
            {
                DateTime day = _grid.CellDate(row, c);
                if (_grid.IsInMonth(day))
                {
                    Select(day);
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- maandkiezer

        void TogglePicker()
        {
            if (_picker.Visibility == Visibility.Visible) HidePicker();
            else ShowPicker(_grid.Year);
        }

        void HidePicker()
        {
            if (_picker != null) _picker.Visibility = Visibility.Collapsed;
        }

        void ShowPicker(int year)
        {
            _pickerYear = Math.Max(WeekInput.MinYear, Math.Min(WeekInput.MaxYear, year));
            _picker.Children.Clear();
            _picker.RowDefinitions.Clear();
            _picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _picker.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock yearText = Text(18, _p.Text, FontWeights.Bold);
            yearText.Text = _pickerYear.ToString(CultureInfo.InvariantCulture);
            yearText.HorizontalAlignment = HorizontalAlignment.Center;
            yearText.VerticalAlignment = VerticalAlignment.Center;
            Place(header, IconButton(((char)0xE76B).ToString(), "Vorig jaar", () => ShowPicker(_pickerYear - 1)), 0, 0);
            Place(header, yearText, 0, 1);
            Place(header, IconButton(((char)0xE76C).ToString(), "Volgend jaar", () => ShowPicker(_pickerYear + 1)), 0, 2);
            Place(_picker, header, 0);

            var months = new Grid();
            for (int c = 0; c < 3; c++) months.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < 4; r++) months.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (int m = 1; m <= 12; m++)
            {
                int month = m;
                bool shown = _pickerYear == _grid.Year && month == _grid.Month;
                bool current = _pickerYear == _today.Year && month == _today.Month;
                TextBlock label = Text(14, shown ? _p.OnAccent : current ? _p.AccentText : _p.Text, shown || current ? FontWeights.Bold : FontWeights.Normal);
                label.Text = DutchText.MonthShort(month);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                var button = new FlatButton { Child = label, CornerRadius = Pill, Margin = new Thickness(5, 7, 5, 7), FocusVisualStyle = _focusStyle };
                if (shown) button.SetBrushes(_p.AccentFill, _p.AccentHover, _p.AccentPressed);
                else button.SetBrushes(_p.Card, _p.ControlHover, _p.ControlPressed);
                button.Click += (s, e) =>
                {
                    HidePicker();
                    var grid = new MonthGrid(_pickerYear, month);
                    ShowMonth(grid, grid.IsInMonth(_today) ? _today : new DateTime(_pickerYear, month, 1));
                };
                AutomationProperties.SetName(button, DutchText.MonthYear(_pickerYear, month));
                Place(months, button, (month - 1) / 3, (month - 1) % 3);
                if (IsVisible) Motion.SlideIn(button, 8, 150, (month - 1) / 3 * 25);
            }
            Place(_picker, months, 1);
            _picker.Visibility = Visibility.Visible;
        }

        void GoToToday()
        {
            _today = DateTime.Today;
            ClearNotice();
            HidePicker();
            ShowMonth(MonthGrid.For(_today), _today);
        }

        void GoToWeek()
        {
            int defaultYear = _grid != null ? _grid.Year : _today.Year;
            WeekInputResult input = WeekInput.Parse(_weekBox.Text, defaultYear);
            if (!input.Ok)
            {
                ShowNotice(input.Error, NoticeKind.Error);
                _weekBox.Focus();
                _weekBox.SelectAll();
                return;
            }
            ClearNotice();
            DateTime monday = IsoWeek.FirstDayOfWeek(input.Year, input.Week);
            ShowMonth(MonthGrid.For(monday), monday);
        }

        void ShowMonth(MonthGrid grid, DateTime select)
        {
            bool sameGrid = _grid != null && _grid.FirstCell == grid.FirstCell && _grid.Month == grid.Month;
            int direction = _grid == null || sameGrid ? 0 : Math.Sign(grid.FirstCell.CompareTo(_grid.FirstCell));
            _grid = grid;
            _selected = select.Date;
            if (sameGrid)
            {
                Render();
                return;
            }
            // Nieuwe maand: de balk staat meteen goed, de dagen schuiven in vanuit de bladerrichting.
            _weekRowPlaced = false;
            LoadAppointments();
            if (direction != 0 && IsVisible) Motion.SlideIn(_daysGrid, direction * 16, 170);
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
            if (e.Key == Key.Escape && _picker.Visibility == Visibility.Visible)
            {
                HidePicker();
                e.Handled = true;
                return;
            }
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
                case Key.T: GoToToday(); break;
                case Key.M: TogglePicker(); break;
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
                    UpdateLoadNotice(cached, false);
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
            // Mislukt verversen terwijl er al (oudere) afspraken staan: die laten staan en het melden.
            bool keptOld = !result.IsOk && _result != null && _result.IsOk;
            if (!keptOld) _result = result;
            _loading = false;
            Render();
            UpdateLoadNotice(result, keptOld);
            if (result.IsOk) Prefetch(grid);
        }

        /// <summary>Zet of wist de melding over het laden. Een melding over een actie van de gebruiker blijft staan.</summary>
        void UpdateLoadNotice(ProviderResult result, bool keptOld)
        {
            bool free = _noticeKind == NoticeKind.None || _noticeKind == NoticeKind.Syncing || _noticeKind == NoticeKind.RefreshFailed;
            if (result.IsOk && result.Provisional)
            {
                if (free) ShowNotice(SyncingText, NoticeKind.Syncing);
                _syncRefreshTimer.Stop();
                _syncRefreshTimer.Start();
            }
            else if (keptOld)
            {
                if (free) ShowNotice("Niet bijgewerkt. " + result.Message, NoticeKind.RefreshFailed);
            }
            else if (_noticeKind == NoticeKind.Syncing || _noticeKind == NoticeKind.RefreshFailed)
            {
                ClearNotice();
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
            ShowNotice("Afspraak wordt geopend in Outlook…", NoticeKind.Info);
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
                ClearNotice();
                HidePopup();
            }
            else
            {
                ShowNotice(result.Message, NoticeKind.Error);
            }
        }

        // ---------------------------------------------------------------- tekenen

        void Render()
        {
            if (_grid == null) return;
            _monthTitle.Text = DutchText.Capitalize(DutchText.MonthYear(_grid.Year, _grid.Month));
            _busyDays = _result != null && _result.IsOk ? AppointmentDay.CountsPerDay(_result.Items, _grid.FirstCell, _grid.LastCell) : NoCounts;

            _titleWeek.Text = "week " + IsoWeek.GetWeek(_today).ToString(CultureInfo.InvariantCulture);
            int selectedRow = _grid.RowOf(_selected);
            _titleDate.Text = DutchText.DayTitle(_today, _today);
            _weekRow.Visibility = selectedRow >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (selectedRow >= 0)
            {
                // De balk glijdt naar de nieuwe week in plaats van te verspringen.
                double y = selectedRow * 40;
                if (_weekRowPlaced && IsVisible && Motion.Enabled) _weekRowShift.BeginAnimation(TranslateTransform.YProperty, Motion.To(y, 190));
                else
                {
                    _weekRowShift.BeginAnimation(TranslateTransform.YProperty, null);
                    _weekRowShift.Y = y;
                }
                _weekRowPlaced = true;
            }
            for (int r = 0; r < MonthGrid.Rows; r++)
            {
                TextBlock label = _weekLabels[r];
                label.Text = _grid.WeekNumber(r).ToString(CultureInfo.InvariantCulture);
                label.Foreground = r == selectedRow ? _p.AccentText : _p.WeekNumber;
                label.FontWeight = r == selectedRow ? FontWeights.Bold : FontWeights.Normal;
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
            cell.Label.FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal;
            int count;
            _busyDays.TryGetValue(date, out count);
            bool busy = count > 0;
            cell.SetDots(count, isToday ? _p.OnAccent : inMonth ? _p.AccentFill : _p.Dimmed);
            AutomationProperties.SetName(cell, DutchText.LongDate(date) + (busy ? ", met afspraken" : ""));
        }

        void RenderDay()
        {
            _dayTitle.Text = DutchText.Capitalize(DutchText.DayTitle(_selected, _today));
            _dayWeek.Text = "week " + IsoWeek.GetWeek(_selected).ToString(CultureInfo.InvariantCulture);
            _list.Children.Clear();
            bool empty = false;

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
                SetStatus(null);
                empty = items.Count == 0;
                // Alleen invloeien als er echt iets anders te zien is (andere dag of nieuwe gegevens), niet bij elke verversing.
                bool animate = IsVisible && (_listDay != _selected || !ReferenceEquals(_listResult, _result));
                int index = 0;
                foreach (Appointment appointment in items)
                {
                    var card = (FrameworkElement)BuildItem(appointment);
                    _list.Children.Add(card);
                    if (animate && index < 8) Motion.SlideIn(card, 10, 170, index * 35);
                    index++;
                }
                if (animate && empty) Motion.SlideIn(_empty, 8, 200);
            }
            _empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _listDay = _selected;
            _listResult = _result;
            _scroll.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateThumb));
        }

        /// <summary>Eén afspraak als kaartje met een rode streep.</summary>
        UIElement BuildItem(Appointment appointment)
        {
            var bar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = _p.AccentFill,
                Margin = new Thickness(0, 1, 10, 1),
            };
            DockPanel.SetDock(bar, Dock.Left);

            var lines = new StackPanel();
            TextBlock time = Text(12, _p.Secondary);
            time.Text = DutchText.TimeRange(appointment, _selected);
            DateTime now = DateTime.Now;
            if (!appointment.AllDay && _selected == _today && appointment.Start <= now && now < appointment.End)
            {
                time.Text = "Nu bezig · " + time.Text;
                time.Foreground = _p.AccentText;
                time.FontWeight = FontWeights.Bold;
            }
            else if (!appointment.AllDay && _selected == _today)
            {
                // Begint binnen drie uur: dat staat ervoor, zodat je niet hoeft te rekenen.
                string soon = DutchText.StartsIn(appointment.Start, now);
                if (soon != null)
                {
                    time.Text = soon + " · " + time.Text;
                    time.Foreground = _p.AccentText;
                }
            }
            lines.Children.Add(time);
            TextBlock subject = Text(14, _p.Text, FontWeights.Bold);
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

            var button = new FlatButton
            {
                Child = row,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
            };
            button.SetBrushes(_p.Card, _p.CardHover, _p.Pressed);
            button.FocusVisualStyle = _focusStyle;
            button.PressScale = 0.985;
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

        void ShowNotice(string text, NoticeKind kind)
        {
            _noticeKind = kind;
            _notice.Text = text ?? "";
            _notice.Foreground = kind == NoticeKind.Error || kind == NoticeKind.RefreshFailed ? _p.Error : _p.Secondary;
            _notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void ClearNotice()
        {
            _noticeKind = NoticeKind.None;
            _notice.Text = "";
            _notice.Visibility = Visibility.Collapsed;
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
