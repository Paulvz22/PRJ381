using System.Diagnostics;
using BCOpendayApp.AR.Data;
using BCOpendayApp.Panorama.Services;

namespace BCOpendayApp.Services.Campus;

public sealed record CampusLocationList(IReadOnlyList<CampusLocation> Locations, string Message);

/// <summary>The campus locations, built automatically from AR building scans and online 360° views.</summary>
public interface ICampusLocationService
{
    /// <summary>Never throws; works offline from what the phone already has.</summary>
    Task<CampusLocationList> GetAllAsync(bool forceRefresh = false);

    Task<CampusLocation?> GetAsync(string id);

    /// <summary>Local file of the location's photo, or null when it has none (or it cannot be fetched).</summary>
    Task<string?> GetPhotoPathAsync(string locationId, bool forceRefresh = false);

    /// <summary>Staff only. <paramref name="jpeg"/> must already be shrunk (see <see cref="LocationPhotoRules"/>).</summary>
    Task SavePhotoAsync(string locationId, byte[] jpeg, string uid);
}

public sealed class CampusLocationService : ICampusLocationService
{
    private static readonly TimeSpan PhotoLifetime = TimeSpan.FromHours(6);
    private static readonly TimeSpan PhotoTimeout = TimeSpan.FromSeconds(15);

    private readonly IBuildingRepository buildings;
    private readonly PanoramaOnlineLibrary panoramas;
    private readonly ILocationPhotoRemoteStore photos;
    private readonly string photoFolder;
    private IReadOnlyList<CampusLocation> last = Array.Empty<CampusLocation>();

    public CampusLocationService(
        IBuildingRepository buildings,
        PanoramaOnlineLibrary panoramas,
        ILocationPhotoRemoteStore photos,
        string appDataDirectory)
    {
        this.buildings = buildings;
        this.panoramas = panoramas;
        this.photos = photos;
        photoFolder = Path.Combine(appDataDirectory, "location-photos");
    }

    public async Task<CampusLocationList> GetAllAsync(bool forceRefresh = false)
    {
        var buildingTask = buildings.GetBuildingsAsync(forceRefresh);
        var viewTask = panoramas.GetListAsync();
        await Task.WhenAll(buildingTask, viewTask).ConfigureAwait(false);

        last = CampusLocationCatalog.Build(buildingTask.Result.Buildings, viewTask.Result.Manifests);
        var message = viewTask.Result.IsFresh
            ? buildingTask.Result.Message
            : $"{buildingTask.Result.Message} {viewTask.Result.Message}".Trim();
        return new CampusLocationList(last, message);
    }

    public async Task<CampusLocation?> GetAsync(string id)
    {
        var found = last.FirstOrDefault(location => location.Id == id);
        if (found is not null)
        {
            return found;
        }

        var list = await GetAllAsync().ConfigureAwait(false);
        return list.Locations.FirstOrDefault(location => location.Id == id);
    }

    public async Task<string?> GetPhotoPathAsync(string locationId, bool forceRefresh = false)
    {
        if (!PanoramaChunks.IsValidLocationId(locationId) || locationId.Length == 0)
        {
            return null;
        }

        var file = Path.Combine(photoFolder, locationId + ".jpg");
        var none = Path.Combine(photoFolder, locationId + ".none");
        var cached = File.Exists(file) ? file : File.Exists(none) ? none : null;
        if (!forceRefresh && cached is not null && DateTime.UtcNow - File.GetLastWriteTimeUtc(cached) < PhotoLifetime)
        {
            return cached == file ? file : null;
        }

        if (!photos.IsConfigured)
        {
            return File.Exists(file) ? file : null;
        }

        try
        {
            using var timeout = new CancellationTokenSource(PhotoTimeout);
            var photo = await photos.GetAsync(locationId, timeout.Token).ConfigureAwait(false);
            Directory.CreateDirectory(photoFolder);
            if (LocationPhotoRules.Decode(photo) is not { } bytes)
            {
                File.Delete(file);
                await File.WriteAllTextAsync(none, string.Empty).ConfigureAwait(false);
                return null;
            }

            await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);
            File.Delete(none);
            return file;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Location photo not loaded: {exception.Message}");
            return File.Exists(file) ? file : null; // offline: keep showing the old photo
        }
    }

    public async Task SavePhotoAsync(string locationId, byte[] jpeg, string uid)
    {
        if (!photos.IsConfigured)
        {
            throw new InvalidOperationException("Firebase is not set up in this build, so photos cannot be uploaded.");
        }

        var photo = LocationPhotoRules.Create(jpeg, uid, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await photos.SetAsync(locationId, photo).ConfigureAwait(false);

        Directory.CreateDirectory(photoFolder);
        await File.WriteAllBytesAsync(Path.Combine(photoFolder, locationId + ".jpg"), jpeg).ConfigureAwait(false);
        File.Delete(Path.Combine(photoFolder, locationId + ".none"));
    }
}
