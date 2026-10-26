namespace BCOpendayApp.Panorama.Capture;

/// <summary>One photo of the capture ring: decoded pixels (0xAARRGGBB, row by row) and where the camera pointed.</summary>
public sealed class StitchFrame
{
    public StitchFrame(int[] pixels, int width, int height, double headingDegrees, double pitchDegrees)
    {
        if (width <= 1 || height <= 1 || pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel array does not match the frame size.", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
        HeadingDegrees = headingDegrees;
        PitchDegrees = pitchDegrees;
    }

    public int[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Compass heading of the camera's centre line when the photo was taken (true north if known).</summary>
    public double HeadingDegrees { get; }

    /// <summary>Camera tilt when the photo was taken (+ = up).</summary>
    public double PitchDegrees { get; }
}

/// <param name="FocalPixels">Focal length in pixels of the frames (all frames must share one camera and size).</param>
/// <param name="MaxOutputWidth">WebGL texture limit on most phones is 4096, so the result is never wider.</param>
/// <param name="RefineWithImages">Correct compass error by matching the overlap between neighbouring photos.</param>
public sealed record StitchOptions(
    double FocalPixels,
    int MaxOutputWidth = 4096,
    bool RefineWithImages = true,
    double MaxCorrectionDegrees = 8);

/// <summary>
/// A 360° × <see cref="VerticalFovDegrees"/> panorama in equirectangular layout: column 0 is heading -180°
/// (south), the centre column is heading 0° (north), rows are linear in pitch, symmetric around the horizon.
/// Because columns ARE compass headings, the viewer's yaw equals the real-world heading (north offset 0).
/// </summary>
public sealed record StitchResult(
    int[] Pixels,
    int Width,
    int Height,
    double VerticalFovDegrees,
    IReadOnlyList<double> FinalHeadings,
    int RefinedPairs,
    double CoverageFraction);

/// <summary>
/// Turns a ring of ordinary photos into one panorama, entirely on the phone and without extra libraries.
/// 1. Placement: every photo is placed at the compass heading measured when it was taken.
/// 2. Refinement: phone compasses are off by a few degrees, which shows as broken edges. For each pair of
///    neighbouring photos the overlap is compared at small angle shifts (normalised cross-correlation) and the
///    best shift is used; the remaining error around the full circle is spread evenly over all pairs. Pairs that
///    do not match convincingly (blank walls, sky) keep their compass heading.
/// 3. Rendering: each output pixel is a direction (heading, pitch); it is projected into the one or two photos
///    whose centres are closest (pinhole camera model incl. the photo's tilt) and blended across a narrow seam.
/// Pure C# on int arrays, so it is unit-tested in the console test project.
/// </summary>
public static class PanoramaStitcher
{
    private const double RefineStepDegrees = 0.2;
    private const double RefineRowStepDegrees = 0.5;
    private const double RefineMaxPitchDegrees = 12;
    private const double MinOverlapDegrees = 5;
    private const double MinCorrelation = 0.5;
    private const double MinContrast = 6; // grey levels (0..255) of standard deviation in the overlap
    private const double MaxLoopErrorDegrees = 15;
    private const double SeamHalfWidthDegrees = 3;
    private const int Background = unchecked((int)0xFF101214);

    /// <summary>Horizontal field of view (degrees) of a frame <paramref name="width"/> pixels wide.</summary>
    public static double HorizontalFovDegrees(double focalPixels, int width) =>
        2 * ToDegrees(Math.Atan(width / 2.0 / focalPixels));

    public static StitchResult Stitch(IReadOnlyList<StitchFrame> frames, StitchOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("No photos to stitch.", nameof(frames));
        }

        var width = frames[0].Width;
        var height = frames[0].Height;
        if (frames.Any(f => f.Width != width || f.Height != height))
        {
            throw new ArgumentException("All photos must have the same size.", nameof(frames));
        }

        var focal = options.FocalPixels;
        if (!double.IsFinite(focal) || focal <= 1)
        {
            throw new ArgumentException("Focal length must be positive.", nameof(options));
        }

        // Half-width of the part of each photo that is used (the outer 2% is often soft or vignetted).
        var halfUse = HorizontalFovDegrees(focal, width) / 2 * 0.98;

        var order = Enumerable.Range(0, frames.Count)
            .OrderBy(i => Normalize(frames[i].HeadingDegrees))
            .ToArray();
        var sorted = order.Select(i => frames[i]).ToArray();
        var headings = sorted.Select(f => Normalize(f.HeadingDegrees)).ToArray();

        var refinedPairs = 0;
        if (options.RefineWithImages && sorted.Length >= 2)
        {
            (headings, refinedPairs) = Refine(sorted, headings, focal, halfUse, options.MaxCorrectionDegrees);
        }

        var outWidth = Math.Min(options.MaxOutputWidth, (int)Math.Round(2 * Math.PI * focal));
        outWidth = Math.Max(256, outWidth - outWidth % 2);

        var halfVertical = VerticalHalfFov(sorted, headings, focal, height, halfUse, outWidth);
        var degreesPerPixel = 360.0 / outWidth;
        var outHeight = Math.Max(16, (int)Math.Round(2 * halfVertical / degreesPerPixel));
        var verticalFov = outHeight * degreesPerPixel;

        var pixels = Render(sorted, headings, focal, halfUse, outWidth, outHeight, verticalFov, out var covered);

        // Report headings in the caller's original order.
        var final = new double[frames.Count];
        for (var k = 0; k < order.Length; k++)
        {
            final[order[k]] = headings[k];
        }

        return new StitchResult(pixels, outWidth, outHeight, verticalFov, final, refinedPairs, covered / (double)outWidth);
    }

    // ---------------------------------------------------------------- refinement

    private static (double[] Headings, int Refined) Refine(
        StitchFrame[] frames, double[] headings, double focal, double halfUse, double maxCorrection)
    {
        var n = frames.Length;
        var compareHalf = halfUse * 0.9;
        var grids = frames.Select(f => LuminanceGrid(f, focal, compareHalf)).ToArray();

        // gap[i] = angle from photo i to photo i+1 (clockwise); delta[i] = correction found for that gap.
        var gaps = new double[n];
        var deltas = new double[n];
        var confident = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            gaps[i] = n == 1 ? 360 : Normalize(headings[j] - headings[i]);
            if (n == 2 && i == 1)
            {
                gaps[i] = 360 - gaps[0];
            }

            if (2 * compareHalf - gaps[i] < MinOverlapDegrees)
            {
                continue; // no usable overlap: keep the compass
            }

            if (BestShift(grids[i], grids[j], gaps[i], maxCorrection) is { } delta)
            {
                deltas[i] = delta;
                confident[i] = true;
            }
        }

        var refined = confident.Count(c => c);
        if (refined == 0)
        {
            return (headings, 0);
        }

        var corrected = new double[n];
        if (confident.All(c => c) && n >= 3)
        {
            // A full ring: the corrected gaps must add up to exactly 360°. Spread the leftover evenly.
            var sum = gaps.Select((g, i) => g + deltas[i]).Sum();
            var loopError = 360 - sum;
            if (Math.Abs(loopError) > MaxLoopErrorDegrees)
            {
                return (headings, 0); // the matches disagree with each other - trust the compass instead
            }

            var position = 0.0;
            for (var i = 0; i < n; i++)
            {
                corrected[i] = position;
                position += gaps[i] + deltas[i] + loopError / n;
            }

            Anchor(corrected, headings, Enumerable.Range(0, n).ToList());
            return (corrected.Select(Normalize).ToArray(), refined);
        }

        // Otherwise: chains of confidently matched photos keep their relative positions, and each chain is
        // anchored to the average of its compass headings.
        var start = Array.IndexOf(confident, false) + 1; // a chain starts after a non-confident gap
        var chain = new List<int>();
        var chainPosition = 0.0;
        for (var step = 0; step < n; step++)
        {
            var i = (start + step) % n;
            if (chain.Count == 0)
            {
                chainPosition = headings[i];
            }

            corrected[i] = chainPosition;
            chain.Add(i);
            if (confident[i] && step < n - 1)
            {
                chainPosition += gaps[i] + deltas[i];
            }
            else
            {
                Anchor(corrected, headings, chain);
                chain.Clear();
            }
        }

        return (corrected.Select(Normalize).ToArray(), refined);
    }

    /// <summary>Shifts the chain so its average difference from the compass headings is zero.</summary>
    private static void Anchor(double[] corrected, double[] compass, List<int> chain)
    {
        var offset = chain.Average(i => AngleDifference(compass[i], corrected[i]));
        foreach (var i in chain)
        {
            corrected[i] += offset;
        }
    }

    /// <summary>
    /// Best correction for the gap between photo a and the next photo b, or null when the overlap does not match
    /// convincingly. Grid column k of a photo is local yaw (k * step - half); a pixel at absolute heading θ is
    /// local yaw θ - h in that photo, so b's column = a's column - (gap + delta) / step.
    /// </summary>
    private static double? BestShift(LumGrid a, LumGrid b, double gap, double maxCorrection)
    {
        var minShift = (int)Math.Ceiling((gap - maxCorrection) / RefineStepDegrees);
        var maxShift = (int)Math.Floor((gap + maxCorrection) / RefineStepDegrees);
        double bestScore = double.NegativeInfinity;
        int bestShift = 0;
        double bestContrast = 0;

        for (var m = minShift; m <= maxShift; m++)
        {
            var (score, contrast, count) = Correlate(a, b, m);
            if (count < 200)
            {
                continue;
            }

            // Prefer small corrections when two shifts match almost equally well.
            var penalised = score - 0.002 * Math.Abs(m * RefineStepDegrees - gap);
            if (penalised > bestScore)
            {
                bestScore = penalised;
                bestShift = m;
                bestContrast = contrast;
            }
        }

        if (bestScore < MinCorrelation || bestContrast < MinContrast)
        {
            return null;
        }

        return bestShift * RefineStepDegrees - gap;
    }

    private static (double Score, double Contrast, int Count) Correlate(LumGrid a, LumGrid b, int shift)
    {
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
        var count = 0;
        for (var ka = Math.Max(0, shift); ka < a.Columns && ka - shift < b.Columns; ka++)
        {
            var kb = ka - shift;
            for (var r = 0; r < a.Rows; r++)
            {
                var va = a.Values[ka * a.Rows + r];
                var vb = b.Values[kb * b.Rows + r];
                if (va < 0 || vb < 0)
                {
                    continue;
                }

                sumA += va;
                sumB += vb;
                sumAA += va * va;
                sumBB += vb * vb;
                sumAB += va * vb;
                count++;
            }
        }

        if (count == 0)
        {
            return (double.NegativeInfinity, 0, 0);
        }

        var varA = sumAA - sumA * sumA / count;
        var varB = sumBB - sumB * sumB / count;
        var cov = sumAB - sumA * sumB / count;
        if (varA <= 1e-9 || varB <= 1e-9)
        {
            return (double.NegativeInfinity, 0, count);
        }

        var contrast = Math.Sqrt(Math.Min(varA, varB) / count);
        return (cov / Math.Sqrt(varA * varB), contrast, count);
    }

    private sealed record LumGrid(float[] Values, int Columns, int Rows);

    /// <summary>Brightness of one photo sampled on a regular yaw/pitch grid (-1 = outside the photo).</summary>
    private static LumGrid LuminanceGrid(StitchFrame frame, double focal, double half)
    {
        var columns = (int)Math.Floor(2 * half / RefineStepDegrees) + 1;
        var rows = (int)Math.Floor(2 * RefineMaxPitchDegrees / RefineRowStepDegrees) + 1;
        var values = new float[columns * rows];
        var (sinP, cosP) = SinCos(frame.PitchDegrees);
        for (var k = 0; k < columns; k++)
        {
            var (sinA, cosA) = SinCos(-half + k * RefineStepDegrees);
            for (var r = 0; r < rows; r++)
            {
                var (sinPhi, cosPhi) = SinCos(-RefineMaxPitchDegrees + r * RefineRowStepDegrees);
                values[k * rows + r] = Project(frame, focal, sinA, cosA, sinPhi, cosPhi, sinP, cosP, out var x, out var y)
                    && SampleNearest(frame, x, y) is { } argb
                    ? Luminance(argb)
                    : -1f;
            }
        }

        return new LumGrid(values, columns, rows);
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>
    /// The largest symmetric pitch range every covered column can fill: at a column at yaw α from its photo's
    /// centre a pinhole photo reaches pitch ± atan(h/2 / f · cos α), shifted by the photo's own tilt.
    /// </summary>
    private static double VerticalHalfFov(
        StitchFrame[] frames, double[] headings, double focal, int height, double halfUse, int outWidth)
    {
        var limit = double.PositiveInfinity;
        for (var u = 0; u < outWidth; u++)
        {
            var theta = ColumnHeading(u, outWidth);
            var bestUp = double.NegativeInfinity;
            var bestDown = double.NegativeInfinity;
            for (var i = 0; i < frames.Length; i++)
            {
                var alpha = AngleDifference(theta, headings[i]);
                if (Math.Abs(alpha) > halfUse)
                {
                    continue;
                }

                var reach = ToDegrees(Math.Atan(height / 2.0 / focal * Math.Cos(ToRadians(alpha))));
                bestUp = Math.Max(bestUp, reach + frames[i].PitchDegrees);
                bestDown = Math.Max(bestDown, reach - frames[i].PitchDegrees);
            }

            if (double.IsFinite(bestUp))
            {
                limit = Math.Min(limit, Math.Min(bestUp, bestDown));
            }
        }

        return double.IsFinite(limit) ? Math.Clamp(limit * 0.98, 5, 89) : 30;
    }

    private static int[] Render(
        StitchFrame[] frames, double[] headings, double focal, double halfUse,
        int outWidth, int outHeight, double verticalFov, out int coveredColumns)
    {
        var pixels = new int[outWidth * outHeight];
        var rowSin = new double[outHeight];
        var rowCos = new double[outHeight];
        for (var r = 0; r < outHeight; r++)
        {
            (rowSin[r], rowCos[r]) = SinCos(verticalFov / 2 - (r + 0.5) * verticalFov / outHeight);
        }

        var frameSin = frames.Select(f => Math.Sin(ToRadians(f.PitchDegrees))).ToArray();
        var frameCos = frames.Select(f => Math.Cos(ToRadians(f.PitchDegrees))).ToArray();
        coveredColumns = 0;

        for (var u = 0; u < outWidth; u++)
        {
            var theta = ColumnHeading(u, outWidth);

            // The two photos whose centres are nearest this column.
            int first = -1, second = -1;
            double firstAbs = double.MaxValue, secondAbs = double.MaxValue, firstAlpha = 0, secondAlpha = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                var alpha = AngleDifference(theta, headings[i]);
                var abs = Math.Abs(alpha);
                if (abs > halfUse)
                {
                    continue;
                }

                if (abs < firstAbs)
                {
                    (second, secondAbs, secondAlpha) = (first, firstAbs, firstAlpha);
                    (first, firstAbs, firstAlpha) = (i, abs, alpha);
                }
                else if (abs < secondAbs)
                {
                    (second, secondAbs, secondAlpha) = (i, abs, alpha);
                }
            }

            if (first < 0)
            {
                for (var r = 0; r < outHeight; r++)
                {
                    pixels[r * outWidth + u] = Background;
                }

                continue;
            }

            coveredColumns++;

            // Narrow seam: the nearer photo wins except within ±SeamHalfWidth of the half-way line.
            var firstWeight = second < 0
                ? 1.0
                : Math.Clamp((secondAbs - firstAbs) / (2 * SeamHalfWidthDegrees) + 0.5, 0, 1);
            var (sinA1, cosA1) = SinCos(firstAlpha);
            var (sinA2, cosA2) = second < 0 ? (0.0, 1.0) : SinCos(secondAlpha);

            for (var r = 0; r < outHeight; r++)
            {
                // Each photo is used where it has pixels; if only one of the two reaches this row (different
                // tilts), that one is used on its own.
                var has1 = Project(frames[first], focal, sinA1, cosA1, rowSin[r], rowCos[r], frameSin[first], frameCos[first], out var x1, out var y1)
                    & SampleBilinear(frames[first], x1, y1, out var r1, out var g1, out var b1);
                var has2 = false;
                double r2 = 0, g2 = 0, b2 = 0;
                if (second >= 0 && (firstWeight < 1 || !has1))
                {
                    has2 = Project(frames[second], focal, sinA2, cosA2, rowSin[r], rowCos[r], frameSin[second], frameCos[second], out var x2, out var y2)
                        && SampleBilinear(frames[second], x2, y2, out r2, out g2, out b2);
                }

                var w1 = has1 ? (has2 ? firstWeight : 1) : 0;
                var w2 = has2 ? 1 - w1 : 0;
                var total = w1 + w2;
                pixels[r * outWidth + u] = total <= 0
                    ? Background
                    : Pack((r1 * w1 + r2 * w2) / total, (g1 * w1 + g2 * w2) / total, (b1 * w1 + b2 * w2) / total);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Direction (yaw α from the photo's centre, pitch φ) → pixel in a pinhole photo tilted by its pitch p.
    /// Camera axes: x right, y up, z forward. Returns false behind the camera.
    /// </summary>
    private static bool Project(
        StitchFrame frame, double focal,
        double sinA, double cosA, double sinPhi, double cosPhi, double sinP, double cosP,
        out double x, out double y)
    {
        var dx = cosPhi * sinA;
        var dy = sinPhi;
        var dz = cosPhi * cosA;

        // Undo the photo's upward tilt (rotation about the camera's x axis).
        var cy = dy * cosP - dz * sinP;
        var cz = dy * sinP + dz * cosP;
        if (cz <= 1e-6)
        {
            x = y = 0;
            return false;
        }

        x = (frame.Width - 1) / 2.0 + focal * dx / cz;
        y = (frame.Height - 1) / 2.0 - focal * cy / cz;
        return true;
    }

    private static bool SampleBilinear(StitchFrame frame, double x, double y, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (x < 0 || y < 0 || x > frame.Width - 1 || y > frame.Height - 1)
        {
            return false;
        }

        var x0 = Math.Min((int)x, frame.Width - 2);
        var y0 = Math.Min((int)y, frame.Height - 2);
        var fx = x - x0;
        var fy = y - y0;
        var row = y0 * frame.Width;
        var p00 = frame.Pixels[row + x0];
        var p10 = frame.Pixels[row + x0 + 1];
        var p01 = frame.Pixels[row + frame.Width + x0];
        var p11 = frame.Pixels[row + frame.Width + x0 + 1];

        var w00 = (1 - fx) * (1 - fy);
        var w10 = fx * (1 - fy);
        var w01 = (1 - fx) * fy;
        var w11 = fx * fy;
        r = w00 * ((p00 >> 16) & 0xFF) + w10 * ((p10 >> 16) & 0xFF) + w01 * ((p01 >> 16) & 0xFF) + w11 * ((p11 >> 16) & 0xFF);
        g = w00 * ((p00 >> 8) & 0xFF) + w10 * ((p10 >> 8) & 0xFF) + w01 * ((p01 >> 8) & 0xFF) + w11 * ((p11 >> 8) & 0xFF);
        b = w00 * (p00 & 0xFF) + w10 * (p10 & 0xFF) + w01 * (p01 & 0xFF) + w11 * (p11 & 0xFF);
        return true;
    }

    private static int? SampleNearest(StitchFrame frame, double x, double y)
    {
        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);
        if (ix < 0 || iy < 0 || ix >= frame.Width || iy >= frame.Height)
        {
            return null;
        }

        return frame.Pixels[iy * frame.Width + ix];
    }

    private static float Luminance(int argb) =>
        (((argb >> 16) & 0xFF) * 299 + ((argb >> 8) & 0xFF) * 587 + (argb & 0xFF) * 114) / 1000f;

    private static int Pack(double r, double g, double b) =>
        unchecked((int)0xFF000000)
        | (Channel(r) << 16)
        | (Channel(g) << 8)
        | Channel(b);

    private static int Channel(double value) => (int)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>Compass heading at the centre of output column u (column 0 = -180°, centre = 0° = north).</summary>
    public static double ColumnHeading(int u, int outWidth) => -180 + (u + 0.5) * 360.0 / outWidth;

    private static (double Sin, double Cos) SinCos(double degrees)
    {
        var radians = ToRadians(degrees);
        return (Math.Sin(radians), Math.Cos(radians));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;

    private static double Normalize(double degrees)
    {
        var result = degrees % 360;
        return result < 0 ? result + 360 : result;
    }

    /// <summary>Signed a - b in (-180, 180].</summary>
    private static double AngleDifference(double a, double b)
    {
        var d = Normalize(a - b);
        return d > 180 ? d - 360 : d;
    }
}
