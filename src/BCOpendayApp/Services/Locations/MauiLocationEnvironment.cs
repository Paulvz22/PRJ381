using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace BCOpendayApp.Services;

public sealed class MauiLocationEnvironment : ILocationEnvironment
{
    public bool IsInternetAvailable => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public Task<Stream> OpenBundledLocationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return FileSystem.Current.OpenAppPackageFileAsync("locations.json");
    }
}
