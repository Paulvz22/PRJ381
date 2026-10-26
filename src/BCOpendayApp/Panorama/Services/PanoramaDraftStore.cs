using System.Diagnostics;
using System.Text.Json;
using BCOpendayApp.Panorama.Models;
using Microsoft.Maui.Storage;

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// Stores accepted panoramas in app-private storage. There is intentionally no Firestore or upload code here:
/// drafts never leave this phone.
/// </summary>
public sealed class PanoramaDraftStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root;
    private readonly string manifestPath;

    public PanoramaDraftStore()
        : this(FileSystem.Current.AppDataDirectory)
    {
    }

    internal PanoramaDraftStore(string appDataDirectory)
    {
        root = Path.Combine(appDataDirectory, PanoramaLocalFiles.DraftsFolderName);
        manifestPath = Path.Combine(root, PanoramaLocalFiles.ManifestFileName);
    }

    public async Task<IReadOnlyList<PanoramaDraft>> LoadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PanoramaDraft?> FindAsync(string? draftId)
    {
        if (!PanoramaLocalFiles.IsSafeDraftId(draftId))
        {
            return null;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var drafts = await LoadCoreAsync().ConfigureAwait(false);
            return drafts.FirstOrDefault(draft => string.Equals(draft.Id, draftId, StringComparison.Ordinal));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Copies the accepted preview into app-private storage and records its metadata.</summary>
    public async Task<PanoramaDraft> AcceptAsync(
        string temporaryFilePath,
        string originalFileName,
        PanoramaImageInfo image)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(temporaryFilePath) || !File.Exists(temporaryFilePath))
            {
                throw new InvalidOperationException("The imported preview is no longer available. Choose the image again.");
            }

            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            var fileName = PanoramaLocalFiles.CreateDraftFileName(id, image.Format);
            var destination = Path.Combine(root, fileName);

            try
            {
                File.Copy(temporaryFilePath, destination, overwrite: false);
            }
            catch
            {
                TryDelete(destination);
                throw;
            }

            var draft = new PanoramaDraft
            {
                Id = id,
                DisplayName = NormalizeDisplayName(originalFileName),
                FileName = fileName,
                Width = image.Width,
                Height = image.Height,
                Projection = PanoramaDraft.EquirectangularProjection,
                IsFullSphere = true,
                CaptureMethod = PanoramaDraft.GalleryImportMethod,
                FileSizeBytes = image.FileSizeBytes,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            var drafts = await LoadCoreAsync().ConfigureAwait(false);
            drafts.Insert(0, draft);

            try
            {
                await SaveCoreAsync(drafts).ConfigureAwait(false);
            }
            catch
            {
                // The manifest is the source of truth. Do not leave an accepted-looking orphan behind.
                TryDelete(destination);
                throw;
            }

            TryDelete(temporaryFilePath);
            return draft;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Moves a panorama stitched on this phone (a JPEG in the cache folder) into the drafts folder and records it.
    /// <paramref name="template"/> carries the capture metadata; id, file name and dates are filled in here.
    /// </summary>
    public async Task<PanoramaDraft> SaveCapturedAsync(string stitchedJpegPath, PanoramaDraft template)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(stitchedJpegPath) || !File.Exists(stitchedJpegPath))
            {
                throw new InvalidOperationException("The stitched panorama is no longer available. Capture it again.");
            }

            Directory.CreateDirectory(root);
            var id = Guid.NewGuid().ToString("N");
            var fileName = id + ".jpg";
            var destination = Path.Combine(root, fileName);

            var draft = template with
            {
                Id = id,
                FileName = fileName,
                DisplayName = CleanTypedName(template.DisplayName),
                Projection = PanoramaDraft.EquirectangularProjection,
                IsFullSphere = false,
                CaptureMethod = PanoramaDraft.InAppCaptureMethod,
                FileSizeBytes = new FileInfo(stitchedJpegPath).Length,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            if (!PanoramaDraftValidation.IsValid(draft))
            {
                throw new InvalidDataException("The captured panorama does not have valid size or location data.");
            }

            try
            {
                File.Copy(stitchedJpegPath, destination, overwrite: false);
            }
            catch
            {
                TryDelete(destination);
                throw;
            }

            var drafts = await LoadCoreAsync().ConfigureAwait(false);
            drafts.Insert(0, draft);

            try
            {
                await SaveCoreAsync(drafts).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(destination);
                throw;
            }

            TryDelete(stitchedJpegPath);
            return draft;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(PanoramaDraft draft)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var drafts = await LoadCoreAsync().ConfigureAwait(false);
            if (drafts.RemoveAll(item => string.Equals(item.Id, draft.Id, StringComparison.Ordinal)) == 0)
            {
                return;
            }

            await SaveCoreAsync(drafts).ConfigureAwait(false);
            TryDelete(GetImagePath(draft));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Resolves a metadata file name to a path inside the drafts folder, rejecting path traversal.</summary>
    public string GetImagePath(PanoramaDraft draft)
    {
        if (!PanoramaDraftValidation.IsValid(draft))
        {
            throw new InvalidDataException("The local panorama metadata is invalid.");
        }

        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, draft.FileName));
        if (!path.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The local panorama path is invalid.");
        }

        return path;
    }

    private async Task<List<PanoramaDraft>> LoadCoreAsync()
    {
        if (!File.Exists(manifestPath))
        {
            return new List<PanoramaDraft>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize(json, PanoramaDraftJsonContext.Default.ListPanoramaDraft)
                ?? new List<PanoramaDraft>();

            return parsed
                .Where(PanoramaDraftValidation.IsValid)
                .Where(HasImageFile)
                .ToList();
        }
        catch (JsonException exception)
        {
            Debug.WriteLine($"Panorama draft manifest is corrupt: {exception.Message}");
            QuarantineManifest();
            return new List<PanoramaDraft>();
        }
    }

    private bool HasImageFile(PanoramaDraft draft)
    {
        try
        {
            return File.Exists(GetImagePath(draft));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return false;
        }
    }

    private async Task SaveCoreAsync(List<PanoramaDraft> drafts)
    {
        Directory.CreateDirectory(root);
        var temporaryManifest = manifestPath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(drafts, PanoramaDraftJsonContext.Default.ListPanoramaDraft);
            await File.WriteAllTextAsync(temporaryManifest, json).ConfigureAwait(false);
            File.Move(temporaryManifest, manifestPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryManifest);
            throw;
        }
    }

    private void QuarantineManifest()
    {
        try
        {
            var quarantine = manifestPath + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            File.Move(manifestPath, quarantine, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not quarantine the panorama draft manifest: {exception.Message}");
        }
    }

    private static string NormalizeDisplayName(string originalFileName)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty);
        name = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return "Imported panorama";
        }

        return name.Length <= 100 ? name : name[..100].TrimEnd();
    }

    /// <summary>A name typed on the capture screen (no file extension to strip).</summary>
    private static string CleanTypedName(string typed)
    {
        var name = new string((typed ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return "Captured panorama";
        }

        return name.Length <= 100 ? name : name[..100].TrimEnd();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete panorama file '{path}': {exception.Message}");
        }
    }
}
