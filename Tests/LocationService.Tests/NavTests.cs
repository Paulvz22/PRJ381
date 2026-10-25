using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

/// <summary>Tests for AR navigation guidance (AR/Nav compiles into this project).</summary>
static class NavTests
{
    static readonly GeoPoint Campus = new(-25.7300, 28.1900);

    static GeoPoint At(double east, double north) => GeoMath.FromLocal(Campus, new GeoMath.Vec2(east, north));

    public static void Register(Action<string, Func<Task>> add)
    {
        add("NAV: target straight ahead gives turn 0 and the right distance", () =>
        {
            var step = NavGuidance.Next(Campus, 0, new[] { At(0, 100) }, 0, 5)!;
            Near(step.TurnDegrees, 0, 0.5); Near(step.DistanceToNextMeters, 100, 0.5); Near(step.BearingToNext, 0, 0.5);
            Check(!step.Arrived);
            return Task.CompletedTask;
        });
        add("NAV: target to the east is a right turn facing north and a left turn facing south", () =>
        {
            var route = new[] { At(100, 0) };
            Near(NavGuidance.Next(Campus, 0, route, 0, 5)!.TurnDegrees, 90, 0.5);
            Near(NavGuidance.Next(Campus, 180, route, 0, 5)!.TurnDegrees, -90, 0.5);
            return Task.CompletedTask;
        });
        add("NAV: turn angle wraps correctly across north", () =>
        {
            var route = new[] { GeoMath.FromLocal(Campus, GeoMath.Direction(10) * 100) };
            Near(NavGuidance.Next(Campus, 350, route, 0, 5)!.TurnDegrees, 20, 0.5);
            var other = new[] { GeoMath.FromLocal(Campus, GeoMath.Direction(350) * 100) };
            Near(NavGuidance.Next(Campus, 10, other, 0, 5)!.TurnDegrees, -20, 0.5);
            return Task.CompletedTask;
        });
        add("NAV: waypoints already reached are skipped", () =>
        {
            var route = new[] { At(0, 50), At(0, 100), At(0, 200) };
            var step = NavGuidance.Next(At(0, 45), 0, route, 0, 5)!;
            Check(step.NextIndex == 1 && !step.Arrived);
            Near(step.DistanceToNextMeters, 55, 0.5);
            return Task.CompletedTask;
        });
        add("NAV: progress never goes backwards", () =>
        {
            var route = new[] { At(0, 50), At(0, 100), At(0, 200) };
            var step = NavGuidance.Next(At(0, 49), 0, route, 1, 5)!;
            Check(step.NextIndex == 1);
            return Task.CompletedTask;
        });
        add("NAV: remaining distance adds up the remaining legs", () =>
        {
            var step = NavGuidance.Next(Campus, 0, new[] { At(0, 100), At(100, 100) }, 0, 5)!;
            Near(step.DistanceToNextMeters, 100, 0.5); Near(step.RemainingMeters, 200, 0.5);
            return Task.CompletedTask;
        });
        add("NAV: arrival only happens at the last waypoint, inside the radius", () =>
        {
            var route = new[] { At(0, 50), At(0, 100) };
            var atFirst = NavGuidance.Next(At(0, 50), 0, route, 0, 5)!;
            Check(atFirst.NextIndex == 1 && !atFirst.Arrived);
            var atEnd = NavGuidance.Next(At(0, 97), 0, route, 1, 5)!;
            Check(atEnd.NextIndex == 1 && atEnd.Arrived);
            return Task.CompletedTask;
        });
        add("NAV: weak GPS widens the arrival radius, capped at 25 m", () =>
        {
            var route = new[] { At(0, 100) };
            Check(!NavGuidance.Next(At(0, 85), 0, route, 0, 5)!.Arrived);
            Check(NavGuidance.Next(At(0, 85), 0, route, 0, 20)!.Arrived);
            Check(!NavGuidance.Next(At(0, 70), 0, route, 0, 500)!.Arrived);
            return Task.CompletedTask;
        });
        add("NAV: empty route gives no guidance", () =>
        {
            Check(NavGuidance.Next(Campus, 0, Array.Empty<GeoPoint>(), 0, 5) is null);
            return Task.CompletedTask;
        });
        add("NAV: turn text", () =>
        {
            Check(NavGuidance.Describe(0) == "Straight ahead");
            Check(NavGuidance.Describe(40) == "Bear right");
            Check(NavGuidance.Describe(-90) == "Turn left");
            Check(NavGuidance.Describe(170) == "Turn around");
            return Task.CompletedTask;
        });
    }

    static void Near(double actual, double expected, double tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"Expected {expected} +/- {tolerance}, got {actual}.");
    }

    static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
}