using System;
using UnityEngine;

namespace BiolaCampus.Geo
{
    /// <summary>
    /// WGS84 helpers. The CesiumGeoreference origin is placed at the campus center, which makes the
    /// Unity world frame a local East-Up-North frame (+X = east, +Y = up, +Z = north, 1 unit = 1 m).
    /// <see cref="EnuFrame"/> reproduces that mapping so OSM lat/lon can be placed in the same space
    /// without depending on Cesium API details.
    /// </summary>
    public static class GeoMath
    {
        public const double WgsA = 6378137.0;
        public const double WgsF = 1.0 / 298.257223563;
        public static readonly double WgsE2 = WgsF * (2.0 - WgsF);
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static void GeodeticToEcef(double latDeg, double lonDeg, double h, out double x, out double y, out double z)
        {
            double lat = latDeg * Deg2Rad, lon = lonDeg * Deg2Rad;
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double n = WgsA / Math.Sqrt(1.0 - WgsE2 * sinLat * sinLat);
            x = (n + h) * cosLat * Math.Cos(lon);
            y = (n + h) * cosLat * Math.Sin(lon);
            z = (n * (1.0 - WgsE2) + h) * sinLat;
        }
    }

    public sealed class EnuFrame
    {
        public readonly double OriginLat, OriginLon, OriginHeight;
        readonly double _x0, _y0, _z0;
        readonly double _sinLat, _cosLat, _sinLon, _cosLon;
        readonly double _metersPerDegLat, _metersPerDegLon;

        public EnuFrame(double lat, double lon, double height)
        {
            OriginLat = lat; OriginLon = lon; OriginHeight = height;
            GeoMath.GeodeticToEcef(lat, lon, height, out _x0, out _y0, out _z0);
            double la = lat * GeoMath.Deg2Rad, lo = lon * GeoMath.Deg2Rad;
            _sinLat = Math.Sin(la); _cosLat = Math.Cos(la);
            _sinLon = Math.Sin(lo); _cosLon = Math.Cos(lo);

            // Radii of curvature, used for the (small-area) inverse transform.
            double w = Math.Sqrt(1.0 - GeoMath.WgsE2 * _sinLat * _sinLat);
            double m = GeoMath.WgsA * (1.0 - GeoMath.WgsE2) / (w * w * w);
            double n = GeoMath.WgsA / w;
            _metersPerDegLat = m * GeoMath.Deg2Rad;
            _metersPerDegLon = n * _cosLat * GeoMath.Deg2Rad;
        }

        /// <summary>Geodetic → Unity local (x=east, y=up, z=north).</summary>
        public Vector3 ToLocal(double lat, double lon, double h)
        {
            GeoMath.GeodeticToEcef(lat, lon, h, out double x, out double y, out double z);
            double dx = x - _x0, dy = y - _y0, dz = z - _z0;
            double east = -_sinLon * dx + _cosLon * dy;
            double north = -_sinLat * _cosLon * dx - _sinLat * _sinLon * dy + _cosLat * dz;
            double up = _cosLat * _cosLon * dx + _cosLat * _sinLon * dy + _sinLat * dz;
            return new Vector3((float)east, (float)up, (float)north);
        }

        /// <summary>Geodetic → horizontal local position (east, north).</summary>
        public Vector2 ToLocalXZ(double lat, double lon)
        {
            Vector3 v = ToLocal(lat, lon, OriginHeight);
            return new Vector2(v.x, v.z);
        }

        /// <summary>Approximate inverse, accurate to centimetres within a few km of the origin.</summary>
        public void ToGeodetic(Vector2 eastNorth, out double lat, out double lon)
        {
            lat = OriginLat + eastNorth.y / _metersPerDegLat;
            lon = OriginLon + eastNorth.x / _metersPerDegLon;
        }
    }
}
