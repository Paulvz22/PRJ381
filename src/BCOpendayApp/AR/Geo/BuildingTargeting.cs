using BCOpendayApp.AR.Models;
using static BCOpendayApp.AR.Geo.GeoMath;

namespace BCOpendayApp.AR.Geo;

/// <summary>Where one building is relative to the visitor right now.</summary>
public sealed record BuildingSighting(
    BuildingAnchor Building,
    double DistanceMeters,
    double CenterBearing,
    double LeftBearing,
    double RightBearing,
    bool IsInside)
{
    /// <summary>Signed degrees from the camera heading to the building centre (negative = left of centre).</summary>
    public double OffsetFrom(double heading) => AngleDifference(CenterBearing, heading);

    /// <summary>Degrees between the heading and the building's visible width; 0 when the heading is inside it.</summary>
    public double AngularMiss(double heading)
    {
        var fromLeft = NormalizeDegrees(heading - LeftBearing);
        var width = NormalizeDegrees(RightBearing - LeftBearing);
        if (fromLeft <= width)
        {
            return 0;
        }

        return Math.Min(Math.Abs(AngleDifference(heading, LeftBearing)), Math.Abs(AngleDifference(heading, RightBearing)));
    }
}

/// <summary>
/// Decides which scanned building the camera is aimed at. Pure maths so it can be unit-tested:
/// 1. Each building's outline is projected around the visitor's position.
/// 2. A ray is cast along the camera heading; the NEAREST outline it crosses wins (a closer building hides
///    the ones behind it, like in reality).
/// 3. If the ray misses everything, buildings within a tolerance (compass error + GPS error seen from that
///    distance) still count, so a building is not "lost" because of a few degrees of sensor noise.
/// </summary>
public static class BuildingTargeting
{
    /// <summary>Buildings further away than this are ignored.</summary>
    public const double MaxRangeMeters = 400;

    /// <summary>Assumed compass/heading error, degrees.</summary>
    public const double HeadingToleranceDegrees = 4;

    /// <summary>Upper bound for the GPS-derived tolerance so far-off guesses are not accepted.</summary>
    public const double MaxToleranceDegrees = 15;

    public static IReadOnlyList<BuildingSighting> Sight(GeoPoint user, IEnumerable<BuildingAnchor> buildings)
    {
        var result = new List<BuildingSighting>();
        foreach (var building in buildings)
        {
            if (!building.IsActive)
            {
                continue;
            }

            var outline = Outline(user, building);
            var origin = new Vec2(0, 0);
            var inside = building.HasFootprint
                ? Contains(outline, origin)
                : ToLocal(user, building.Center).Length <= building.RadiusMeters;

            var distance = inside ? 0 : NearestDistance(outline, building, user);
            if (distance > MaxRangeMeters)
            {
                continue;
            }

            var centre = ToLocal(user, building.Center);
            var centerBearing = centre.Length < 0.01 ? 0 : BearingOf(centre);
            var (left, right) = inside ? (centerBearing - 180, centerBearing + 180) : AngularSpan(outline, centerBearing);

            result.Add(new BuildingSighting(
                building, distance, centerBearing, NormalizeDegrees(left), NormalizeDegrees(right), inside));
        }

        return result.OrderBy(s => s.DistanceMeters).ToList();
    }

    /// <summary>
    /// The building the camera points at, or null. <paramref name="gpsAccuracyMeters"/> widens the tolerance:
    /// a 10 m GPS error seen from 30 m away is ~18°, but it is capped at <see cref="MaxToleranceDegrees"/>.
    /// </summary>
    public static BuildingSighting? Target(
        GeoPoint user, double heading, double gpsAccuracyMeters, IReadOnlyList<BuildingSighting> sightings)
    {
        var direction = Direction(heading);
        var origin = new Vec2(0, 0);

        BuildingSighting? best = null;
        double bestDistance = double.MaxValue;
        foreach (var sighting in sightings)
        {
            if (sighting.IsInside)
            {
                continue;
            }

            var building = sighting.Building;
            var hit = building.HasFootprint
                ? RayHitPolygon(origin, direction, Outline(user, building))
                : RayHitCircle(origin, direction, ToLocal(user, building.Center), building.RadiusMeters);

            if (hit is { } distance && distance <= MaxRangeMeters && distance < bestDistance)
            {
                best = sighting;
                bestDistance = distance;
            }
        }

        if (best is not null)
        {
            return best;
        }

        // Nothing hit exactly: accept the closest building within the noise tolerance.
        return sightings
            .Where(s => !s.IsInside)
            .Select(s => (Sighting: s, Miss: s.AngularMiss(heading), Tolerance: Tolerance(s.DistanceMeters, gpsAccuracyMeters)))
            .Where(x => x.Miss <= x.Tolerance)
            .OrderBy(x => x.Miss / x.Tolerance)
            .ThenBy(x => x.Sighting.DistanceMeters)
            .Select(x => x.Sighting)
            .FirstOrDefault();
    }

    public static double Tolerance(double distanceMeters, double gpsAccuracyMeters)
    {
        var gps = ToDegrees(Math.Atan2(Math.Max(0, gpsAccuracyMeters) * 0.5, Math.Max(distanceMeters, 1)));
        return Math.Min(HeadingToleranceDegrees + gps, MaxToleranceDegrees);
    }

    /// <summary>The building outline in local metres around the user (a 24-sided circle when there is no footprint).</summary>
    public static IReadOnlyList<Vec2> Outline(GeoPoint user, BuildingAnchor building)
    {
        if (building.HasFootprint)
        {
            return building.Footprint.Select(p => ToLocal(user, p)).ToList();
        }

        var centre = ToLocal(user, building.Center);
        return Enumerable.Range(0, 24)
            .Select(i => centre + Direction(i * 15.0) * building.RadiusMeters)
            .ToList();
    }

    private static double NearestDistance(IReadOnlyList<Vec2> outline, BuildingAnchor building, GeoPoint user)
    {
        if (!building.HasFootprint)
        {
            return Math.Max(0, ToLocal(user, building.Center).Length - building.RadiusMeters);
        }

        var best = double.MaxValue;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            best = Math.Min(best, DistanceToSegment(outline[j], outline[i]));
        }

        return best;
    }

    private static double DistanceToSegment(Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        var t = lengthSquared < 1e-12 ? 0 : Math.Clamp(-(a.X * ab.X + a.Y * ab.Y) / lengthSquared, 0, 1);
        return (a + ab * t).Length;
    }

    /// <summary>Left/right bearings of the outline as seen from the user (handles wrapping past north).</summary>
    private static (double Left, double Right) AngularSpan(IReadOnlyList<Vec2> outline, double centerBearing)
    {
        double minOffset = 0, maxOffset = 0;
        foreach (var p in outline)
        {
            var offset = AngleDifference(BearingOf(p), centerBearing);
            minOffset = Math.Min(minOffset, offset);
            maxOffset = Math.Max(maxOffset, offset);
        }

        return (centerBearing + minOffset, centerBearing + maxOffset);
    }
}
