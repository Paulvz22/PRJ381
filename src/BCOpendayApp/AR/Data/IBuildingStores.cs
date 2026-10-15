using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Data;

/// <summary>The cloud copy of the buildings (Firestore). Split out so the repository can be unit-tested.</summary>
public interface IBuildingRemoteStore
{
    /// <summary>True when Firebase started on this device.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<BuildingAnchor>> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>Requires a signed-in staff (scanner) account - Firestore rules reject everyone else.</summary>
    Task SaveAsync(BuildingAnchor building);

    Task DeleteAsync(string id);
}

/// <summary>Files on the phone: the bundled starter list and the last successful download.</summary>
public interface IBuildingLocalStore
{
    bool IsInternetAvailable { get; }

    /// <summary>Resources/Raw/buildings.json shipped inside the app.</summary>
    Task<string?> ReadBundledAsync();

    /// <summary>The copy saved after the last successful Firestore read (survives app restarts).</summary>
    Task<string?> ReadCacheAsync();

    Task WriteCacheAsync(string json);
}
