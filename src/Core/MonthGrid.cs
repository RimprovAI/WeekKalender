using System;

namespace WeekKalender.Core
{
    /// <summary>
    /// Maandraster van altijd 6 weken (42 dagen), beginnend op de maandag van de week waarin de 1e valt.
    /// Een vaste hoogte voorkomt dat de popup verspringt bij het bladeren.
    /// </summary>
    public sealed class MonthGrid
    {
        public const int Rows = 6;
        public const int Days = Rows * 7;

        public MonthGrid(int year, int month)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            Year = year;
            Month = month;
            FirstCell = IsoWeek.StartOfWeek(new DateTime(year, month, 1));
        }

        public int Year { get; private set; }
        public int Month { get; private set; }

        /// <summary>Altijd een maandag.</summary>
        public DateTime FirstCell { get; private set; }

        public DateTime LastCell
        {
            get { return FirstCell.AddDays(Days - 1); }
        }

        public static MonthGrid For(DateTime date)
        {
            return new MonthGrid(date.Year, date.Month);
        }

        public DateTime CellDate(int row, int column)
        {
            return FirstCell.AddDays(row * 7 + column);
        }

        public int WeekNumber(int row)
        {
            return IsoWeek.GetWeek(FirstCell.AddDays(row * 7));
        }

        public bool IsInMonth(DateTime date)
        {
            return date.Year == Year && date.Month == Month;
        }

        public bool Contains(DateTime date)
        {
            DateTime d = date.Date;
            return d >= FirstCell && d <= LastCell;
        }

        public int RowOf(DateTime date)
        {
            return Contains(date) ? (int)((date.Date - FirstCell).TotalDays / 7) : -1;
        }

        public MonthGrid AddMonths(int months)
        {
            DateTime first = new DateTime(Year, Month, 1).AddMonths(months);
            return new MonthGrid(first.Year, first.Month);
        }
    }
}
