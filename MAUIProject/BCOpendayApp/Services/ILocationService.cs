using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

/// <summary>One read-only catalogue shared by maps, directory, tour and information pages.</summary>
public interface ILocationService
{
    Task<LocationLoadResult> GetLocationsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<Hotspot?> GetLocationAsync(string id, CancellationToken cancellationToken = default);
}

public enum LocationDataSource { Firestore, Mixed, BundledJson, MemoryCache }

public sealed record LocationLoadResult(
    IReadOnlyList<Hotspot> Locations,
    LocationDataSource Source,
    string Message,
    DateTimeOffset? LastServerRead = null);
