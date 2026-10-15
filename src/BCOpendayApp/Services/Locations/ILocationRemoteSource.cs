using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

public interface ILocationRemoteSource
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<Hotspot>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Small platform boundary so the repository can be tested without a device.</summary>
public interface ILocationEnvironment
{
    bool IsInternetAvailable { get; }
    Task<Stream> OpenBundledLocationsAsync(CancellationToken cancellationToken);
}
