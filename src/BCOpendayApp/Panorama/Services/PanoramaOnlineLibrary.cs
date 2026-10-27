using System.Text.Json;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>Online views plus where they came from.</summary>
public sealed record PanoramaOnlineList(IReadOnlyList<PanoramaManifest> Manifests, bool IsFresh, string Message);

/// <summary>
/// Online 360° views with a copy on the phone, so each visitor downloads each view only once and the tour still
/// works offline with whatever was seen before:
///  - the list of views (small) is fetched each time the tour opens and saved; if Firestore is unreachable the
///    saved list is used;
///  - an image is downloaded the first time its view is opened and kept until staff upload a new version
///    (detected by its SHA-256).
/// No MAUI types: the folder is passed in, so it is unit-tested with a fake <see cref="IPanoramaRemoteStore"/>.
/// </summary>
public sealed class PanoramaOnlineLibrary
{
    private const string ListFileName = "online-views.json";
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

    private readonly IPanoramaRemoteStore remote;
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);

    public PanoramaOnlineLibrary(IPanoramaRemoteStore remote, string appDataDirectory)
    {
        this.remote = remote;
        root = Path.Combine(appDataDirectory, "panorama-online");
    }

    public bool IsConfigured => remote.IsConfigured;

    /// <summary>The online views, from Firestore if possible, otherwise the copy saved on this phone.</summary>
    public async Task<PanoramaOnlineList> GetListAsync()
    {
        if (remote.IsConfigured)
        {
            try
            {
                using var timeout = new CancellationTokenSource(ListTimeout);
                var fresh = await remote.ListAsync(timeout.Token).ConfigureAwait(false);
                await SaveListAsync(fresh).ConfigureAwait(false);
                return new PanoramaOnlineList(fresh, true, string.Empty);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Online views unavailable: {exception.Message}");
            }
        }

        var saved = await LoadListAsync().ConfigureAwait(false);
        return new PanoramaOnlineList(
            saved,
            false,
            remote.IsConfigured ? "Offline - showing the online views this phone has seen before." : string.Empty);
    }

    /// <summary>Path of the image on this phone, downloading it first if needed.</summary>
    public async Task<string> GetImagePathAsync(PanoramaManifest manifest)
    {
        var path = ImagePath(manifest);
        if (File.Exists(path))
        {
            return path;
        }

        if (!remote.IsConfigured)
        {
            throw new InvalidOperationException("This view has not been downloaded to this phone yet.");
        }

        using var timeout = new CancellationTokenSource(DownloadTimeout);
        var image = await remote.DownloadAsync(manifest, timeout.Token).ConfigureAwait(false);
        await StoreImageAsync(manifest, image).ConfigureAwait(false);
        return path;
    }

    /// <summary>Staff: puts a view online and keeps the uploaded image as this phone's cached copy.</summary>
    public async Task<PanoramaManifest> UploadAsync(PanoramaDraft draft, byte[] image, string uid, IProgress<double>? progress = null)
    {
        if (image.LongLength > PanoramaChunks.MaxImageBytes)
        {
            throw new InvalidOperationException(
                $"This image is {image.LongLength / (1024d * 1024d):0.0} MB; online views can be at most "
                + $"{PanoramaChunks.MaxImageBytes / (1024d * 1024d):0.0} MB.");
        }

        var manifest = await remote.UploadAsync(draft, image, uid, progress).ConfigureAwait(false);
        await StoreImageAsync(manifest, image).ConfigureAwait(false);

        var list = (await LoadListAsync().ConfigureAwait(false)).Where(m => m.Id != manifest.Id).Append(manifest).ToList();
        await SaveListAsync(list).ConfigureAwait(false);
        return manifest;
    }

    /// <summary>Staff: removes a view for everyone (and from this phone's cache).</summary>
    public async Task DeleteAsync(PanoramaManifest manifest)
    {
        await remote.DeleteAsync(manifest).ConfigureAwait(false);
        var list = (await LoadListAsync().ConfigureAwait(false)).Where(m => m.Id != manifest.Id).ToList();
        await SaveListAsync(list).ConfigureAwait(false);
        DeleteImages(manifest.Id);
    }

    private string ImagePath(PanoramaManifest manifest) =>
        Path.Combine(root, $"{manifest.Id}.{manifest.Sha256[..16]}{(manifest.MimeType == "image/png" ? ".png" : ".jpg")}");

    private async Task StoreImageAsync(PanoramaManifest manifest, byte[] image)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(root);
            DeleteImages(manifest.Id); // older versions of this view
            var path = ImagePath(manifest);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, image).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private void DeleteImages(string id)
    {
        if (!Directory.Exists(root) || !PanoramaLocalFiles.IsSafeDraftId(id))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, id + ".*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException exception)
            {
                System.Diagnostics.Debug.WriteLine($"Could not delete cached view: {exception.Message}");
            }
        }
    }

    private async Task<List<PanoramaManifest>> LoadListAsync()
    {
        var path = Path.Combine(root, ListFileName);
        if (!File.Exists(path))
        {
            return new List<PanoramaManifest>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return (JsonSerializer.Deserialize(json, PanoramaDraftJsonContext.Default.ListPanoramaManifest) ?? new())
                .Where(PanoramaChunks.IsValid)
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Saved online view list unreadable: {exception.Message}");
            return new List<PanoramaManifest>();
        }
    }

    private async Task SaveListAsync(IEnumerable<PanoramaManifest> manifests)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, ListFileName);
            var temporary = path + ".tmp";
            var json = JsonSerializer.Serialize(manifests.ToList(), PanoramaDraftJsonContext.Default.ListPanoramaManifest);
            await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }
}
