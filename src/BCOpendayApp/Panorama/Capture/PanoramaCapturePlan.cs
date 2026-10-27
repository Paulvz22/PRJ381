namespace BCOpendayApp.Panorama.Capture;

/// <summary>What the capture screen should tell the person holding the phone.</summary>
/// <param name="TurnDegrees">Signed turn to the next photo (+ = turn right / clockwise).</param>
/// <param name="TiltDegrees">Signed tilt correction (+ = tilt up).</param>
public readonly record struct CaptureGuidance(int TargetIndex, double TurnDegrees, double TiltDegrees, bool IsAligned);

/// <summary>
/// The ring of photos for one panorama: N photos, evenly spaced around the circle, starting where the person
/// was facing when they tapped Start. N is chosen so neighbouring photos overlap by ~40% (needed both for a
/// seamless join and for the stitcher's alignment step). Pure logic - unit-tested.
/// </summary>
public sealed class PanoramaCapturePlan
{
    /// <summary>Within this many degrees of a target the photo is taken (when the phone is also steady).</summary>
    public const double HeadingToleranceDegrees = 3;

    /// <summary>Photos must be taken roughly level; the stitcher handles small tilts.</summary>
    public const double PitchToleranceDegrees = 6;

    private const double OverlapShare = 0.4;
    private const int MinPhotos = 8;
    private const int MaxPhotos = 24;

    private readonly bool[] taken;

    public PanoramaCapturePlan(double horizontalFovDegrees, double startHeadingDegrees)
    {
        if (!double.IsFinite(horizontalFovDegrees) || horizontalFovDegrees <= 5)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalFovDegrees));
        }

        var step = horizontalFovDegrees * (1 - OverlapShare);
        Count = Math.Clamp((int)Math.Ceiling(360 / step), MinPhotos, MaxPhotos);
        StepDegrees = 360.0 / Count;
        StartHeading = Normalize(startHeadingDegrees);
        taken = new bool[Count];
    }

    public int Count { get; }

    public double StepDegrees { get; }

    public double StartHeading { get; }

    public int TakenCount => taken.Count(t => t);

    public bool IsComplete => taken.All(t => t);

    public double TargetHeading(int index) => Normalize(StartHeading + index * StepDegrees);

    public bool IsTaken(int index) => taken[index];

    public void MarkTaken(int index) => taken[index] = true;

    public void Unmark(int index) => taken[index] = false;

    /// <summary>
    /// The photo to take next: any remaining target the phone already points at, otherwise the nearest remaining
    /// target to the RIGHT (people turn one way round the circle), otherwise the nearest to the left.
    /// Null once all photos are taken.
    /// </summary>
    public CaptureGuidance? Guide(double headingDegrees, double pitchDegrees)
    {
        var remaining = Enumerable.Range(0, Count).Where(i => !taken[i]).ToList();
        if (remaining.Count == 0)
        {
            return null;
        }

        var turns = remaining
            .Select(i => (Index: i, Turn: AngleDifference(TargetHeading(i), headingDegrees)))
            .ToList();
        var here = turns.Where(t => Math.Abs(t.Turn) <= HeadingToleranceDegrees).OrderBy(t => Math.Abs(t.Turn)).ToList();
        var right = turns.Where(t => t.Turn > 0).OrderBy(t => t.Turn).ToList();
        var target = here.Count > 0 ? here[0] : right.Count > 0 ? right[0] : turns.OrderBy(t => Math.Abs(t.Turn)).First();

        var tilt = -pitchDegrees;
        var aligned = Math.Abs(target.Turn) <= HeadingToleranceDegrees && Math.Abs(tilt) <= PitchToleranceDegrees;
        return new CaptureGuidance(target.Index, target.Turn, tilt, aligned);
    }

    private static double Normalize(double degrees)
    {
        var result = degrees % 360;
        return result < 0 ? result + 360 : result;
    }

    private static double AngleDifference(double a, double b)
    {
        var d = Normalize(a - b);
        return d > 180 ? d - 360 : d;
    }
}

/// <summary>"Is the phone being held still?" - true when the heading and tilt stayed within a small range.</summary>
public sealed class SteadinessTracker
{
    private readonly Queue<(DateTimeOffset Time, double Heading, double Pitch)> samples = new();
    private readonly TimeSpan window;
    private readonly double maxSpreadDegrees;

    public SteadinessTracker(TimeSpan window, double maxSpreadDegrees)
    {
        this.window = window;
        this.maxSpreadDegrees = maxSpreadDegrees;
    }

    public void Add(DateTimeOffset time, double heading, double pitch)
    {
        samples.Enqueue((time, heading, pitch));
        while (samples.Count > 0 && time - samples.Peek().Time > window + window)
        {
            samples.Dequeue();
        }
    }

    public void Reset() => samples.Clear();

    public bool IsSteady(DateTimeOffset now)
    {
        var recent = samples.Where(s => now - s.Time <= window).ToList();
        if (recent.Count < 3 || now - samples.Peek().Time < window)
        {
            return false; // not watched long enough yet
        }

        var reference = recent[^1].Heading;
        var headingSpread = recent.Max(s => Math.Abs(AngleDifference(s.Heading, reference)));
        var pitchSpread = recent.Max(s => s.Pitch) - recent.Min(s => s.Pitch);
        return headingSpread <= maxSpreadDegrees && pitchSpread <= maxSpreadDegrees * 2;
    }

    private static double AngleDifference(double a, double b)
    {
        var d = (a - b) % 360;
        if (d < 0)
        {
            d += 360;
        }

        return d > 180 ? d - 360 : d;
    }
}
