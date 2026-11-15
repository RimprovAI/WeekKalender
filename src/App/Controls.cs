using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WeekKalender.App
{
    /// <summary>Korte, zachte bewegingen. Staan Windows-animaties uit, dan gebeurt alles direct.</summary>
    internal static class Motion
    {
        static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

        public static bool Enabled
        {
            get { return SystemParameters.ClientAreaAnimation; }
        }

        public static DoubleAnimation To(double to, int milliseconds, int delay = 0)
        {
            return new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut, BeginTime = TimeSpan.FromMilliseconds(delay) };
        }

        public static DoubleAnimation FromTo(double from, double to, int milliseconds, int delay = 0)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut, BeginTime = TimeSpan.FromMilliseconds(delay) };
        }

        /// <summary>Laat een element invloeien: van doorzichtig en iets verschoven naar zijn plek.</summary>
        public static void SlideIn(FrameworkElement element, double offsetY, int milliseconds, int delay = 0)
        {
            if (!Enabled) return;
            var shift = new TranslateTransform(0, offsetY);
            element.RenderTransform = shift;
            element.Opacity = 0;
            element.BeginAnimation(UIElement.OpacityProperty, FromTo(0, 1, milliseconds, delay));
            shift.BeginAnimation(TranslateTransform.YProperty, FromTo(offsetY, 0, milliseconds, delay));
        }

        static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }

    /// <summary>Lichte knop zonder WPF-standaardstijl: kleur vloeit over bij aanwijzen, en hij veert licht in bij indrukken.</summary>
    internal sealed class FlatButton : Border
    {
        readonly SolidColorBrush _fill = new SolidColorBrush(Colors.Transparent);
        readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        Color _normal, _hover, _pressed;
        bool _interactive = true;

        public FlatButton()
        {
            CornerRadius = new CornerRadius(4);
            Focusable = true;
            Background = _fill;
            RenderTransform = _scale;
            RenderTransformOrigin = new Point(0.5, 0.5);
            MouseEnter += (s, e) => { if (_interactive) Fill(_hover, true); };
            MouseLeave += (s, e) => { if (!IsMouseCaptured) Fill(_normal, true); };
            MouseLeftButtonDown += OnDown;
            MouseLeftButtonUp += OnUp;
            KeyDown += OnKey;
        }

        public event EventHandler Click;

        /// <summary>Hoe ver de knop inveert bij indrukken (1 = niet).</summary>
        public double PressScale = 0.96;

        public bool Interactive
        {
            get { return _interactive; }
            set
            {
                _interactive = value;
                Focusable = value;
                Fill(_normal, false);
            }
        }

        public void SetBrushes(Brush normal, Brush hover, Brush pressed)
        {
            _hover = ColorOf(hover);
            _pressed = ColorOf(pressed);
            _normal = ColorOf(normal);
            // Doorzichtig als rustkleur: dezelfde tint zonder dekking, zodat de overgang niet via wit loopt.
            if (_normal.A == 0) _normal = Color.FromArgb(0, _hover.R, _hover.G, _hover.B);
            Fill(IsMouseOver && _interactive ? _hover : _normal, false);
        }

        static Color ColorOf(Brush brush)
        {
            var solid = brush as SolidColorBrush;
            return solid != null ? solid.Color : Colors.Transparent;
        }

        void Fill(Color color, bool animate)
        {
            if (animate && Motion.Enabled)
            {
                _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(110)));
            }
            else
            {
                _fill.BeginAnimation(SolidColorBrush.ColorProperty, null);
                _fill.Color = color;
            }
        }

        void Press(bool down)
        {
            double to = down ? PressScale : 1;
            if (Motion.Enabled)
            {
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(to, down ? 60 : 140));
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(to, down ? 60 : 140));
            }
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!_interactive) return;
            Fill(_pressed, true);
            Press(true);
            CaptureMouse();
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_interactive || !IsMouseCaptured) return;
            ReleaseMouseCapture();
            Point p = e.GetPosition(this);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
            Fill(inside ? _hover : _normal, true);
            Press(false);
            e.Handled = true;
            if (inside) RaiseClick();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (_interactive && (e.Key == Key.Enter || e.Key == Key.Space))
            {
                e.Handled = true;
                RaiseClick();
            }
        }

        internal void RaiseClick()
        {
            var handler = Click;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new InvokablePeer(this, () => { if (_interactive) RaiseClick(); }, () => _interactive);
        }
    }

    /// <summary>Maakt eigen knoppen en dagcellen zichtbaar en bedienbaar voor schermlezers (UI Automation).</summary>
    internal sealed class InvokablePeer : FrameworkElementAutomationPeer, IInvokeProvider
    {
        readonly Action _invoke;
        readonly Func<bool> _enabled;

        public InvokablePeer(FrameworkElement owner, Action invoke, Func<bool> enabled) : base(owner)
        {
            _invoke = invoke;
            _enabled = enabled;
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return _enabled() ? AutomationControlType.Button : AutomationControlType.ListItem;
        }

        protected override string GetClassNameCore()
        {
            return Owner.GetType().Name;
        }

        public override object GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.Invoke && _enabled()) return this;
            return base.GetPattern(patternInterface);
        }

        void IInvokeProvider.Invoke()
        {
            _invoke();
        }
    }

    /// <summary>Eén dag in het maandraster: cirkel (vandaag/selectie/hover), dagnummer en een stipje bij afspraken.</summary>
    internal sealed class DayCell : Grid
    {
        readonly ScaleTransform _pop = new ScaleTransform(1, 1);

        public readonly Ellipse Circle = new Ellipse { Width = 34, Height = 34, StrokeThickness = 1.5 };
        public readonly Ellipse InnerRing = new Ellipse { Width = 29, Height = 29, StrokeThickness = 1.5 };
        public readonly TextBlock Label = new TextBlock
        {
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        };
        readonly Ellipse[] _dots = new Ellipse[3];
        readonly StackPanel _dotRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };

        public DayCell()
        {
            Width = 40;
            Height = 40;
            Background = Brushes.Transparent;   // zodat de hele cel klikbaar is
            Circle.RenderTransform = _pop;
            Circle.RenderTransformOrigin = new Point(0.5, 0.5);
            Children.Add(Circle);
            Children.Add(InnerRing);
            Children.Add(Label);
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i] = new Ellipse { Width = 4, Height = 4, Margin = new Thickness(1, 0, 1, 0) };
                _dotRow.Children.Add(_dots[i]);
            }
            Children.Add(_dotRow);
        }

        public DateTime Date { get; set; }

        /// <summary>Selecteren via een schermlezer (zelfde als klikken).</summary>
        public event EventHandler Invoked;

        /// <summary>Eén, twee of drie stipjes: hoe voller de dag, hoe meer (drie = drie of meer afspraken).</summary>
        public void SetDots(int count, Brush fill)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                _dots[i].Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
                _dots[i].Fill = fill;
            }
        }

        /// <summary>De cirkel veert kort op als de dag net geselecteerd is.</summary>
        public void Pop()
        {
            if (!Motion.Enabled) return;
            _pop.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.FromTo(0.72, 1, 180));
            _pop.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.FromTo(0.72, 1, 180));
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new InvokablePeer(this, () => { var handler = Invoked; if (handler != null) handler(this, EventArgs.Empty); }, () => true);
        }
    }
}
