using System;
using System.Linq;
using WeekKalender.Core;

namespace WeekKalender.Tests
{
    public static class DesignTests
    {
        public static void AantalAfsprakenPerDag()
        {
            var day = new DateTime(2026, 11, 10);
            var items = new[]
            {
                new Appointment { Start = day.AddHours(9), End = day.AddHours(10) },
                new Appointment { Start = day.AddHours(13), End = day.AddHours(14) },
                new Appointment { AllDay = true, Start = day, End = day.AddDays(2) },                       // 10 en 11
                new Appointment { Start = day.AddDays(3).AddHours(23), End = day.AddDays(4).AddHours(1) },  // 13 en 14
            };
            var counts = AppointmentDay.CountsPerDay(items, new DateTime(2026, 10, 26), new DateTime(2026, 12, 6));
            Assert.Equal("10:3,11:1,13:1,14:1", string.Join(",", counts.OrderBy(c => c.Key).Select(c => c.Key.Day + ":" + c.Value)), "aantallen");
            var same = AppointmentDay.DaysWithItems(items, new DateTime(2026, 10, 26), new DateTime(2026, 12, 6));
            Assert.Equal(same.Count, counts.Count, "zelfde dagen als de stipjes-berekening");
        }

        public static void BegintOver()
        {
            var now = new DateTime(2026, 11, 10, 13, 35, 0);
            Assert.Equal("Over 25 min", DutchText.StartsIn(now.AddMinutes(25), now), "minuten");
            Assert.Equal("Over 1 min", DutchText.StartsIn(now.AddSeconds(20), now), "afgerond naar boven");
            Assert.Equal("Over 1 u", DutchText.StartsIn(now.AddMinutes(60), now), "heel uur");
            Assert.Equal("Over 1 u 10 min", DutchText.StartsIn(now.AddMinutes(70), now), "uur en minuten");
            Assert.Equal(null, DutchText.StartsIn(now.AddHours(4), now), "te ver weg");
            Assert.Equal(null, DutchText.StartsIn(now.AddMinutes(-5), now), "al begonnen");
            Assert.Equal("jan,mrt,dec", DutchText.MonthShort(1) + "," + DutchText.MonthShort(3) + "," + DutchText.MonthShort(12), "korte maandnamen");
        }
    }
}
