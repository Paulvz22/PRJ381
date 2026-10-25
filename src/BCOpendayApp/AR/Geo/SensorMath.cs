using BCOpendayApp.AR.Models;
using static BCOpendayApp.AR.Geo.GeoMath;

namespace BCOpendayApp.AR.Geo;

/// <summary>Where the back camera points: compass heading (0 = north, clockwise) and pitch (+ = up).</summary>
public readonly record struct CameraPose(double Heading, double Pitch);

/// <summary>
/// Pure sensor maths (unit-tested in the console test project).
/// The device orientation quaternion rotates DEVICE axes (x right, y up the screen, z out of the screen) into
/// EARTH axes (x east, y north, z up) - that is how .NET MAUI's OrientationSensor reports it. The back camera
/// looks along device -z, so rotating (0, 0, -1) gives the camera's direction in the world.
/// </summary>
public static class SensorMath
{
    /// <summary>
    /// Camera heading/pitch from an orientation quaternion (x, y, z, w). Heading is relative to the same north
    /// the sensor uses (MAGNETIC north on Android) - add the declination to get true north.
    /// Returns null when the quaternion is invalid.
    /// </summary>
    public static CameraPose? CameraPoseFromQuaternion(double x, double y, double z, double w)
    {
        var norm = Math.Sqrt(x * x + y * y + z * z + w * w);
        if (!double.IsFinite(norm) || norm < 1e-6)
        {
            return null;
        }

        x /= norm; y /= norm; z /= norm; w /= norm;

        // v' = v + 2w(u x v) + 2u x (u x v) with v = (0, 0, -1), u = (x, y, z)
        var (vx, vy, vz) = (0.0, 0.0, -1.0);
        var (cx, cy, cz) = (y * vz - z * vy, z * vx - x * vz, x * vy - y * vx);
        var (ccx, ccy, ccz) = (y * cz - z * cy, z * cx - x * cz, x * cy - y * cx);
        var dx = vx + 2 * (w * cx + ccx);
        var dy = vy + 2 * (w * cy + ccy);
        var dz = vz + 2 * (w * cz + ccz);

        var horizontal = Math.Sqrt(dx * dx + dy * dy);
        var pitch = ToDegrees(Math.Atan2(dz, horizontal));
        var heading = horizontal < 1e-9 ? 0 : NormalizeDegrees(ToDegrees(Math.Atan2(dx, dy)));
        return new CameraPose(heading, pitch);
    }

    /// <summary>
    /// Android's rotation-vector sensor sometimes reports only x, y, z (w missing or set to -1 by the wrapper).
    /// The quaternion is a unit quaternion, so w can be recovered from the other three.
    /// </summary>
    public static double RepairW(double x, double y, double z, double w)
    {
        var expected = Math.Sqrt(Math.Max(0, 1 - (x * x + y * y + z * z)));
        return Math.Abs(x * x + y * y + z * z + w * w - 1) > 0.05 ? expected : w;
    }
}

/// <summary>Smooths a noisy compass heading without the 359° → 0° jump problem (averages unit vectors).</summary>
public sealed class HeadingFilter
{
    private readonly double smoothing;
    private double sin, cos;
    private bool hasValue;

    /// <param name="smoothing">0..1, how much of each new reading is used (higher = faster, noisier).</param>
    public HeadingFilter(double smoothing = 0.2) => this.smoothing = Math.Clamp(smoothing, 0.01, 1);

    public double? Value => hasValue ? NormalizeDegrees(ToDegrees(Math.Atan2(sin, cos))) : null;

    public double Add(double headingDegrees)
    {
        var s = Math.Sin(ToRadians(headingDegrees));
        var c = Math.Cos(ToRadians(headingDegrees));
        if (!hasValue)
        {
            (sin, cos, hasValue) = (s, c, true);
        }
        else
        {
            sin += (s - sin) * smoothing;
            cos += (c - cos) * smoothing;
        }

        return Value!.Value;
    }

    public void Reset() => hasValue = false;
}

/// <summary>
/// A tiny Kalman-style position filter: each GPS fix is blended in according to its reported accuracy, and
/// the estimate's uncertainty grows with time (walking speed) between fixes. Removes most GPS jitter.
/// </summary>
public sealed class PositionFilter
{
    private const double WalkingNoiseMetersPerSecond = 1.5;

    private GeoPoint estimate;
    private double variance;
    private DateTimeOffset lastTime;
    private bool hasValue;

    public GeoPoint? Position => hasValue ? estimate : null;

    /// <summary>Estimated accuracy (1 sigma, metres) of <see cref="Position"/>.</summary>
    public double AccuracyMeters => hasValue ? Math.Sqrt(variance) : double.PositiveInfinity;

    public GeoPoint Add(GeoPoint fix, double accuracyMeters, DateTimeOffset time)
    {
        var fixVariance = Math.Pow(Math.Max(accuracyMeters, 1), 2);
        if (!hasValue)
        {
            (estimate, variance, lastTime, hasValue) = (fix, fixVariance, time, true);
            return estimate;
        }

        var seconds = Math.Max(0, (time - lastTime).TotalSeconds);
        variance += Math.Pow(WalkingNoiseMetersPerSecond * seconds, 2);
        lastTime = time;

        var gain = variance / (variance + fixVariance);
        var offset = ToLocal(estimate, fix) * gain;
        estimate = FromLocal(estimate, offset);
        variance *= 1 - gain;
        return estimate;
    }

    public void Reset() => hasValue = false;
}

/// <summary>The sampler's averaged fix plus how much the kept fixes actually spread around it.</summary>
public readonly record struct SamplerStats(
    GeoPoint Point,
    double AccuracyMeters,
    double SpreadMeters,
    int KeptCount,
    int RejectedCount,
    int SampleCount);

/// <summary>
/// Averages several GPS fixes into one point for the scanner, weighting each fix by 1/accuracy², ignoring fixes
/// that are much worse than the best one seen, and dropping positional outliers before averaging. Reports the
/// spread actually measured so a quiet, precise handheld fix is never mistaken for a good one.
/// </summary>
public sealed class LocationSampler
{
    /// <summary>How many good fixes must stay before sampling may stop early.</summary>
    public const int StopMinimumFixes = 10;

    /// <summary>Best combined accuracy that counts as good enough to stop early.</summary>
    public const double StopMaximumAccuracyMeters = 6;

    /// <summary>How long to sample before it may stop early.</summary>
    public static readonly TimeSpan StopMinimumDuration = TimeSpan.FromSeconds(8);

    private readonly List<(GeoPoint Point, double Accuracy)> samples = new();

    public int Count => samples.Count;

    public double BestAccuracy => samples.Count == 0 ? double.PositiveInfinity : samples.Min(s => s.Accuracy);

    /// <summary>Standard deviation (metres) of the kept fixes around their mean (0 when empty).</summary>
    public double SpreadMeters => Analyze()?.SpreadMeters ?? 0;

    public int KeptCount => Analyze()?.KeptCount ?? 0;

    public int RejectedCount => Analyze()?.RejectedCount ?? 0;

    public void Add(GeoPoint point, double accuracyMeters)
    {
        if (double.IsFinite(point.Lat) && double.IsFinite(point.Lng)
            && double.IsFinite(accuracyMeters) && accuracyMeters > 0)
        {
            samples.Add((point, accuracyMeters));
        }
    }

    public void Clear() => samples.Clear();

    /// <summary>The weighted average and its combined accuracy, or null with no usable samples.</summary>
    public (GeoPoint Point, double AccuracyMeters)? Result() =>
        Analyze() is { } stats ? (stats.Point, stats.AccuracyMeters) : null;

    /// <summary>Whether there are enough good fixes, lasting long enough, to stop sampling early.</summary>
    public bool CanStop(TimeSpan elapsed) =>
        Analyze() is { } stats
        && stats.KeptCount >= StopMinimumFixes
        && elapsed >= StopMinimumDuration
        && stats.AccuracyMeters <= StopMaximumAccuracyMeters;

    /// <summary>The averaged fix and the measured spread, or null with no usable samples.</summary>
    public SamplerStats? Analyze()
    {
        if (samples.Count == 0)
        {
            return null;
        }

        // First pass: ignore fixes reported much worse than the best one (the existing accuracy filter).
        var limit = Math.Max(BestAccuracy * 2, 5);
        var used = samples.Where(s => s.Accuracy <= limit).ToList();

        // Then drop positional outliers: fixes further than max(3 m, 2 × the median distance) from the mean.
        var firstMean = WeightedMean(used[0].Point, used, out _);
        var distances = used.Select(s => DistanceMeters(firstMean, s.Point)).ToList();
        var threshold = Math.Max(3.0, 2 * Median(distances));
        var kept = new List<(GeoPoint Point, double Accuracy)>();
        for (var i = 0; i < used.Count; i++)
        {
            if (distances[i] <= threshold)
            {
                kept.Add(used[i]);
            }
        }

        if (kept.Count == 0)
        {
            kept = used; // never drop everything
        }

        var mean = WeightedMean(kept[0].Point, kept, out var sumWeight);

        // Averaging independent fixes improves accuracy, but GPS errors are correlated over short periods, so do
        // not claim better than half of the best single fix - nor better than the spread actually measured.
        var spread = WeightedSpread(mean, kept);
        var combined = Math.Max(Math.Max(1 / Math.Sqrt(sumWeight), BestAccuracy * 0.5), spread);
        return new SamplerStats(mean, combined, spread, kept.Count, samples.Count - kept.Count, samples.Count);
    }

    private static GeoPoint WeightedMean(GeoPoint origin, IReadOnlyList<(GeoPoint Point, double Accuracy)> points, out double sumWeight)
    {
        double weight = 0, x = 0, y = 0;
        foreach (var (point, accuracy) in points)
        {
            var w = 1 / (accuracy * accuracy);
            var local = ToLocal(origin, point);
            x += local.X * w;
            y += local.Y * w;
            weight += w;
        }

        sumWeight = weight;
        return FromLocal(origin, new Vec2(x / weight, y / weight));
    }

    private static double WeightedSpread(GeoPoint mean, IReadOnlyList<(GeoPoint Point, double Accuracy)> points)
    {
        double weight = 0, sum = 0;
        foreach (var (point, accuracy) in points)
        {
            var w = 1 / (accuracy * accuracy);
            var distance = DistanceMeters(mean, point);
            sum += w * distance * distance;
            weight += w;
        }

        return weight > 0 ? Math.Sqrt(sum / weight) : 0;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
