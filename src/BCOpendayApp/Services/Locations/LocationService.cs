using System.Diagnostics;
using System.Text.Json;
using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

/// <summary>
/// Authenticated Firestore -> last successful in-memory catalogue -> bundled JSON.
/// Partial remote catalogues are completed from the bundle, so all five stops remain available.
/// </summary>
public sealed class LocationService : ILocationService
{
    private readonly ILocationRemoteSource remote;
    private readonly ILocationEnvironment environment;
    private readonly TimeSpan remoteTimeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<Hotspot>? bundled;
    private LocationLoadResult? current;
    private LocationLoadResult? lastGood;
    private DateTimeOffset nextAttempt;
    private bool wasOnline;

    public LocationService(ILocationRemoteSource remote, ILocationEnvironment environment)
        : this(remote, environment, TimeSpan.FromSeconds(6)) { }

    public LocationService(ILocationRemoteSource remote, ILocationEnvironment environment, TimeSpan remoteTimeout)
    {
        if (remoteTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(remoteTimeout));
        this.remote = remote;
        this.environment = environment;
        this.remoteTimeout = remoteTimeout;
    }

    public async Task<LocationLoadResult> GetLocationsAsync(bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            bool online = environment.IsInternetAvailable && remote.IsConfigured;
            if (!forceRefresh && current is not null && online == wasOnline
                && DateTimeOffset.UtcNow < nextAttempt)
                return current;

            if (bundled is null)
            {
                await using var stream = await environment.OpenBundledLocationsAsync(cancellationToken);
                var data = await JsonSerializer.DeserializeAsync(stream, LocationJsonContext.Default.ListHotspot,
                    cancellationToken) ?? throw new InvalidDataException("locations.json is empty.");
                bundled = HotspotValidation.ValidateBundle(data);
            }

            LocationLoadResult result;
            if (!online)
            {
                result = Fallback(!remote.IsConfigured
                    ? "Firebase is not configured for this build."
                    : "No internet connection.");
            }
            else
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(remoteTimeout);
                try
                {
                    // WaitAsync also bounds SDK implementations that do not accept cancellation.
                    var data = await remote.ReadAsync(deadline.Token).WaitAsync(deadline.Token);
                    var (merged, count) = HotspotValidation.Merge(bundled, data);
                    if (count == 0) throw new InvalidDataException("No valid location documents returned.");
                    result = new(merged,
                        count == bundled.Count ? LocationDataSource.Firestore : LocationDataSource.Mixed,
                        count == bundled.Count ? "Locations loaded from Firestore."
                            : $"{count} locations from Firestore; missing or invalid entries use bundled data.",
                        DateTimeOffset.UtcNow);
                    lastGood = result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // Navigation cancellation is NOT an offline failure.
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Location refresh failed ({ex.GetType().Name}): {ex.Message}");
                    result = Fallback(ex is OperationCanceledException or TimeoutException
                        ? "The server took too long to respond."
                        : "Live locations are unavailable (check Firebase setup, sign-in and rules).");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            current = result;
            wasOnline = online;
            nextAttempt = DateTimeOffset.UtcNow.AddSeconds(
                result.Source is LocationDataSource.Firestore or LocationDataSource.Mixed ? 300 : 30);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task<Hotspot?> GetLocationAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var normalized = id.Trim().ToLowerInvariant();
        var catalogue = await GetLocationsAsync(cancellationToken: cancellationToken);
        return catalogue.Locations.FirstOrDefault(x => x.Id == normalized);
    }

    private LocationLoadResult Fallback(string reason) => lastGood is not null
        ? lastGood with { Source = LocationDataSource.MemoryCache,
            Message = reason + " Showing the last successful catalogue from this app session." }
        : new(bundled!, LocationDataSource.BundledJson, reason + " Using bundled offline locations.");
}
