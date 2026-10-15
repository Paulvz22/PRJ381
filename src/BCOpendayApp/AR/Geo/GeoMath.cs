using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Geo;

/// <summary>
/// Small-area geometry for campus-sized distances (a few km at most). Positions are projected onto a flat
/// local plane in metres (x = east, y = north) around an origin, which is accurate to centimetres at this scale.
/// Pure C#, no MAUI types, so it is covered by the console test project.
/// </summary>
public static class GeoMath
{
    public const double EarthRadiusMeters = 6_371_008.8;

    public readonly record struct Vec2(double X, double Y)
    {
        public double Length => Math.Sqrt(X * X + Y * Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    }

    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    public static double ToDegrees(double radians) => radians * 180.0 / Math.PI;

    /// <summary>Normalises any angle to [0, 360).</summary>
    public static double NormalizeDegrees(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    /// <summary>Smallest signed difference a - b in (-180, 180].</summary>
    public static double AngleDifference(double a, double b)
    {
        var d = NormalizeDegrees(a - b);
        return d > 180.0 ? d - 360.0 : d;
    }

    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Lat - a.Lat);
        var dLng = ToRadians(b.Lng - a.Lng);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(a.Lat)) * Math.Cos(ToRadians(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Initial bearing from a to b, degrees clockwise from true north.</summary>
    public static double BearingDegrees(GeoPoint a, GeoPoint b)
    {
        var lat1 = ToRadians(a.Lat);
        var lat2 = ToRadians(b.Lat);
        var dLng = ToRadians(b.Lng - a.Lng);
        var y = Math.Sin(dLng) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLng);
        return NormalizeDegrees(ToDegrees(Math.Atan2(y, x)));
    }

    /// <summary>Projects <paramref name="point"/> to metres east/north of <paramref name="origin"/>.</summary>
    public static Vec2 ToLocal(GeoPoint origin, GeoPoint point)
    {
        var metersPerDegree = EarthRadiusMeters * Math.PI / 180.0;
        return new Vec2(
            (point.Lng - origin.Lng) * metersPerDegree * Math.Cos(ToRadians(origin.Lat)),
            (point.Lat - origin.Lat) * metersPerDegree);
    }

    /// <summary>The inverse of <see cref="ToLocal"/>.</summary>
    public static GeoPoint FromLocal(GeoPoint origin, Vec2 local)
    {
        var metersPerDegree = EarthRadiusMeters * Math.PI / 180.0;
        return new GeoPoint(
            origin.Lat + local.Y / metersPerDegree,
            origin.Lng + local.X / (metersPerDegree * Math.Cos(ToRadians(origin.Lat))));
    }

    /// <summary>Unit vector for a compass bearing (x = east, y = north).</summary>
    public static Vec2 Direction(double bearingDegrees) =>
        new(Math.Sin(ToRadians(bearingDegrees)), Math.Cos(ToRadians(bearingDegrees)));

    /// <summary>Compass bearing of a local vector.</summary>
    public static double BearingOf(Vec2 v) => NormalizeDegrees(ToDegrees(Math.Atan2(v.X, v.Y)));

    /// <summary>Even-odd point-in-polygon test (polygon given in local metres).</summary>
    public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Distance along a ray from <paramref name="origin"/> in direction <paramref name="dir"/> (unit vector)
    /// to the first polygon edge it crosses, or null if it misses.
    /// </summary>
    public static double? RayHitPolygon(Vec2 origin, Vec2 dir, IReadOnlyList<Vec2> polygon)
    {
        double? best = null;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var t = RayHitSegment(origin, dir, polygon[j], polygon[i]);
            if (t is { } hit && (best is null || hit < best))
            {
                best = hit;
            }
        }

        return best;
    }

    /// <summary>Distance along the ray to the first point of a circle, or null if it misses (0 when inside).</summary>
    public static double? RayHitCircle(Vec2 origin, Vec2 dir, Vec2 center, double radius)
    {
        var m = origin - center;
        var b = m.X * dir.X + m.Y * dir.Y;
        var c = m.X * m.X + m.Y * m.Y - radius * radius;
        if (c <= 0)
        {
            return 0;
        }

        if (b > 0)
        {
            return null;
        }

        var discriminant = b * b - c;
        return discriminant < 0 ? null : -b - Math.Sqrt(discriminant);
    }

    private static double? RayHitSegment(Vec2 origin, Vec2 dir, Vec2 a, Vec2 b)
    {
        var edge = b - a;
        var denominator = Cross(dir, edge);
        if (Math.Abs(denominator) < 1e-12)
        {
            return null;
        }

        var toA = a - origin;
        var t = Cross(toA, edge) / denominator;
        var u = Cross(toA, dir) / denominator;
        return t >= 0 && u >= 0 && u <= 1 ? t : null;
    }

    /// <summary>Signed area in m² (positive = counter-clockwise).</summary>
    public static double SignedArea(IReadOnlyList<Vec2> polygon)
    {
        double sum = 0;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            sum += Cross(polygon[j], polygon[i]);
        }

        return sum / 2;
    }

    /// <summary>True when two non-adjacent edges cross (e.g. corners were walked out of order).</summary>
    public static bool IsSelfIntersecting(IReadOnlyList<Vec2> polygon)
    {
        var n = polygon.Count;
        for (var i = 0; i < n; i++)
        {
            var a1 = polygon[i];
            var a2 = polygon[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                if (j == i || (j + 1) % n == i || (i + 1) % n == j)
                {
                    continue;
                }

                if (SegmentsCross(a1, a2, polygon[j], polygon[(j + 1) % n]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Convex hull (Andrew's monotone chain), counter-clockwise.</summary>
    public static IReadOnlyList<Vec2> ConvexHull(IEnumerable<Vec2> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        var hull = new List<Vec2>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^1] - hull[^2], p - hull[^2]) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>Area-weighted centroid (falls back to the vertex average for degenerate shapes).</summary>
    public static Vec2 Centroid(IReadOnlyList<Vec2> polygon)
    {
        var area = SignedArea(polygon);
        if (Math.Abs(area) < 1e-6)
        {
            return new Vec2(polygon.Average(p => p.X), polygon.Average(p => p.Y));
        }

        double cx = 0, cy = 0;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var f = Cross(polygon[j], polygon[i]);
            cx += (polygon[j].X + polygon[i].X) * f;
            cy += (polygon[j].Y + polygon[i].Y) * f;
        }

        return new Vec2(cx / (6 * area), cy / (6 * area));
    }

    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    private static bool SegmentsCross(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
    {
        var d1 = Cross(q2 - q1, p1 - q1);
        var d2 = Cross(q2 - q1, p2 - q1);
        var d3 = Cross(p2 - p1, q1 - p1);
        var d4 = Cross(p2 - p1, q2 - p1);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
