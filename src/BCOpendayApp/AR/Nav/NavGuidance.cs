using BCOpendayApp.AR.Models;
using static BCOpendayApp.AR.Geo.GeoMath;

namespace BCOpendayApp.AR.Geo;

/// <summary>What the visitor should do right now to follow a route.</summary>
/// <param name="NextIndex">Index of the waypoint being walked to. Feed it back as startIndex next time.</param>
/// <param name="TurnDegrees">Signed angle from where the camera points to the next waypoint (negative = left, positive = right).</param>
public sealed record GuidanceStep(
    int NextIndex,
    GeoPoint Next,
    double DistanceToNextMeters,
    double RemainingMeters,
    double BearingToNext,
    double TurnDegrees,
    bool Arrived);

/// <summary>
/// Pure maths for turn-by-turn guidance along a list of waypoints (the last one is the destination).
/// No MAUI types, so it is unit-tested in the console test project.
/// </summary>
public static class NavGuidance
{
    public const double MinArrivalRadiusMeters = 8;
    public const double MaxArrivalRadiusMeters = 25;

    /// <summary>How close counts as "reached": never under 8 m, grows with GPS error, capped at 25 m.</summary>
    public static double ArrivalRadius(double gpsAccuracyMeters) =>
        double.IsNaN(gpsAccuracyMeters)
            ? MinArrivalRadiusMeters
            : Math.Clamp(gpsAccuracyMeters, MinArrivalRadiusMeters, MaxArrivalRadiusMeters);

    /// <summary>
    /// Returns the guidance for the visitor's position and camera heading, or null for an empty route.
    /// Waypoints already reached are skipped, and progress never goes back below startIndex.
    /// </summary>
    public static GuidanceStep? Next(
        GeoPoint user,
        double headingDegrees,
        IReadOnlyList<GeoPoint> route,
        int startIndex,
        double gpsAccuracyMeters)
    {
        if (route.Count == 0)
        {
            return null;
        }

        var radius = ArrivalRadius(gpsAccuracyMeters);
        var last = route.Count - 1;
        var index = Math.Clamp(startIndex, 0, last);

        while (index < last && DistanceMeters(user, route[index]) <= radius)
        {
            index++;
        }

        var next = route[index];
        var distance = DistanceMeters(user, next);
        var bearing = BearingDegrees(user, next);

        var remaining = distance;
        for (var i = index; i < last; i++)
        {
            remaining += DistanceMeters(route[i], route[i + 1]);
        }

        return new GuidanceStep(
            index,
            next,
            distance,
            remaining,
            bearing,
            AngleDifference(bearing, headingDegrees),
            Arrived: index == last && distance <= radius);
    }

    /// <summary>Short instruction for a signed turn angle.</summary>
    public static string Describe(double turnDegrees)
    {
        var size = Math.Abs(turnDegrees);
        var side = turnDegrees < 0 ? "left" : "right";
        return size <= 20 ? "Straight ahead"
            : size <= 60 ? $"Bear {side}"
            : size <= 135 ? $"Turn {side}"
            : "Turn around";
    }
}