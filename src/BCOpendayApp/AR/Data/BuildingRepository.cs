using System.Diagnostics;
using System.Text.Json;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Data;

public enum BuildingSource { Firestore, DeviceCache, Bundled, None }

public sealed record BuildingLoadResult(IReadOnlyList<BuildingAnchor> Buildings, BuildingSource Source, string Message);

public interface IBuildingRepository
{
    /// <summary>Never throws. Order: Firestore → copy saved on this phone → bundled buildings.json.</summary>
    Task<BuildingLoadResult> GetBuildingsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Validates, uploads (staff account required) and updates the local copy. Throws with a readable message.</summary>
    Task SaveAsync(BuildingAnchor building);

    Task DeleteAsync(string id);

    /// <summary>All currently known buildings as JSON (same format as Resources/Raw/buildings.json).</summary>
    string ExportJson(IEnumerable<BuildingAnchor> buildings);
}

/// <summary>
/// Gives visitors building data straight after install: the bundled file works offline on first launch, a fresh
/// Firestore read replaces it when online, and that read is saved on the phone so the next offline launch has
/// the newest data. Invalid entries are dropped individually instead of breaking the AR view.
/// </summary>
public sealed class BuildingRepository : IBuildingRepository
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RemoteTimeout = TimeSpan.FromSeconds(8);

    private readonly IBuildingRemoteStore remote;
    private readonly IBuildingLocalStore local;
    private readonly SemaphoreSlim gate = new(1, 1);
    private BuildingLoadResult? current;
    private DateTimeOffset loadedAt;

    public BuildingRepository(IBuildingRemoteStore remote, IBuildingLocalStore local)
    {
        this.remote = remote;
        this.local = local;
    }

    public async Task<BuildingLoadResult> GetBuildingsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && current is { Source: BuildingSource.Firestore } && DateTimeOffset.UtcNow - loadedAt < CacheLifetime)
            {
                return current;
            }

            string? remoteProblem = null;
            if (remote.IsConfigured && local.IsInternetAvailable)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RemoteTimeout);
                    var buildings = Clean(await remote.ReadAllAsync(timeout.Token));
                    await SaveCacheAsync(buildings);
                    return Remember(new BuildingLoadResult(buildings, BuildingSource.Firestore,
                        $"{buildings.Count} buildings loaded from Firestore."));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Building download failed: {exception}");
                    remoteProblem = "Could not reach Firestore.";
                }
            }
            else
            {
                remoteProblem = remote.IsConfigured ? "Offline." : "Firebase is not configured in this build.";
            }

            var cached = Parse(await SafeRead(local.ReadCacheAsync));
            if (cached.Count > 0)
            {
                return Remember(new BuildingLoadResult(cached, BuildingSource.DeviceCache,
                    $"{remoteProblem} Using {cached.Count} buildings saved on this phone."));
            }

            var bundled = Parse(await SafeRead(local.ReadBundledAsync));
            return Remember(bundled.Count > 0
                ? new BuildingLoadResult(bundled, BuildingSource.Bundled, $"{remoteProblem} Using {bundled.Count} built-in buildings.")
                : new BuildingLoadResult(bundled, BuildingSource.None, $"{remoteProblem} No buildings have been scanned yet."));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(BuildingAnchor building)
    {
        if (BuildingValidation.Problem(building) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        if (!remote.IsConfigured)
        {
            throw new InvalidOperationException("Firebase is not configured in this build, so buildings cannot be uploaded.");
        }

        await remote.SaveAsync(building);
        await UpdateLocalAsync(list => list.Where(b => b.Id != building.Id).Append(building));
    }

    public async Task DeleteAsync(string id)
    {
        if (!remote.IsConfigured)
        {
            throw new InvalidOperationException("Firebase is not configured in this build.");
        }

        await remote.DeleteAsync(id);
        await UpdateLocalAsync(list => list.Where(b => b.Id != id));
    }

    public string ExportJson(IEnumerable<BuildingAnchor> buildings) =>
        JsonSerializer.Serialize(buildings.OrderBy(b => b.Name).ToList(), BuildingJsonContext.Default.ListBuildingAnchor);

    private async Task UpdateLocalAsync(Func<IEnumerable<BuildingAnchor>, IEnumerable<BuildingAnchor>> change)
    {
        await gate.WaitAsync();
        try
        {
            var baseList = current?.Buildings ?? Array.Empty<BuildingAnchor>();
            var updated = change(baseList).OrderBy(b => b.Name).ToList();
            await SaveCacheAsync(updated);
            Remember(new BuildingLoadResult(updated, BuildingSource.Firestore, $"{updated.Count} buildings (just updated)."));
        }
        finally
        {
            gate.Release();
        }
    }

    private BuildingLoadResult Remember(BuildingLoadResult result)
    {
        current = result;
        loadedAt = DateTimeOffset.UtcNow;
        return result;
    }

    private async Task SaveCacheAsync(IReadOnlyList<BuildingAnchor> buildings)
    {
        try
        {
            await local.WriteCacheAsync(ExportJson(buildings));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not save the building cache: {exception.Message}");
        }
    }

    /// <summary>A hand-edited file can contain "summary": null etc.; treat missing text as empty.</summary>
    private static BuildingAnchor Normalize(BuildingAnchor b) => b with
    {
        Id = b.Id ?? string.Empty,
        Name = b.Name ?? string.Empty,
        Summary = b.Summary ?? string.Empty,
        Description = b.Description ?? string.Empty,
        StopId = b.StopId ?? string.Empty,
        UpdatedBy = b.UpdatedBy ?? string.Empty,
        Footprint = b.Footprint ?? Array.Empty<GeoPoint>(),
    };

    private static async Task<string?> SafeRead(Func<Task<string?>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception.Message);
            return null;
        }
    }

    /// <summary>Parses JSON and drops invalid or duplicate entries (keeps the app working with a bad file).</summary>
    public static IReadOnlyList<BuildingAnchor> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<BuildingAnchor>();
        }

        try
        {
            return Clean(JsonSerializer.Deserialize(json, BuildingJsonContext.Default.ListBuildingAnchor) ?? new());
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            Debug.WriteLine($"Building JSON is invalid: {exception.Message}");
            return Array.Empty<BuildingAnchor>();
        }
    }

    private static IReadOnlyList<BuildingAnchor> Clean(IEnumerable<BuildingAnchor?> buildings) =>
        buildings
            .OfType<BuildingAnchor>()
            .Select(Normalize)
            .Where(BuildingValidation.IsValid)
            .GroupBy(b => b.Id)
            .Select(g => g.OrderByDescending(b => b.UpdatedAtUnixMs).First())
            .OrderBy(b => b.Name)
            .ToList();
}
