using System.Globalization;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// One 360° scene as far as navigation is concerned.
/// <paramref name="NorthOffsetDegrees"/> is the true-north compass heading the panorama shows at yaw 0
/// (0 for scenes captured in the app, whose images are laid out by compass heading). Null means the direction
/// of the image is unknown: the scene can still be linked TO, but gets no automatic arrows of its own.
/// </summary>
public sealed record PanoramaNode(string Id, string Title, GeoPoint? Location, double? NorthOffsetDegrees);

/// <summary>An automatic "walk to the next panorama" arrow, placed at <paramref name="Yaw"/> in the source scene.</summary>
public sealed record PanoramaLink(
    string TargetId,
    string TargetTitle,
    double Yaw,
    double BearingDegrees,
    double DistanceMeters);

/// <summary>
/// Street-View style links between panoramas from their GPS positions: from each scene, the nearest other scene
/// in every direction gets an arrow pointing the way it really is. A farther scene that lies in roughly the same
/// direction as a nearer one is skipped (you reach it by going through the nearer one), which keeps the view
/// clean when many panoramas are taken along one path. Pure maths - unit-tested in the console test project.
/// </summary>
public static class PanoramaLinks
{
    /// <summary>Scenes farther apart than this are not linked directly.</summary>
    public const double MaxLinkDistanceMeters = 150;

    /// <summary>Closer than this counts as the same spot (e.g. a re-capture) and is not linked.</summary>
    public const double MinLinkDistanceMeters = 1.5;

    /// <summary>Two arrows from one scene must point at least this far apart.</summary>
    public const double MinSeparationDegrees = 35;

    public const int MaxLinksPerScene = 6;

    /// <summary>Pitch at which arrows are drawn: a little below the horizon, like footsteps on the ground.</summary>
    public const double ArrowPitchDegrees = -12;

    public static IReadOnlyList<PanoramaLink> For(PanoramaNode source, IEnumerable<PanoramaNode> all)
    {
        if (source.Location is not { } origin || source.NorthOffsetDegrees is not { } northOffset
            || !IsUsable(origin) || !double.IsFinite(northOffset))
        {
            return Array.Empty<PanoramaLink>();
        }

        var candidates = all
            .Where(node => node.Id != source.Id && node.Location is { } p && IsUsable(p))
            .Select(node =>
            {
                var target = node.Location!.Value;
                return (Node: node,
                    Distance: GeoMath.DistanceMeters(origin, target),
                    Bearing: GeoMath.BearingDegrees(origin, target));
            })
            .Where(c => c.Distance >= MinLinkDistanceMeters && c.Distance <= MaxLinkDistanceMeters)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Node.Id, StringComparer.Ordinal);

        var links = new List<PanoramaLink>();
        foreach (var candidate in candidates)
        {
            if (links.Count >= MaxLinksPerScene)
            {
                break;
            }

            if (links.Any(link => Math.Abs(GeoMath.AngleDifference(link.BearingDegrees, candidate.Bearing)) < MinSeparationDegrees))
            {
                continue; // a nearer scene already covers this direction
            }

            links.Add(new PanoramaLink(
                candidate.Node.Id,
                candidate.Node.Title,
                YawFor(candidate.Bearing, northOffset),
                candidate.Bearing,
                candidate.Distance));
        }

        return links;
    }

    /// <summary>Viewer yaw (-180..180, Pannellum's range) of a real-world compass bearing.</summary>
    public static double YawFor(double bearingDegrees, double northOffsetDegrees) =>
        GeoMath.AngleDifference(bearingDegrees, northOffsetDegrees);

    /// <summary>Arrow label, e.g. "Library · 35 m".</summary>
    public static string Label(PanoramaLink link) =>
        $"{link.TargetTitle} · {link.DistanceMeters.ToString(link.DistanceMeters < 10 ? "0.0" : "0", CultureInfo.InvariantCulture)} m";

    private static bool IsUsable(GeoPoint point) =>
        double.IsFinite(point.Lat) && double.IsFinite(point.Lng)
        && Math.Abs(point.Lat) <= 90 && Math.Abs(point.Lng) <= 180
        && !(point.Lat == 0 && point.Lng == 0);
}
