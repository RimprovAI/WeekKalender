using System;

namespace WeekKalender.Core
{
    /// <summary>
    /// ISO 8601-weeknummers: de week begint op maandag en week 1 is de week met de eerste donderdag van het jaar.
    /// </summary>
    public static class IsoWeek
    {
        /// <summary>0 = maandag … 6 = zondag.</summary>
        public static int DayIndex(DateTime date)
        {
            return ((int)date.DayOfWeek + 6) % 7;
        }

        public static DateTime StartOfWeek(DateTime date)
        {
            return date.Date.AddDays(-DayIndex(date));
        }

        public static int GetWeek(DateTime date)
        {
            int week, year;
            GetWeekAndYear(date, out week, out year);
            return week;
        }

        /// <summary>Het jaar waar de week bij hoort; wijkt rond de jaarwisseling af van het kalenderjaar.</summary>
        public static int GetWeekYear(DateTime date)
        {
            int week, year;
            GetWeekAndYear(date, out week, out year);
            return year;
        }

        public static void GetWeekAndYear(DateTime date, out int week, out int weekYear)
        {
            // De donderdag van dezelfde week bepaalt in welk jaar de week valt.
            DateTime thursday = date.Date.AddDays(3 - DayIndex(date));
            weekYear = thursday.Year;
            week = (thursday.DayOfYear - 1) / 7 + 1;
        }

        /// <summary>52 of 53. 28 december valt altijd in de laatste week van het jaar.</summary>
        public static int WeeksInYear(int year)
        {
            return GetWeek(new DateTime(year, 12, 28));
        }

        /// <summary>De maandag van de gegeven ISO-week.</summary>
        public static DateTime FirstDayOfWeek(int weekYear, int week)
        {
            if (weekYear < 2 || weekYear > 9998) throw new ArgumentOutOfRangeException(nameof(weekYear));
            if (week < 1 || week > WeeksInYear(weekYear)) throw new ArgumentOutOfRangeException(nameof(week));
            // 4 januari valt altijd in week 1.
            return StartOfWeek(new DateTime(weekYear, 1, 4)).AddDays((week - 1) * 7);
        }
    }
}
