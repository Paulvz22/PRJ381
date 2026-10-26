using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

string json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "locations.json"));
var bundle = JsonSerializer.Deserialize(json, LocationJsonContext.Default.ListHotspot)!;
var live = bundle.Select(item => item with { Name = "Live " + item.Name }).ToArray();
var tests = new List<(string Name, Func<Task> Run)>();
void Add(string name, Func<Task> test) => tests.Add((name, test));
(FakeEnvironment Env, FakeRemote Remote, LocationService Service) Create(bool online = true, bool configured = true)
{
    var env = new FakeEnvironment(json) { IsInternetAvailable = online };
    var remote = new FakeRemote { IsConfigured = configured, Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(live) };
    return (env, remote, new LocationService(remote, env));
}

Add("first launch offline reads all five bundled locations", async () =>
{
    var (_, remote, service) = Create(online: false);
    var result = await service.GetLocationsAsync();
    Check(result.Source == LocationDataSource.BundledJson && result.Locations.Count == 5);
    Check(remote.Calls == 0);
});
Add("missing Firebase configuration never calls the remote service", async () =>
{
    var (_, remote, service) = Create(configured: false);
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.BundledJson);
    Check(remote.Calls == 0);
});
Add("all five remote records replace the bundled records", async () =>
{
    var (_, _, service) = Create();
    var result = await service.GetLocationsAsync();
    Check(result.Source == LocationDataSource.Firestore);
    Check(result.Locations.All(item => item.Name.StartsWith("Live ", StringComparison.Ordinal)));
});
Add("partial remote data keeps all five stops", async () =>
{
    var (_, remote, service) = Create();
    remote.Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(new[] { live[0] });
    var result = await service.GetLocationsAsync();
    Check(result.Source == LocationDataSource.Mixed && result.Locations.Count == 5);
    Check(result.Locations[0].Name == live[0].Name && result.Locations[1].Name == bundle[1].Name);
});
Add("invalid remote document falls back individually", async () =>
{
    var (_, remote, service) = Create();
    var bad = live.ToArray(); bad[0] = bad[0] with { Name = "" };
    remote.Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(bad);
    var result = await service.GetLocationsAsync();
    Check(result.Source == LocationDataSource.Mixed && result.Locations[0].Name == bundle[0].Name);
});
Add("duplicate valid remote IDs are not chosen arbitrarily", async () =>
{
    var (_, remote, service) = Create();
    remote.Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(new[] { live[0], live[0] });
    var result = await service.GetLocationsAsync();
    Check(result.Source == LocationDataSource.BundledJson);
});
Add("empty remote collection returns bundled fallback", async () =>
{
    var (_, remote, service) = Create();
    remote.Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(Array.Empty<Hotspot>());
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.BundledJson);
});
Add("permission error returns bundled fallback", async () =>
{
    var (_, remote, service) = Create();
    remote.Handler = _ => Task.FromException<IReadOnlyList<Hotspot>>(new UnauthorizedAccessException());
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.BundledJson);
});
Add("hung server is bounded by the timeout", async () =>
{
    var (env, remote, _) = Create();
    remote.Handler = async token => { await Task.Delay(Timeout.Infinite, token); return live; };
    var service = new LocationService(remote, env, TimeSpan.FromMilliseconds(50));
    var watch = Stopwatch.StartNew();
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.BundledJson);
    Check(watch.Elapsed < TimeSpan.FromSeconds(3));
});
Add("caller cancellation is propagated, not reported as offline", async () =>
{
    var (_, _, service) = Create();
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await Throws<OperationCanceledException>(() => service.GetLocationsAsync(cancellationToken: cancellation.Token));
});
Add("an in-flight caller cancellation does not commit a fallback", async () =>
{
    var (_, remote, service) = Create();
    remote.Handler = async token => { await Task.Delay(Timeout.Infinite, token); return live; };
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    await Throws<OperationCanceledException>(() => service.GetLocationsAsync(cancellationToken: cancellation.Token));
    remote.Handler = _ => Task.FromResult<IReadOnlyList<Hotspot>>(live);
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.Firestore);
});
Add("successful remote data is reused during this app session", async () =>
{
    var (_, remote, service) = Create();
    await service.GetLocationsAsync();
    remote.Handler = _ => Task.FromException<IReadOnlyList<Hotspot>>(new IOException("Server unavailable"));
    var fallback = await service.GetLocationsAsync(forceRefresh: true);
    Check(fallback.Source == LocationDataSource.MemoryCache && fallback.Locations[0].Name == live[0].Name);
});
Add("reconnection retries immediately instead of waiting for fallback TTL", async () =>
{
    var (env, remote, service) = Create(online: false);
    await service.GetLocationsAsync(); env.IsInternetAvailable = true;
    Check((await service.GetLocationsAsync()).Source == LocationDataSource.Firestore);
    Check(remote.Calls == 1);
});
Add("concurrent page reads share one cached catalogue", async () =>
{
    var (env, remote, service) = Create();
    remote.Handler = async token => { await Task.Delay(20, token); return live; };
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetLocationsAsync()));
    Check(remote.Calls == 1 && env.Opens == 1);
});
Add("manual refresh bypasses the normal cache TTL", async () =>
{
    var (_, remote, service) = Create();
    await service.GetLocationsAsync(); await service.GetLocationsAsync();
    Check(remote.Calls == 1);
    await service.GetLocationsAsync(forceRefresh: true); Check(remote.Calls == 2);
});
Add("ID lookup is case-insensitive and handles unknown IDs", async () =>
{
    var (_, _, service) = Create(online: false);
    Check((await service.GetLocationAsync("  LIBRARY  "))?.Id == "library");
    Check(await service.GetLocationAsync("unknown") is null);
});
Add("broken bundle reports a packaging error", async () =>
{
    var (env, _, service) = Create(online: false); env.Json = "[]";
    await Throws<InvalidDataException>(() => service.GetLocationsAsync());
});
Add("missing map position is not a fabricated zero coordinate", () =>
{
    Check(bundle.Where(item => item.Id == "lecture").All(item => !item.HasMapPosition && item.X is null && item.Y is null));
    return Task.CompletedTask;
});
Add("invalid and incomplete coordinates are rejected", () =>
{
    var library = bundle.Single(item => item.Id == "library");
    Check(!HotspotValidation.IsValid(library with { X = double.NaN }));
    Check(!HotspotValidation.IsValid(library with { Y = 2 }));
    Check(!HotspotValidation.IsValid(library with { X = null }));
    Check(HotspotValidation.IsValid(library with { X = 0, Y = 1 }));
    return Task.CompletedTask;
});
Add("map pins retain normalized centers as the image grows", () =>
{
    var first = MapGeometry.PinPosition(0.5, 0.25, 400, 800);
    var zoomed = MapGeometry.PinPosition(0.5, 0.25, 800, 1600);
    Check(first == (178d, 178d) && zoomed == (378d, 378d));
    return Task.CompletedTask;
});
Add("edge pins remain inside the map canvas", () =>
{
    Check(MapGeometry.PinPosition(0, 0, 400, 800) == (0d, 0d));
    Check(MapGeometry.PinPosition(1, 1, 400, 800) == (356d, 756d));
    return Task.CompletedTask;
});
Add("generated JSON excludes computed UI-only properties", () =>
{
    var roundTrip = JsonSerializer.Serialize(bundle, LocationJsonContext.Default.ListHotspot);
    Check(!roundTrip.Contains("hasMapPosition", StringComparison.Ordinal));
    Check(!roundTrip.Contains("details", StringComparison.Ordinal));
    Check(JsonSerializer.Deserialize(roundTrip, LocationJsonContext.Default.ListHotspot)!.Count == 5);
    return Task.CompletedTask;
});

ArTests.Register(Add);
NavTests.Register(Add);
NavRouterTests.Register(Add);
SamplerTests.Register(Add);
PanoramaTests.Register(Add);
PanoramaCaptureTests.Register(Add);
PanoramaOnlineTests.Register(Add);
CampusLocationTests.Register(Add);

int failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; Console.WriteLine("FAIL " + name + ": " + exception); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (Exception exception) when (exception is T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

sealed class FakeEnvironment(string json) : ILocationEnvironment
{
    public string Json { get; set; } = json;
    public bool IsInternetAvailable { get; set; } = true;
    public int Opens { get; private set; }
    public Task<Stream> OpenBundledLocationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Opens++;
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Json)));
    }
}
sealed class FakeRemote : ILocationRemoteSource
{
    public bool IsConfigured { get; set; } = true;
    public int Calls { get; private set; }
    public Func<CancellationToken, Task<IReadOnlyList<Hotspot>>> Handler { get; set; } =
        _ => Task.FromResult<IReadOnlyList<Hotspot>>(Array.Empty<Hotspot>());
    public Task<IReadOnlyList<Hotspot>> ReadAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Handler(cancellationToken);
    }
}
