using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace WeekKalender.App
{
    /// <summary>Tekent het weeknummer als icoon op de exacte pixelgrootte van het systeemvak.</summary>
    internal static class IconRenderer
    {
        public static IntPtr Create(int week, int size, bool lightTaskbar)
        {
            using (Bitmap bitmap = Render(week, size, lightTaskbar))
            {
                return bitmap.GetHicon();
            }
        }

        public static Bitmap Render(int week, int size, bool lightTaskbar)
        {
            string text = week.ToString(CultureInfo.InvariantCulture);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var family = PickFamily())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                // Lettergrootte zo kiezen dat het getal de breedte vult maar niet te hoog wordt.
                const float probe = 100f;
                RectangleF ink = InkBounds(text, family, probe, format);
                float maxWidth = size * (text.Length > 1 ? 1.0f : 0.62f);
                float maxHeight = size * 0.80f;
                float em = probe * Math.Min(maxWidth / ink.Width, maxHeight / ink.Height);

                ink = InkBounds(text, family, em, format);
                float x = (float)Math.Round((size - ink.Width) / 2f - ink.X);
                float y = (float)Math.Round((size - ink.Height) / 2f - ink.Y);

                Color color = lightTaskbar ? Color.FromArgb(255, 0, 0, 0) : Color.White;
                using (var font = new Font(family, em, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(color))
                {
                    g.DrawString(text, font, brush, new PointF(x, y), format);
                }
            }
            return bitmap;
        }

        static RectangleF InkBounds(string text, FontFamily family, float emPixels, StringFormat format)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(text, family, (int)FontStyle.Bold, emPixels, PointF.Empty, format);
                return path.GetBounds();
            }
        }

        static FontFamily PickFamily()
        {
            try
            {
                return new FontFamily("Segoe UI");
            }
            catch (ArgumentException)
            {
                return new FontFamily(GenericFontFamilies.SansSerif);
            }
        }
    }
}
