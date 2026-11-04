using System;
using UnityEngine;

namespace BiolaCampus.World
{
    /// <summary>
    /// Sun position (NOAA / Astronomical Almanac low-precision formulae, ~0.01° accuracy) and
    /// Pacific time helpers for La Mirada, California.
    /// </summary>
    public static class SolarPosition
    {
        public const double LaMiradaLat = 33.9058, LaMiradaLon = -118.0148;

        /// <summary>Elevation above the horizon and azimuth clockwise from north, degrees.</summary>
        public static void Compute(DateTime utc, double latDeg, double lonDeg, out double elevationDeg, out double azimuthDeg)
        {
            double jd = utc.ToOADate() + 2415018.5;
            double n = jd - 2451545.0;
            double L = Mod360(280.460 + 0.9856474 * n);
            double g = Deg2Rad(Mod360(357.528 + 0.9856003 * n));
            double lambda = Deg2Rad(L + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g));
            double eps = Deg2Rad(23.439 - 0.0000004 * n);
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda));
            double dec = Math.Asin(Math.Sin(eps) * Math.Sin(lambda));
            double gmstHours = Mod(18.697374558 + 24.06570982441908 * n, 24.0);
            double ha = Deg2Rad(Mod360(gmstHours * 15.0 + lonDeg) ) - ra;
            double lat = Deg2Rad(latDeg);
            double el = Math.Asin(Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(ha));
            double az = Math.Atan2(-Math.Sin(ha), Math.Tan(dec) * Math.Cos(lat) - Math.Sin(lat) * Math.Cos(ha));
            // Atmospheric refraction near the horizon (Bennett).
            double elDeg = el * 180.0 / Math.PI;
            if (elDeg > -1) elDeg += 1.02 / Math.Tan(Deg2Rad(elDeg + 10.3 / (elDeg + 5.11))) / 60.0;
            elevationDeg = elDeg;
            azimuthDeg = Mod360(az * 180.0 / Math.PI);
        }

        /// <summary>Direction *towards* the sun in Unity space (x = east, y = up, z = north).</summary>
        public static Vector3 Direction(double elevationDeg, double azimuthDeg)
        {
            double el = Deg2Rad(elevationDeg), az = Deg2Rad(azimuthDeg);
            return new Vector3((float)(Math.Cos(el) * Math.Sin(az)), (float)Math.Sin(el), (float)(Math.Cos(el) * Math.Cos(az)));
        }

        /// <summary>UTC offset for US Pacific time (DST: 2nd Sunday of March → 1st Sunday of November, 2:00).</summary>
        public static TimeSpan PacificOffset(DateTime localDate)
        {
            int y = localDate.Year;
            DateTime dstStart = NthSunday(y, 3, 2).AddHours(2);
            DateTime dstEnd = NthSunday(y, 11, 1).AddHours(2);
            return localDate >= dstStart && localDate < dstEnd ? TimeSpan.FromHours(-7) : TimeSpan.FromHours(-8);
        }

        public static DateTime PacificToUtc(DateTime local) => DateTime.SpecifyKind(local - PacificOffset(local), DateTimeKind.Utc);

        public static DateTime UtcToPacific(DateTime utc)
        {
            var guess = utc + TimeSpan.FromHours(-8);
            return utc + PacificOffset(guess);
        }

        static DateTime NthSunday(int year, int month, int nth)
        {
            var d = new DateTime(year, month, 1);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d.AddDays(7 * (nth - 1));
        }

        static double Deg2Rad(double d) => d * Math.PI / 180.0;
        static double Mod360(double d) => Mod(d, 360.0);
        static double Mod(double a, double m) { double r = a % m; return r < 0 ? r + m : r; }
    }
}
