using System.Text.Json;
using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

/// <summary>Tests for the AR guide's maths and data loading (AR/Geo and AR/Data compile into this project).</summary>
static class ArTests
{
    static readonly GeoPoint Campus = new(-25.7300, 28.1900);

    public static void Register(Action<string, Func<Task>> add)
    {
        add("AR: upright phone with the back camera facing north has heading 0 and pitch 0", () =>
        {
            var pose = SensorMath.CameraPoseFromQuaternion(0.7071068, 0, 0, 0.7071068)!.Value;
            Near(pose.Heading, 0, 0.01); Near(pose.Pitch, 0, 0.01);
            return Task.CompletedTask;
        });
        add("AR: turning the upright phone to face west gives heading 270", () =>
        {
            // 90° about Earth Z (west) applied after the upright rotation.
            var (x, y, z, w) = Multiply((0, 0, 0.7071068, 0.7071068), (0.7071068, 0, 0, 0.7071068));
            Near(SensorMath.CameraPoseFromQuaternion(x, y, z, w)!.Value.Heading, 270, 0.01);
            return Task.CompletedTask;
        });
        add("AR: tilting the camera up 20 degrees gives pitch +20", () =>
        {
            var half = 110 * Math.PI / 360;
            Near(SensorMath.CameraPoseFromQuaternion(Math.Sin(half), 0, 0, Math.Cos(half))!.Value.Pitch, 20, 0.01);
            return Task.CompletedTask;
        });
        add("AR: a missing quaternion w is rebuilt from x, y, z", () =>
        {
            Near(SensorMath.RepairW(0.7071068, 0, 0, -1), 0.7071068, 0.0001);
            Near(SensorMath.RepairW(0.6, 0, 0, 0.8), 0.8, 1e-12);
            return Task.CompletedTask;
        });
        add("AR: heading smoothing does not jump across north", () =>
        {
            var filter = new HeadingFilter(0.5);
            filter.Add(359); var value = filter.Add(1);
            Check(GeoMath.AngleDifference(value, 0) is > -1.5 and < 1.5);
            return Task.CompletedTask;
        });
        add("AR: distance, bearing and local projection agree", () =>
        {
            var north = GeoMath.FromLocal(Campus, new GeoMath.Vec2(0, 100));
            var east = GeoMath.FromLocal(Campus, new GeoMath.Vec2(100, 0));
            Near(GeoMath.DistanceMeters(Campus, north), 100, 0.2);
            Near(GeoMath.DistanceMeters(Campus, east), 100, 0.2);
            Near(GeoMath.BearingDegrees(Campus, north), 0, 0.05);
            Near(GeoMath.BearingDegrees(Campus, east), 90, 0.05);
            var back = GeoMath.ToLocal(Campus, east);
            Near(back.X, 100, 1e-6); Near(back.Y, 0, 1e-6);
            return Task.CompletedTask;
        });
        add("AR: pointing at a building selects it, pointing away selects nothing", () =>
        {
            var building = Square("main", 0, 100, 40);
            var sightings = BuildingTargeting.Sight(Campus, new[] { building });
            Check(BuildingTargeting.Target(Campus, 0, 5, sightings)?.Building.Id == "main");
            Check(BuildingTargeting.Target(Campus, 90, 5, sightings) is null);
            Check(BuildingTargeting.Target(Campus, 180, 5, sightings) is null);
            Near(sightings[0].DistanceMeters, 80, 0.5);
            return Task.CompletedTask;
        });
        add("AR: small compass errors just outside the edge are tolerated, large ones are not", () =>
        {
            var sightings = BuildingTargeting.Sight(Campus, new[] { Square("main", 0, 100, 40) });
            // The near corners are at ±14° (atan(20/80)); 16° is just outside, 35° is far outside.
            Check(BuildingTargeting.Target(Campus, 16, 5, sightings) is not null);
            Check(BuildingTargeting.Target(Campus, 35, 5, sightings) is null);
            return Task.CompletedTask;
        });
        add("AR: the nearer building hides the one behind it", () =>
        {
            var sightings = BuildingTargeting.Sight(Campus, new[] { Square("far", 0, 200, 120), Square("near", 0, 60, 20) });
            Check(BuildingTargeting.Target(Campus, 0, 5, sightings)?.Building.Id == "near");
            // Past the near building's edge (±11°) the wide far building is hit directly and wins.
            Check(BuildingTargeting.Target(Campus, 15, 5, sightings)?.Building.Id == "far");
            return Task.CompletedTask;
        });
        add("AR: buildings straddling north are found at 355 and 5 degrees", () =>
        {
            var sightings = BuildingTargeting.Sight(Campus, new[] { Square("north", 0, 100, 60) });
            Check(BuildingTargeting.Target(Campus, 355, 5, sightings) is not null);
            Check(BuildingTargeting.Target(Campus, 5, 5, sightings) is not null);
            Check(sightings[0].AngularMiss(358) == 0 && sightings[0].AngularMiss(2) == 0);
            return Task.CompletedTask;
        });
        add("AR: standing inside an outline is reported and not used as the target", () =>
        {
            var sightings = BuildingTargeting.Sight(Campus, new[] { Square("here", 0, 0, 30), Square("main", 0, 100, 40) });
            Check(sightings.Single(s => s.Building.Id == "here").IsInside);
            Check(BuildingTargeting.Target(Campus, 0, 5, sightings)?.Building.Id == "main");
            return Task.CompletedTask;
        });
        add("AR: centre + radius buildings and range limit", () =>
        {
            var circle = new BuildingAnchor { Id = "hall", Name = "Hall", CenterLat = At(0, 50).Lat, CenterLng = At(0, 50).Lng, RadiusMeters = 10 };
            var far = new BuildingAnchor { Id = "far", Name = "Far", CenterLat = At(0, 900).Lat, CenterLng = At(0, 900).Lng, RadiusMeters = 10 };
            var sightings = BuildingTargeting.Sight(Campus, new[] { circle, far });
            Check(sightings.Count == 1 && BuildingTargeting.Target(Campus, 0, 5, sightings)?.Building.Id == "hall");
            Near(sightings[0].DistanceMeters, 40, 0.5);
            return Task.CompletedTask;
        });
        add("AR: hidden (inactive) buildings are not shown", () =>
        {
            Check(BuildingTargeting.Sight(Campus, new[] { Square("x", 0, 50, 20) with { IsActive = false } }).Count == 0);
            return Task.CompletedTask;
        });
        add("AR: validation rejects crossed outlines, bad ids and impossible values", () =>
        {
            var good = Square("main-building", 0, 100, 40);
            Check(BuildingValidation.Problem(good) is null);
            var crossed = good with { Footprint = new[] { good.Footprint[0], good.Footprint[2], good.Footprint[1], good.Footprint[3] } };
            Check(BuildingValidation.Problem(crossed)!.Contains("crosses", StringComparison.Ordinal));
            Check(BuildingValidation.Problem(good with { Id = "Main Building" }) is not null);
            Check(BuildingValidation.Problem(good with { Name = "" }) is not null);
            Check(BuildingValidation.Problem(good with { RadiusMeters = 9999 }) is not null);
            Check(BuildingValidation.Problem(good with { CenterLat = 0, CenterLng = 0 }) is not null);
            return Task.CompletedTask;
        });
        add("AR: names become safe ids", () =>
        {
            Check(BuildingValidation.SlugFromName("Main Building (Block A)") == "main-building-block-a");
            Check(BuildingValidation.SlugFromName("   ") == "building");
            return Task.CompletedTask;
        });
        add("AR: Firestore outline text round-trips and damaged text is rejected", () =>
        {
            var points = Square("x", 0, 100, 40).Footprint;
            var parsed = BuildingValidation.ParseFootprint(BuildingValidation.FormatFootprint(points));
            Check(parsed.Count == 4);
            Near(GeoMath.DistanceMeters(parsed[2], points[2]), 0, 0.02);
            Check(BuildingValidation.ParseFootprint("1,2;oops").Count == 0);
            return Task.CompletedTask;
        });
        add("AR: GPS averaging weights good fixes and ignores bad ones", () =>
        {
            var sampler = new LocationSampler();
            sampler.Add(At(0, 0), 4); sampler.Add(At(2, 0), 4); sampler.Add(At(200, 0), 60);
            var (point, accuracy) = sampler.Result()!.Value;
            Near(GeoMath.ToLocal(Campus, point).X, 1, 0.01);
            Check(accuracy is >= 2 and <= 4);
            return Task.CompletedTask;
        });
        add("AR: position filter smooths GPS jitter", () =>
        {
            var filter = new PositionFilter();
            var t = DateTimeOffset.UnixEpoch;
            for (var i = 0; i < 20; i++) filter.Add(At(i % 2 == 0 ? 8 : -8, 0), 8, t.AddSeconds(i * 0.2));
            Check(Math.Abs(GeoMath.ToLocal(Campus, filter.Position!.Value).X) < 4);
            return Task.CompletedTask;
        });
        add("AR: offline first launch uses the bundled buildings", async () =>
        {
            var (repo, remote, local) = Repo(online: false);
            var result = await repo.GetBuildingsAsync();
            Check(result.Source == BuildingSource.Bundled && result.Buildings.Count == 1 && remote.Reads == 0);
        });
        add("AR: a Firestore read replaces the bundle and is saved on the phone", async () =>
        {
            var (repo, remote, local) = Repo();
            remote.Buildings = new[] { Square("a", 0, 100, 40), Square("b", 100, 0, 30) };
            var result = await repo.GetBuildingsAsync();
            Check(result.Source == BuildingSource.Firestore && result.Buildings.Count == 2);
            Check(BuildingRepository.Parse(local.Cache).Count == 2);
        });
        add("AR: when Firestore fails the phone's saved copy is used before the bundle", async () =>
        {
            var (repo, remote, local) = Repo();
            local.Cache = JsonSerializer.Serialize(new List<BuildingAnchor> { Square("a", 0, 100, 40), Square("b", 0, 300, 40) }, BuildingJsonContext.Default.ListBuildingAnchor);
            remote.Fail = true;
            var result = await repo.GetBuildingsAsync();
            Check(result.Source == BuildingSource.DeviceCache && result.Buildings.Count == 2);
        });
        add("AR: invalid buildings are dropped one by one", async () =>
        {
            var (repo, remote, _) = Repo();
            remote.Buildings = new[] { Square("ok", 0, 100, 40), Square("ok2", 0, 300, 40) with { Name = "" } };
            Check((await repo.GetBuildingsAsync()).Buildings.Single().Id == "ok");
        });
        add("AR: saving needs Firebase and a valid building", async () =>
        {
            var (repo, remote, _) = Repo();
            await Throws<InvalidOperationException>(() => repo.SaveAsync(Square("main", 0, 100, 40) with { Name = "" }));
            remote.IsConfigured = false;
            await Throws<InvalidOperationException>(() => repo.SaveAsync(Square("main", 0, 100, 40)));
        });
        add("AR: a saved building is immediately visible locally", async () =>
        {
            var (repo, remote, local) = Repo();
            await repo.GetBuildingsAsync();
            await repo.SaveAsync(Square("new", 0, 100, 40));
            Check(remote.Saved.Single().Id == "new");
            Check((await repo.GetBuildingsAsync()).Buildings.Any(b => b.Id == "new"));
        });
        add("AR: the bundled buildings.json file is valid JSON", () =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "buildings.json");
            var json = File.ReadAllText(path);
            Check(JsonSerializer.Deserialize(json, BuildingJsonContext.Default.ListBuildingAnchor) is not null);
            return Task.CompletedTask;
        });
    }

    static GeoPoint At(double east, double north) => GeoMath.FromLocal(Campus, new GeoMath.Vec2(east, north));

    /// <summary>A square building of the given size, centred east/north metres from the test origin.</summary>
    static BuildingAnchor Square(string id, double east, double north, double size)
    {
        var h = size / 2;
        var corners = new[] { At(east - h, north - h), At(east + h, north - h), At(east + h, north + h), At(east - h, north + h) };
        var centre = At(east, north);
        return new BuildingAnchor
        {
            Id = id, Name = "Building " + id, CenterLat = centre.Lat, CenterLng = centre.Lng,
            RadiusMeters = size, Footprint = corners, UpdatedAtUnixMs = 1,
        };
    }

    static (BuildingRepository Repo, FakeBuildingRemote Remote, FakeBuildingLocal Local) Repo(bool online = true)
    {
        var remote = new FakeBuildingRemote();
        var local = new FakeBuildingLocal
        {
            IsInternetAvailable = online,
            Bundled = JsonSerializer.Serialize(new List<BuildingAnchor> { Square("bundled", 0, 100, 40) }, BuildingJsonContext.Default.ListBuildingAnchor),
        };
        return (new BuildingRepository(remote, local), remote, local);
    }

    static (double, double, double, double) Multiply((double X, double Y, double Z, double W) a, (double X, double Y, double Z, double W) b) => (
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    static void Near(double actual, double expected, double tolerance)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"Expected {expected} ± {tolerance}, got {actual}.");
    }

    static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }

    static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (Exception exception) when (exception is T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}

sealed class FakeBuildingRemote : IBuildingRemoteStore
{
    public bool IsConfigured { get; set; } = true;
    public bool Fail { get; set; }
    public int Reads { get; private set; }
    public IReadOnlyList<BuildingAnchor> Buildings { get; set; } = Array.Empty<BuildingAnchor>();
    public List<BuildingAnchor> Saved { get; } = new();

    public Task<IReadOnlyList<BuildingAnchor>> ReadAllAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Fail ? Task.FromException<IReadOnlyList<BuildingAnchor>>(new IOException("offline")) : Task.FromResult(Buildings);
    }

    public Task SaveAsync(BuildingAnchor building) { Saved.Add(building); return Task.CompletedTask; }

    public Task DeleteAsync(string id) => Task.CompletedTask;
}

sealed class FakeBuildingLocal : IBuildingLocalStore
{
    public bool IsInternetAvailable { get; set; } = true;
    public string? Bundled { get; set; }
    public string? Cache { get; set; }
    public Task<string?> ReadBundledAsync() => Task.FromResult(Bundled);
    public Task<string?> ReadCacheAsync() => Task.FromResult(Cache);
    public Task WriteCacheAsync(string json) { Cache = json; return Task.CompletedTask; }
}
