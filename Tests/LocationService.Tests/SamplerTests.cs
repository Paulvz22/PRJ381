using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

/// <summary>Tests for the scanner's GPS sampler: accuracy weighting, outlier rejection, measured spread and the
/// stop condition (AR/Geo compiles into this project).</summary>
static class SamplerTests
{
    static readonly GeoPoint Campus = new(-25.7300, 28.1900);

    public static void Register(Action<string, Func<Task>> add)
    {
        add("SAMPLER: all-good fixes average and report how many were kept", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 4); sampler.Add(At(1, 0), 4); sampler.Add(At(2, 0), 4);
            var stats = sampler.Analyze()!.Value;
            Near(GeoMath.ToLocal(Campus, stats.Point).X, 1, 0.01);
            Check(stats.KeptCount == 3 && stats.RejectedCount == 0 && stats.SampleCount == 3);
            Check(stats.AccuracyMeters >= stats.SpreadMeters);
            return Task.CompletedTask;
        });
        add("SAMPLER: a far-away outlier with a good reported accuracy is removed", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 4); sampler.Add(At(1, 0), 4); sampler.Add(At(2, 0), 4); sampler.Add(At(50, 0), 4);
            var stats = sampler.Analyze()!.Value;
            Near(GeoMath.ToLocal(Campus, stats.Point).X, 1, 0.5);
            Check(stats.KeptCount == 3 && stats.RejectedCount == 1);
            return Task.CompletedTask;
        });
        add("SAMPLER: fixes with much worse reported accuracy are ignored", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 3); sampler.Add(At(20, 0), 100);
            var stats = sampler.Analyze()!.Value;
            Near(GeoMath.ToLocal(Campus, stats.Point).X, 0, 0.01);
            Check(stats.KeptCount == 1 && stats.RejectedCount == 1);
            return Task.CompletedTask;
        });
        add("SAMPLER: weighting favours the more accurate fix", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 2); sampler.Add(At(10, 0), 4);
            var stats = sampler.Analyze()!.Value;
            Near(GeoMath.ToLocal(Campus, stats.Point).X, 2, 0.01);
            return Task.CompletedTask;
        });
        add("SAMPLER: identical fixes have zero spread", () =>
        {
            var sampler = new LocationSampler();
            for (var i = 0; i < 5; i++) sampler.Add(At(3, 4), 5);
            var stats = sampler.Analyze()!.Value;
            var local = GeoMath.ToLocal(Campus, stats.Point);
            Near(local.X, 3, 0.01); Near(local.Y, 4, 0.01);
            Near(stats.SpreadMeters, 0, 0.01);
            Check(stats.KeptCount == 5);
            return Task.CompletedTask;
        });
        add("SAMPLER: fewer fixes than the minimum cannot stop", () =>
        {
            var sampler = new LocationSampler();
            for (var i = 0; i < 9; i++) sampler.Add(At(i * 0.2, 0), 4);
            Check(sampler.KeptCount == 9);
            Check(!sampler.CanStop(TimeSpan.FromSeconds(30)));
            return Task.CompletedTask;
        });
        add("SAMPLER: NaN, zero and infinite accuracy are rejected", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), double.NaN);
            sampler.Add(At(1, 0), 0);
            sampler.Add(At(2, 0), double.PositiveInfinity);
            Check(sampler.Count == 0);
            Check(sampler.Analyze() is null);
            return Task.CompletedTask;
        });
        add("SAMPLER: empty input gives no result and cannot stop", () =>
        {
            var sampler = new LocationSampler();
            Check(sampler.Result() is null);
            Check(sampler.Analyze() is null);
            Check(!sampler.CanStop(TimeSpan.FromSeconds(100)));
            return Task.CompletedTask;
        });
        add("SAMPLER: combined accuracy is never below the measured spread", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 4); sampler.Add(At(10, 0), 4); sampler.Add(At(10, 10), 4);
            var stats = sampler.Analyze()!.Value;
            Check(stats.SpreadMeters > 0);
            Check(stats.AccuracyMeters >= stats.SpreadMeters);
            return Task.CompletedTask;
        });
        add("SAMPLER: stop condition needs enough fixes, enough time and good accuracy", () =>
        {
            var good = new LocationSampler();
            for (var i = 0; i < 10; i++) good.Add(At(i * 0.2, 0), 4);
            Check(!good.CanStop(TimeSpan.FromSeconds(7)));
            Check(good.CanStop(TimeSpan.FromSeconds(8)));

            var poor = new LocationSampler();
            for (var i = 0; i < 10; i++) poor.Add(At(0, 0), 14);
            Check(poor.Analyze()!.Value.AccuracyMeters > LocationSampler.StopMaximumAccuracyMeters);
            Check(!poor.CanStop(TimeSpan.FromSeconds(8)));
            return Task.CompletedTask;
        });
    }

    static GeoPoint At(double east, double north) => GeoMath.FromLocal(Campus, new GeoMath.Vec2(east, north));

    static void Near(double actual, double expected, double tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"Expected {expected} ± {tolerance}, got {actual}.");
    }

    static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
}
