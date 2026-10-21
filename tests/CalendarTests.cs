using System;
using System.Collections.Generic;
using System.Linq;
using WeekKalender.Core;

namespace WeekKalender.Tests
{
    public static class IsoWeekTests
    {
        static void Check(int y, int m, int d, int week, int weekYear)
        {
            var date = new DateTime(y, m, d);
            int w, wy;
            IsoWeek.GetWeekAndYear(date, out w, out wy);
            Assert.Equal(week + "/" + weekYear, w + "/" + wy, date.ToString("yyyy-MM-dd"));
        }

        public static void Vandaag_IsWeek43()
        {
            Check(2026, 10, 21, 43, 2026);
        }

        public static void Jaargrenzen()
        {
            Check(2020, 12, 31, 53, 2020);   // donderdag
            Check(2021, 1, 1, 53, 2020);     // vrijdag hoort nog bij 2020
            Check(2021, 1, 3, 53, 2020);     // zondag
            Check(2021, 1, 4, 1, 2021);      // maandag
            Check(2024, 12, 30, 1, 2025);    // maandag hoort al bij 2025
            Check(2025, 12, 29, 1, 2026);
            Check(2026, 1, 1, 1, 2026);
            Check(2026, 12, 28, 53, 2026);
            Check(2026, 12, 31, 53, 2026);
            Check(2027, 1, 1, 53, 2026);
            Check(2027, 1, 3, 53, 2026);
            Check(2027, 1, 4, 1, 2027);
            Check(2008, 12, 29, 1, 2009);
            Check(2010, 1, 3, 53, 2009);
            Check(2012, 1, 1, 52, 2011);
            Check(2016, 1, 3, 53, 2015);
            Check(2005, 1, 2, 53, 2004);
            Check(2006, 1, 1, 52, 2005);
            Check(2007, 1, 1, 1, 2007);
        }

        public static void Schrikkeljaren()
        {
            Check(2016, 2, 29, 9, 2016);
            Check(2024, 2, 29, 9, 2024);
            Check(2028, 2, 29, 9, 2028);
            Check(2032, 12, 31, 53, 2032);   // schrikkeljaar dat op donderdag begint
            Assert.Equal(53, IsoWeek.WeeksInYear(2020), "2020 (schrikkeljaar, begint op woensdag)");
            Assert.Equal(52, IsoWeek.WeeksInYear(2024), "2024 (schrikkeljaar, begint op maandag)");
            Assert.Equal(52, IsoWeek.WeeksInYear(2100), "2100 (geen schrikkeljaar)");
        }

        public static void JarenMet53Weken()
        {
            var expected = new[] { 2004, 2009, 2015, 2020, 2026, 2032, 2037 };
            var actual = Enumerable.Range(2000, 41).Where(y => IsoWeek.WeeksInYear(y) == 53).ToArray();
            Assert.Equal(string.Join(",", expected), string.Join(",", actual), "jaren met week 53 in 2000-2040");
        }

        /// <summary>Vergelijkt elke dag van 1900 t/m 2100 met een onafhankelijke berekening via '4 januari zit in week 1'.</summary>
        public static void KomtOvereenMetReferentie_1900_2100()
        {
            for (DateTime d = new DateTime(1900, 1, 1); d <= new DateTime(2100, 12, 31); d = d.AddDays(1))
            {
                int week, year;
                IsoWeek.GetWeekAndYear(d, out week, out year);
                int refWeek, refYear;
                Reference(d, out refWeek, out refYear);
                if (week != refWeek || year != refYear)
                    throw new Exception(d.ToString("yyyy-MM-dd") + ": " + week + "/" + year + " i.p.v. " + refWeek + "/" + refYear);
                if (IsoWeek.DayIndex(d) != ((int)d.DayOfWeek + 6) % 7) throw new Exception("DayIndex");
            }
        }

        static void Reference(DateTime d, out int week, out int year)
        {
            for (int y = d.Year + 1; y >= d.Year - 1; y--)
            {
                DateTime jan4 = new DateTime(y, 1, 4);
                DateTime week1 = jan4.AddDays(-(((int)jan4.DayOfWeek + 6) % 7));
                if (week1 <= d)
                {
                    year = y;
                    week = (int)((d - week1).TotalDays / 7) + 1;
                    return;
                }
            }
            throw new Exception("geen referentie");
        }

        public static void EersteDagVanWeek()
        {
            Assert.Equal(new DateTime(2025, 12, 29), IsoWeek.FirstDayOfWeek(2026, 1), "week 1 2026");
            Assert.Equal(new DateTime(2027, 1, 4), IsoWeek.FirstDayOfWeek(2027, 1), "week 1 2027");
            Assert.Equal(new DateTime(2026, 12, 28), IsoWeek.FirstDayOfWeek(2026, 53), "week 53 2026");
            Assert.Equal(new DateTime(2020, 12, 28), IsoWeek.FirstDayOfWeek(2020, 53), "week 53 2020");
            Assert.Equal(new DateTime(2026, 10, 19), IsoWeek.FirstDayOfWeek(2026, 43), "week 43 2026");
            Assert.Throws<ArgumentOutOfRangeException>(() => IsoWeek.FirstDayOfWeek(2025, 53));
            Assert.Throws<ArgumentOutOfRangeException>(() => IsoWeek.FirstDayOfWeek(2026, 0));
            for (int y = 1990; y <= 2060; y++)
                for (int w = 1; w <= IsoWeek.WeeksInYear(y); w++)
                {
                    DateTime monday = IsoWeek.FirstDayOfWeek(y, w);
                    Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek, "maandag");
                    Assert.Equal(w, IsoWeek.GetWeek(monday), "heen en terug " + y + "-" + w);
                    Assert.Equal(y, IsoWeek.GetWeekYear(monday), "weekjaar " + y + "-" + w);
                }
        }
    }

    public static class MonthGridTests
    {
        static string Weeks(MonthGrid g)
        {
            return string.Join(",", Enumerable.Range(0, MonthGrid.Rows).Select(g.WeekNumber));
        }

        public static void Oktober2026()
        {
            var g = new MonthGrid(2026, 10);
            Assert.Equal(new DateTime(2026, 9, 28), g.FirstCell, "eerste cel");
            Assert.Equal(new DateTime(2026, 11, 8), g.LastCell, "laatste cel");
            Assert.Equal("40,41,42,43,44,45", Weeks(g), "weeknummers");
            Assert.True(g.IsInMonth(new DateTime(2026, 10, 31)), "31 okt in maand");
            Assert.False(g.IsInMonth(new DateTime(2026, 9, 30)), "30 sep gedempt");
            Assert.Equal(3, g.RowOf(new DateTime(2026, 10, 21)), "rij van 21 okt");
        }

        public static void MaandBegintOpMaandag()
        {
            var g = new MonthGrid(2027, 2);   // 1 februari 2027 is een maandag
            Assert.Equal(new DateTime(2027, 2, 1), g.FirstCell, "begint op de 1e");
            Assert.Equal("5,6,7,8,9,10", Weeks(g), "weeknummers");
        }

        public static void Jaarwisseling()
        {
            var dec = new MonthGrid(2026, 12);
            Assert.Equal(new DateTime(2026, 11, 30), dec.FirstCell, "december 2026");
            Assert.Equal("49,50,51,52,53,1", Weeks(dec), "december 2026");
            var jan = new MonthGrid(2027, 1);
            Assert.Equal(new DateTime(2026, 12, 28), jan.FirstCell, "januari 2027");
            Assert.Equal("53,1,2,3,4,5", Weeks(jan), "januari 2027");
            var jan2021 = new MonthGrid(2021, 1);
            Assert.Equal("53,1,2,3,4,5", Weeks(jan2021), "januari 2021");
            var jan2025 = new MonthGrid(2025, 1);
            Assert.Equal("1,2,3,4,5,6", Weeks(jan2025), "januari 2025");
            Assert.Equal(new DateTime(2024, 12, 30), jan2025.FirstCell, "januari 2025 begint 30 dec");
        }

        public static void SchrikkelFebruari()
        {
            var g = new MonthGrid(2028, 2);
            Assert.Equal(new DateTime(2028, 1, 31), g.FirstCell, "eerste cel");
            Assert.True(g.IsInMonth(new DateTime(2028, 2, 29)), "29 februari");
            Assert.True(g.Contains(new DateTime(2028, 2, 29)), "29 februari in raster");
        }

        public static void AltijdZesWekenVanafMaandag()
        {
            for (int y = 2000; y <= 2040; y++)
                for (int m = 1; m <= 12; m++)
                {
                    var g = new MonthGrid(y, m);
                    Assert.Equal(DayOfWeek.Monday, g.FirstCell.DayOfWeek, y + "-" + m);
                    Assert.Equal(41, (g.LastCell - g.FirstCell).Days, "42 dagen");
                    Assert.True(g.Contains(new DateTime(y, m, 1)) && g.Contains(new DateTime(y, m, DateTime.DaysInMonth(y, m))), "hele maand zichtbaar " + y + "-" + m);
                    for (int r = 0; r < MonthGrid.Rows; r++)
                        Assert.Equal(IsoWeek.GetWeek(g.CellDate(r, 3)), g.WeekNumber(r), "weeknummer via donderdag");
                }
        }

        public static void BladerenOverJaargrens()
        {
            var dec = new MonthGrid(2026, 12);
            var next = dec.AddMonths(1);
            Assert.Equal("2027-1", next.Year + "-" + next.Month, "december + 1");
            var prev = next.AddMonths(-1);
            Assert.Equal("2026-12", prev.Year + "-" + prev.Month, "januari - 1");
            var far = dec.AddMonths(-13);
            Assert.Equal("2025-11", far.Year + "-" + far.Month, "13 maanden terug");
        }
    }

    public static class WeekInputTests
    {
        static WeekInputResult P(string s, int year = 2026)
        {
            return WeekInput.Parse(s, year);
        }

        static void Ok(string s, int week, int year)
        {
            WeekInputResult r = P(s);
            Assert.True(r.Ok, "'" + s + "' zou geldig moeten zijn: " + r.Error);
            Assert.Equal(week + "/" + year, r.Week + "/" + r.Year, s);
        }

        public static void GeldigeInvoer()
        {
            Ok("43", 43, 2026);
            Ok(" 5 ", 5, 2026);
            Ok("w5", 5, 2026);
            Ok("Week 12", 12, 2026);
            Ok("43 2027", 43, 2027);
            Ok("43-2027", 43, 2027);
            Ok("43/2027", 43, 2027);
            Ok("2027-W43", 43, 2027);
            Ok("2027W1", 1, 2027);
            Ok("2027 43", 43, 2027);
            Ok("week 53 2026", 53, 2026);
            Ok("53 2020", 53, 2020);
        }

        public static void OngeldigeInvoer()
        {
            Assert.Contains("Vul een weeknummer in", P("").Error);
            Assert.Contains("bestaat niet in 2025", P("53 2025").Error);
            Assert.Contains("bestaat niet", P("0").Error);
            Assert.Contains("bestaat niet", P("54").Error);
            Assert.Contains("Onbekende invoer", P("abc").Error);
            Assert.Contains("Onbekende invoer", P("2027").Error);
            Assert.Contains("jaar tussen", P("10 1800").Error);
            Assert.False(P("53", 2027).Ok, "2027 heeft geen week 53");
        }
    }

    public static class DutchTextTests
    {
        public static void Tooltip()
        {
            Assert.Equal("woensdag 21 oktober 2026 – week 43", DutchText.Tooltip(new DateTime(2026, 10, 21)), "tooltip");
            Assert.Equal("vrijdag 1 januari 2027 – week 53", DutchText.Tooltip(new DateTime(2027, 1, 1)), "jaargrens");
        }

        public static void Kopteksten()
        {
            var today = new DateTime(2026, 10, 21);
            Assert.Equal("Oktober 2026", DutchText.Capitalize(DutchText.MonthYear(2026, 10)), "maand");
            Assert.Equal("woensdag 21 oktober", DutchText.DayTitle(today, today), "dag");
            Assert.Equal("maandag 4 januari 2027", DutchText.DayTitle(new DateTime(2027, 1, 4), today), "ander jaar");
            Assert.Equal("ma,di,wo,do,vr,za,zo", string.Join(",", Enumerable.Range(0, 7).Select(DutchText.DayShort)), "dagen");
        }

        public static void Tijden()
        {
            var day = new DateTime(2026, 10, 21);
            Assert.Equal("Hele dag", DutchText.TimeRange(new Appointment { AllDay = true, Start = day, End = day.AddDays(1) }, day), "hele dag");
            Assert.Equal("09:00–10:30", DutchText.TimeRange(new Appointment { Start = day.AddHours(9), End = day.AddHours(10.5) }, day), "gewoon");
            Assert.Equal("09:00", DutchText.TimeRange(new Appointment { Start = day.AddHours(9), End = day.AddHours(9) }, day), "zonder duur");
            Assert.Equal("22:00–00:00", DutchText.TimeRange(new Appointment { Start = day.AddHours(22), End = day.AddDays(1) }, day), "tot middernacht");
            var overnight = new Appointment { Start = day.AddHours(22), End = day.AddDays(1).AddHours(2) };
            Assert.Equal("22:00 – 22 okt 02:00", DutchText.TimeRange(overnight, day), "over middernacht, dag 1");
            Assert.Equal("21 okt 22:00 – 02:00", DutchText.TimeRange(overnight, day.AddDays(1)), "over middernacht, dag 2");
        }
    }

    public static class AppointmentDayTests
    {
        public static void SorteringEnOverlap()
        {
            var day = new DateTime(2026, 10, 21);
            var items = new List<Appointment>
            {
                new Appointment { Subject = "Middag", Start = day.AddHours(13), End = day.AddHours(14) },
                new Appointment { Subject = "Ochtend", Start = day.AddHours(9), End = day.AddHours(10) },
                new Appointment { Subject = "Vakantie", AllDay = true, Start = day.AddDays(-2), End = day.AddDays(3) },
                new Appointment { Subject = "Gisteren", Start = day.AddDays(-1).AddHours(9), End = day.AddDays(-1).AddHours(10) },
                new Appointment { Subject = "Tot middernacht", Start = day.AddDays(-1).AddHours(23), End = day },
                new Appointment { Subject = "Herinnering", Start = day.AddHours(17), End = day.AddHours(17) },
            };
            var result = AppointmentDay.For(items, day).Select(a => a.Subject);
            Assert.Equal("Vakantie,Ochtend,Middag,Herinnering", string.Join(",", result), "volgorde");
        }

        public static void DagenMetStipje()
        {
            var day = new DateTime(2026, 10, 21);
            var items = new[]
            {
                new Appointment { AllDay = true, Start = day, End = day.AddDays(2) },                      // 21 en 22
                new Appointment { Start = day.AddDays(5).AddHours(23), End = day.AddDays(6).AddHours(1) }, // 26 en 27
                new Appointment { Start = day.AddDays(10).AddHours(8), End = day.AddDays(11) },             // alleen 31
            };
            var days = AppointmentDay.DaysWithItems(items, new DateTime(2026, 9, 28), new DateTime(2026, 11, 8));
            Assert.Equal("21,22,26,27,31", string.Join(",", days.OrderBy(d => d).Select(d => d.Day)), "dagen");
        }
    }

    public static class PlacementTests
    {
        static readonly RectI Monitor = new RectI(0, 0, 1920, 1080);

        static PointI Place(RectI work, RectI? taskbar = null, int width = 344, int height = 620)
        {
            TaskbarEdge edge;
            RectI area = PopupPlacement.EffectiveArea(Monitor, work, taskbar, out edge);
            return PopupPlacement.Place(area, edge, width, height, 12);
        }

        static void At(int x, int y, PointI p, string what)
        {
            Assert.Equal(x + "," + y, p.X + "," + p.Y, what);
        }

        public static void TaakbalkOnder()
        {
            At(1564, 400, Place(new RectI(0, 0, 1920, 1032)), "onder");
        }

        public static void TaakbalkBoven()
        {
            At(1564, 60, Place(new RectI(0, 48, 1920, 1080)), "boven");
        }

        public static void TaakbalkLinksEnRechts()
        {
            At(60, 448, Place(new RectI(48, 0, 1920, 1080)), "links");
            At(1516, 448, Place(new RectI(0, 0, 1872, 1080)), "rechts");
        }

        public static void AutomatischVerbergen()
        {
            At(1564, 400, Place(new RectI(0, 0, 1920, 1080), new RectI(0, 1032, 1920, 1080)), "automatisch verbergen");
            At(1564, 448, Place(new RectI(0, 0, 1920, 1080)), "geen taakbalkinfo: rechtsonder op het scherm");
        }

        public static void TweedeMonitorLinks()
        {
            var monitor = new RectI(-1920, 0, 0, 1080);
            TaskbarEdge edge;
            RectI area = PopupPlacement.EffectiveArea(monitor, new RectI(-1920, 0, 0, 1040), new RectI(0, 1032, 1920, 1080), out edge);
            Assert.Equal(TaskbarEdge.Bottom, edge, "rand");
            PointI p = PopupPlacement.Place(area, edge, 344, 620, 12);
            At(-356, 408, p, "tweede monitor");
        }

        public static void TeHoogBlijftBinnenWerkgebied()
        {
            PointI p = Place(new RectI(0, 0, 1920, 1032), null, 344, 2000);
            Assert.Equal(0, p.Y, "boven aan het werkgebied");
        }
    }
}
