using System;
using System.Windows.Media;
using Microsoft.Win32;

namespace WeekKalender.App
{
    /// <summary>Licht/donker en accentkleur zoals Windows ze voor taakbalk en flyouts gebruikt.</summary>
    internal sealed class ThemeInfo
    {
        public bool SystemLight { get; private set; }
        public bool Transparency { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentLight2 { get; private set; }
        public Color AccentDark1 { get; private set; }

        public static ThemeInfo Read()
        {
            ThemeInfo theme = ReadSystem();
            ApplyOverride(theme);
            return theme;
        }

        static ThemeInfo ReadSystem()
        {
            var theme = new ThemeInfo { Transparency = true, Accent = Color.FromRgb(0x00, 0x78, 0xD4) };
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        theme.SystemLight = ReadInt(key, "SystemUsesLightTheme", 0) == 1;
                        theme.Transparency = ReadInt(key, "EnableTransparency", 1) == 1;
                    }
                }

                // AccentPalette: 8 kleuren (licht3, licht2, licht1, accent, donker1, donker2, donker3, …) van 4 bytes RGBA.
                var palette = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) as byte[];
                if (palette != null && palette.Length >= 32)
                {
                    theme.AccentLight2 = FromPalette(palette, 1);
                    theme.Accent = FromPalette(palette, 3);
                    theme.AccentDark1 = FromPalette(palette, 4);
                    return theme;
                }

                uint color;
                bool opaque;
                if (NativeMethods.DwmGetColorizationColor(out color, out opaque) == 0)
                    theme.Accent = Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
            }
            catch (Exception)
            {
                // Standaardwaarden houden.
            }
            theme.AccentLight2 = Mix(theme.Accent, Colors.White, 0.5);
            theme.AccentDark1 = Mix(theme.Accent, Colors.Black, 0.2);
            return theme;
        }

        /// <summary>Voor het testen: WEEKKALENDER_THEMA=licht of donker overschrijft de Windows-instelling.</summary>
        static void ApplyOverride(ThemeInfo theme)
        {
            string value = Environment.GetEnvironmentVariable("WEEKKALENDER_THEMA");
            if (string.Equals(value, "licht", StringComparison.OrdinalIgnoreCase)) theme.SystemLight = true;
            else if (string.Equals(value, "donker", StringComparison.OrdinalIgnoreCase)) theme.SystemLight = false;
        }

        public bool SameAs(ThemeInfo other)
        {
            return other != null && other.SystemLight == SystemLight && other.Transparency == Transparency &&
                   other.Accent == Accent && other.AccentLight2 == AccentLight2 && other.AccentDark1 == AccentDark1;
        }

        static int ReadInt(RegistryKey key, string name, int fallback)
        {
            object value = key.GetValue(name);
            return value is int ? (int)value : fallback;
        }

        static Color FromPalette(byte[] palette, int index)
        {
            return Color.FromRgb(palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
        }

        static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * amount),
                (byte)(a.G + (b.G - a.G) * amount),
                (byte)(a.B + (b.B - a.B) * amount));
        }
    }

    /// <summary>Kleuren naar de Windows 11-richtlijnen (Fluent), voor licht en donker.</summary>
    internal sealed class Palette
    {
        public Brush Text, Secondary, Dimmed, Hover, Pressed, Separator;
        public Brush AccentFill, OnAccent, AccentText;
        public Brush ControlFill, ControlHover, ControlPressed, ControlStroke;
        public Brush Error, Solid;
        public Color SolidColor;

        public static Palette Create(ThemeInfo theme)
        {
            bool dark = !theme.SystemLight;
            var p = new Palette();
            p.Text = Brush(dark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE4, 0x00, 0x00, 0x00));
            p.Secondary = Brush(dark ? Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9E, 0x00, 0x00, 0x00));
            p.Dimmed = Brush(dark ? Color.FromArgb(0x5D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x5C, 0x00, 0x00, 0x00));
            p.Hover = Brush(dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x09, 0x00, 0x00, 0x00));
            p.Pressed = Brush(dark ? Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x06, 0x00, 0x00, 0x00));
            p.Separator = Brush(dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
            p.AccentFill = Brush(dark ? theme.AccentLight2 : theme.AccentDark1);
            p.OnAccent = Brush(dark ? Colors.Black : Colors.White);
            p.AccentText = Brush(dark ? theme.AccentLight2 : theme.AccentDark1);
            p.ControlFill = Brush(dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            p.ControlHover = Brush(dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0xF9, 0xF9, 0xF9));
            p.ControlPressed = Brush(dark ? Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x4D, 0xF9, 0xF9, 0xF9));
            p.ControlStroke = Brush(dark ? Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
            p.Error = Brush(dark ? Color.FromRgb(0xFF, 0x99, 0xA4) : Color.FromRgb(0xC4, 0x2B, 0x1C));
            p.SolidColor = dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9);
            p.Solid = Brush(p.SolidColor);
            return p;
        }

        static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
