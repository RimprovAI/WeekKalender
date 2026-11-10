using System;
using System.Windows.Media;
using Microsoft.Win32;
using WeekKalender.Core;

namespace WeekKalender.App
{
    /// <summary>Licht of donker: voor de taakbalk (kleur van het tray-icoon en het menu) en voor de popup.</summary>
    internal sealed class ThemeInfo
    {
        /// <summary>Windows gebruikt een lichte taakbalk.</summary>
        public bool SystemLight { get; private set; }

        /// <summary>De popup is licht: volgt Windows, tenzij de instelling Thema iets anders zegt.</summary>
        public bool PopupLight { get; private set; }

        public static ThemeInfo Read(string themeSetting)
        {
            var theme = new ThemeInfo();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    theme.SystemLight = value is int && (int)value == 1;
                }
            }
            catch (Exception)
            {
                // Donker als standaard houden.
            }

            if (themeSetting == AppSettings.ThemeLight) theme.PopupLight = true;
            else if (themeSetting == AppSettings.ThemeDark) theme.PopupLight = false;
            else theme.PopupLight = theme.SystemLight;
            return theme;
        }

        public bool SameAs(ThemeInfo other)
        {
            return other != null && other.SystemLight == SystemLight && other.PopupLight == PopupLight;
        }
    }

    /// <summary>
    /// Kleuren van de huisstijl: merkrood met wit, crème en zand in de lichte variant,
    /// warm zwart met crème tekst in de donkere variant.
    /// </summary>
    internal sealed class Palette
    {
        // Merkkleuren.
        static readonly Color Red = Color.FromRgb(0xE6, 0x22, 0x49);          // primary-400
        static readonly Color RedDark = Color.FromRgb(0xCA, 0x05, 0x38);      // primary-500
        static readonly Color RedDeep = Color.FromRgb(0xA9, 0x04, 0x2F);
        static readonly Color RedSoft = Color.FromRgb(0xEA, 0x60, 0x79);      // rood dat op donker leesbaar blijft
        static readonly Color OffWhite = Color.FromRgb(0xF5, 0xF1, 0xE8);     // secondary-off-white
        static readonly Color Sand = Color.FromRgb(0xDA, 0xD6, 0xCD);         // surface-100
        static readonly Color WarmBlack = Color.FromRgb(0x26, 0x24, 0x1D);

        public bool Dark;
        public Color SolidColor;
        public Brush Solid, Text, Secondary, WeekNumber, Dimmed, Hover, Pressed, Separator;
        public Brush AccentFill, AccentHover, AccentPressed, OnAccent, AccentText;
        public Brush Card, CardHover, ControlFill, ControlHover, ControlPressed, FieldFill, FieldStroke, Error;

        public static Palette Create(bool light)
        {
            var p = new Palette { Dark = !light };
            p.AccentFill = Brush(Red);
            p.AccentHover = Brush(RedDark);
            p.AccentPressed = Brush(RedDeep);
            p.OnAccent = Brush(Colors.White);

            if (light)
            {
                p.SolidColor = OffWhite;
                p.Text = Brush(Color.FromRgb(0x11, 0x11, 0x11));
                p.Secondary = Brush(Color.FromRgb(0x55, 0x51, 0x4A));
                p.WeekNumber = Brush(Color.FromRgb(0x6D, 0x6A, 0x62));
                p.Dimmed = Brush(Color.FromRgb(0xA8, 0xA3, 0x9A));
                p.Hover = Brush(WithAlpha(Sand, 0x99));
                p.Pressed = Brush(Sand);
                p.Separator = Brush(Sand);
                p.AccentText = Brush(RedDark);
                p.Card = Brush(Colors.White);
                p.CardHover = Brush(Color.FromRgb(0xEF, 0xEC, 0xE4));
                p.ControlFill = Brush(Sand);
                p.ControlHover = Brush(Color.FromRgb(0xCD, 0xC8, 0xBD));
                p.ControlPressed = Brush(Color.FromRgb(0xC2, 0xBD, 0xB1));
                p.FieldFill = Brush(Colors.White);
                p.FieldStroke = Brush(Sand);
                p.Error = Brush(RedDark);
            }
            else
            {
                p.SolidColor = WarmBlack;
                p.Text = Brush(OffWhite);
                p.Secondary = Brush(WithAlpha(OffWhite, 0xCC));
                p.WeekNumber = Brush(WithAlpha(OffWhite, 0x99));
                p.Dimmed = Brush(WithAlpha(OffWhite, 0x59));
                p.Hover = Brush(WithAlpha(OffWhite, 0x1F));
                p.Pressed = Brush(WithAlpha(OffWhite, 0x2E));
                p.Separator = Brush(WithAlpha(OffWhite, 0x24));
                p.AccentText = Brush(RedSoft);
                p.Card = Brush(WithAlpha(OffWhite, 0x14));
                p.CardHover = Brush(WithAlpha(OffWhite, 0x24));
                p.ControlFill = Brush(WithAlpha(OffWhite, 0x26));
                p.ControlHover = Brush(WithAlpha(OffWhite, 0x38));
                p.ControlPressed = Brush(WithAlpha(OffWhite, 0x1A));
                p.FieldFill = Brush(WithAlpha(OffWhite, 0x14));
                p.FieldStroke = Brush(WithAlpha(OffWhite, 0x40));
                p.Error = Brush(RedSoft);
            }
            p.Solid = Brush(p.SolidColor);
            return p;
        }

        static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
