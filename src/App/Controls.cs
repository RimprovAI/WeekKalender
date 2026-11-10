using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WeekKalender.App
{
    /// <summary>Lichte knop zonder WPF-standaardstijl: achtergrond voor rust, hover en ingedrukt.</summary>
    internal sealed class FlatButton : Border
    {
        Brush _normal = Brushes.Transparent;
        Brush _hover = Brushes.Transparent;
        Brush _pressed = Brushes.Transparent;
        bool _interactive = true;

        public FlatButton()
        {
            CornerRadius = new CornerRadius(4);
            Focusable = true;
            Background = _normal;
            MouseEnter += (s, e) => { if (_interactive) Background = _hover; };
            MouseLeave += (s, e) => { if (!IsMouseCaptured) Background = _normal; };
            MouseLeftButtonDown += OnDown;
            MouseLeftButtonUp += OnUp;
            KeyDown += OnKey;
        }

        public event EventHandler Click;

        public bool Interactive
        {
            get { return _interactive; }
            set
            {
                _interactive = value;
                Focusable = value;
                Background = _normal;
            }
        }

        public void SetBrushes(Brush normal, Brush hover, Brush pressed)
        {
            _normal = normal;
            _hover = hover;
            _pressed = pressed;
            Background = IsMouseOver && _interactive ? hover : normal;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!_interactive) return;
            Background = _pressed;
            CaptureMouse();
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_interactive || !IsMouseCaptured) return;
            ReleaseMouseCapture();
            Point p = e.GetPosition(this);
            bool inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
            Background = inside ? _hover : _normal;
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
        public readonly Ellipse Circle = new Ellipse { Width = 34, Height = 34, StrokeThickness = 1.5 };
        public readonly Ellipse InnerRing = new Ellipse { Width = 29, Height = 29, StrokeThickness = 1.5 };
        public readonly TextBlock Label = new TextBlock
        {
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
        };
        public readonly Ellipse Dot = new Ellipse
        {
            Width = 5,
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
        };

        public DayCell()
        {
            Width = 40;
            Height = 40;
            Background = Brushes.Transparent;   // zodat de hele cel klikbaar is
            Children.Add(Circle);
            Children.Add(InnerRing);
            Children.Add(Label);
            Children.Add(Dot);
        }

        public DateTime Date { get; set; }

        /// <summary>Selecteren via een schermlezer (zelfde als klikken).</summary>
        public event EventHandler Invoked;

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new InvokablePeer(this, () => { var handler = Invoked; if (handler != null) handler(this, EventArgs.Empty); }, () => true);
        }
    }
}
