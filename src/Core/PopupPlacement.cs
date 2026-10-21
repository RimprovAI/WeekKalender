using System;

namespace WeekKalender.Core
{
    public struct RectI
    {
        public int Left, Top, Right, Bottom;

        public RectI(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Width
        {
            get { return Right - Left; }
        }

        public int Height
        {
            get { return Bottom - Top; }
        }

        public bool Intersects(RectI other)
        {
            return Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
        }

        public override string ToString()
        {
            return "(" + Left + "," + Top + ")-(" + Right + "," + Bottom + ")";
        }
    }

    public struct PointI
    {
        public int X, Y;

        public PointI(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public enum TaskbarEdge
    {
        Bottom,
        Top,
        Left,
        Right
    }

    /// <summary>Plaatsing van de popup in schermpixels, net als de Windows-agenda: in de hoek bij de klok, binnen het werkgebied.</summary>
    public static class PopupPlacement
    {
        /// <summary>
        /// Bepaalt aan welke kant de taakbalk zit en welk deel van het scherm vrij is.
        /// Als het werkgebied het hele scherm is (taakbalk automatisch verbergen), wordt de taakbalkpositie gebruikt.
        /// </summary>
        public static RectI EffectiveArea(RectI monitor, RectI work, RectI? taskbar, out TaskbarEdge edge)
        {
            if (work.Bottom < monitor.Bottom) { edge = TaskbarEdge.Bottom; return work; }
            if (work.Top > monitor.Top) { edge = TaskbarEdge.Top; return work; }
            if (work.Left > monitor.Left) { edge = TaskbarEdge.Left; return work; }
            if (work.Right < monitor.Right) { edge = TaskbarEdge.Right; return work; }

            edge = TaskbarEdge.Bottom;
            if (!taskbar.HasValue || !taskbar.Value.Intersects(monitor)) return work;

            RectI tb = taskbar.Value;
            RectI area = work;
            if (tb.Width >= tb.Height)
            {
                bool top = tb.Top + tb.Bottom < monitor.Top + monitor.Bottom;
                edge = top ? TaskbarEdge.Top : TaskbarEdge.Bottom;
                if (top) area.Top = Math.Max(area.Top, tb.Bottom);
                else area.Bottom = Math.Min(area.Bottom, tb.Top);
            }
            else
            {
                bool left = tb.Left + tb.Right < monitor.Left + monitor.Right;
                edge = left ? TaskbarEdge.Left : TaskbarEdge.Right;
                if (left) area.Left = Math.Max(area.Left, tb.Right);
                else area.Right = Math.Min(area.Right, tb.Left);
            }
            return area;
        }

        public static PointI Place(RectI area, TaskbarEdge edge, int width, int height, int margin)
        {
            int x = edge == TaskbarEdge.Left ? area.Left + margin : area.Right - margin - width;
            int y = edge == TaskbarEdge.Top ? area.Top + margin : area.Bottom - margin - height;
            return new PointI(Clamp(x, area.Left, area.Right - width), Clamp(y, area.Top, area.Bottom - height));
        }

        static int Clamp(int value, int min, int max)
        {
            if (max < min) return min;
            return Math.Max(min, Math.Min(value, max));
        }
    }
}
