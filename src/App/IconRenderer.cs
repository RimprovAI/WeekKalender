using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

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
            // Minstens één vrije pixelkolom, zodat geen cijfer tegen de rand wordt afgesneden.
            int maxWidth = size - Math.Max(1, size / 16);
            int maxHeight = (int)Math.Round(size * 0.75);
            Color color = lightTaskbar ? Color.FromArgb(255, 0, 0, 0) : Color.White;

            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var family = PickFamily())
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                // Eerste schatting uit de letteromtrek. Windows lijnt kleine tekst uit op hele pixels en tekent hem
                // daardoor net iets breder, dus daarna meten we wat er echt staat en verkleinen tot het past.
                RectangleF outline = OutlineBounds(text, family, 100f, format);
                float em = 100f * Math.Min(maxWidth / outline.Width, maxHeight / outline.Height);
                Rectangle ink = MeasureInk(text, family, em, format, size);
                for (int attempt = 0; attempt < 12 && !ink.IsEmpty && (ink.Width > maxWidth || ink.Height > maxHeight); attempt++)
                {
                    float fit = Math.Min((float)maxWidth / ink.Width, (float)maxHeight / ink.Height);
                    em *= Math.Min(fit, 0.97f);
                    ink = MeasureInk(text, family, em, format, size);
                }

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    Prepare(g);
                    // Hele pixels verschuiven ten opzichte van de meting geeft exact hetzelfde beeld, nu gecentreerd.
                    int x = size - ink.Left + (size - ink.Width) / 2;
                    int y = size - ink.Top + (size - ink.Height) / 2;
                    Draw(g, text, family, em, format, color, x, y);
                }
            }
            return bitmap;
        }

        /// <summary>Tekent de tekst op een ruim hulpvlak (op punt size,size) en geeft terug welke pixels echt gekleurd zijn.</summary>
        static Rectangle MeasureInk(string text, FontFamily family, float em, StringFormat format, int size)
        {
            int side = size * 3;
            using (var scratch = new Bitmap(side, side, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(scratch))
                {
                    Prepare(g);
                    Draw(g, text, family, em, format, Color.White, size, size);
                }

                BitmapData data = scratch.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = Math.Abs(data.Stride) / 4;
                    var pixels = new int[stride * side];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    int minX = side, minY = side, maxX = -1, maxY = -1;
                    for (int y = 0; y < side; y++)
                    {
                        for (int x = 0; x < side; x++)
                        {
                            if ((pixels[y * stride + x] >> 24) == 0) continue;   // volledig doorzichtig
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                    return maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                }
                finally
                {
                    scratch.UnlockBits(data);
                }
            }
        }

        static void Prepare(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        static void Draw(Graphics g, string text, FontFamily family, float em, StringFormat format, Color color, int x, int y)
        {
            using (var font = new Font(family, em, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            {
                g.DrawString(text, font, brush, new PointF(x, y), format);
            }
        }

        static RectangleF OutlineBounds(string text, FontFamily family, float emPixels, StringFormat format)
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
