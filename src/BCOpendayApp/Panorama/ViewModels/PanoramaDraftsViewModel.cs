using System.Diagnostics;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace BCOpendayApp.Panorama.ViewModels;

public sealed record PanoramaDraftItem(PanoramaDraft Draft, bool IsOnline = false)
{
    public string DisplayName => Draft.DisplayName;

    public string Badge => (Draft.IsCaptured ? "CAPTURED" : "IMPORTED") + (IsOnline ? " · ONLINE" : " · THIS PHONE ONLY");

    public string UploadText => IsOnline ? "Re-upload" : "Upload";

    public string Details =>
        $"{Draft.Width} × {Draft.Height} · {FormatSize(Draft.FileSizeBytes)} · "
        + DateTimeOffset.FromUnixTimeMilliseconds(Draft.CreatedAtUnixMs).ToLocalTime().ToString("g");

    /// <summary>Whether the viewer can draw arrows to/from this view.</summary>
    public string Placement => Draft.HasLocation && Draft.HasNorthOffset
        ? $"Map position ±{Draft.LocationAccuracyMeters:0} m · arrows to nearby views are automatic"
        : Draft.HasLocation
            ? $"Map position ±{Draft.LocationAccuracyMeters:0} m · direction unknown, so it gets arrows only FROM other views"
            : "No map position · not linked to other views";

    private static string FormatSize(long bytes) => $"{bytes / (1024d * 1024d):0.0} MB";
}

/// <summary>A view that is online (in Firestore), whichever phone uploaded it.</summary>
public sealed record OnlinePanoramaItem(PanoramaManifest Manifest)
{
    public string Name => Manifest.Name;

    public string Details =>
        $"{Manifest.ByteLength / (1024d * 1024d):0.0} MB in {Manifest.ChunkCount} piece(s) · "
        + DateTimeOffset.FromUnixTimeMilliseconds(Manifest.UpdatedAtUnixMs).ToLocalTime().ToString("g")
        + (Manifest.HasLocation ? " · has map position" : " · no map position");
}

public sealed class PanoramaDraftsViewModel : ViewModelBase
{
    private static readonly FilePickerFileType PanoramaFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = new[] { "image/jpeg", "image/png" },
        });

    private readonly PanoramaDraftStore store;
    private readonly IScannerAuthService auth;
    private readonly PanoramaOnlineLibrary online;
    private IReadOnlyList<PanoramaDraftItem> drafts = Array.Empty<PanoramaDraftItem>();
    private IReadOnlyList<OnlinePanoramaItem> onlineViews = Array.Empty<OnlinePanoramaItem>();
    private string onlineStatus = string.Empty;
    private PendingImport? pending;
    private string pendingPreviewPath = string.Empty;
    private string pendingName = string.Empty;
    private string pendingDetails = string.Empty;
    private string statusMessage = "No local panorama drafts yet.";
    private bool isBusy;

    public PanoramaDraftsViewModel(
        PanoramaDraftStore store,
        IScannerAuthService auth,
        PanoramaOnlineLibrary online,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.store = store;
        this.auth = auth;
        this.online = online;
        UploadCommand = CreateCommand<PanoramaDraftItem>(UploadAsync);
        DeleteOnlineCommand = CreateCommand<OnlinePanoramaItem>(DeleteOnlineAsync);

        ImportCommand = CreateCommand(ImportAsync);
        SavePendingCommand = CreateCommand(SavePendingAsync);
        DiscardPendingCommand = CreateCommand(() =>
        {
            DiscardPending();
            StatusMessage = "Preview discarded.";
            return Task.CompletedTask;
        });
        OpenCommand = CreateCommand<PanoramaDraftItem>(OpenAsync);
        DeleteCommand = CreateCommand<PanoramaDraftItem>(DeleteAsync);
        ExportCommand = CreateCommand<PanoramaDraftItem>(ExportAsync);
        CaptureCommand = CreateCommand(() => Navigation.GoToAsync(PanoramaRoutes.Capture));
        OpenTourCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Pano));
        BackCommand = CreateCommand(() => Navigation.GoBackAsync());
    }

    public IReadOnlyList<PanoramaDraftItem> Drafts
    {
        get => drafts;
        private set
        {
            if (SetProperty(ref drafts, value))
            {
                OnPropertyChanged(nameof(HasNoDrafts));
            }
        }
    }

    public bool HasNoDrafts => drafts.Count == 0 && !isBusy;

    public string PendingPreviewPath
    {
        get => pendingPreviewPath;
        private set => SetProperty(ref pendingPreviewPath, value);
    }

    public string PendingName
    {
        get => pendingName;
        private set => SetProperty(ref pendingName, value);
    }

    public string PendingDetails
    {
        get => pendingDetails;
        private set => SetProperty(ref pendingDetails, value);
    }

    public bool HasPending => pending is not null;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(HasNoDrafts));
            }
        }
    }

    public bool IsNotBusy => !isBusy;

    public AsyncCommand ImportCommand { get; }

    public AsyncCommand SavePendingCommand { get; }

    public AsyncCommand DiscardPendingCommand { get; }

    public AsyncCommand OpenCommand { get; }

    public AsyncCommand DeleteCommand { get; }

    public AsyncCommand BackCommand { get; }

    /// <summary>Shares the image + a tour.json scene so the view can be bundled for every visitor.</summary>
    public AsyncCommand ExportCommand { get; }

    /// <summary>Staff: capture a new 360° view with the camera.</summary>
    public AsyncCommand CaptureCommand { get; }

    /// <summary>Opens the whole tour (bundled scenes + online + the ones on this phone, with their arrows).</summary>
    public AsyncCommand OpenTourCommand { get; }

    /// <summary>Puts a view online for every visitor (Firestore).</summary>
    public AsyncCommand UploadCommand { get; }

    /// <summary>Removes an online view for every visitor.</summary>
    public AsyncCommand DeleteOnlineCommand { get; }

    public IReadOnlyList<OnlinePanoramaItem> OnlineViews
    {
        get => onlineViews;
        private set
        {
            if (SetProperty(ref onlineViews, value))
            {
                OnPropertyChanged(nameof(HasNoOnlineViews));
            }
        }
    }

    public bool HasNoOnlineViews => onlineViews.Count == 0;

    public string OnlineStatus
    {
        get => onlineStatus;
        private set => SetProperty(ref onlineStatus, value);
    }

    public override async void OnAppearing()
    {
        try
        {
            // Adding views is for staff only: the same sign-in as the building scanner.
            if (!auth.IsUnlocked)
            {
                await Dialogs.AlertAsync("Staff only", "Sign in on the staff screen (About → Staff tools) to add 360° views.");
                await Navigation.GoBackAsync();
                return;
            }

            await LoadAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusMessage = "Could not read the local panorama drafts.";
        }
    }

    public override void OnDisappearing()
    {
        DiscardPending();
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var loaded = await store.LoadAsync();
            var list = await online.GetListAsync();
            var onlineIds = list.Manifests.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
            Drafts = loaded.Select(draft => new PanoramaDraftItem(draft, onlineIds.Contains(draft.Id))).ToList();
            OnlineViews = list.Manifests.OrderByDescending(m => m.UpdatedAtUnixMs).Select(m => new OnlinePanoramaItem(m)).ToList();
            OnlineStatus = !online.IsConfigured
                ? "Firebase is not set up in this build, so views cannot be put online (add google-services.json and rebuild)."
                : list.IsFresh
                    ? $"{OnlineViews.Count} view(s) online - every visitor sees these."
                    : list.Message;
            StatusMessage = Drafts.Count == 0
                ? "No views on this phone yet."
                : $"{Drafts.Count} view(s) on this phone.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        if (isBusy)
        {
            return;
        }

        DiscardPending();

        FileResult? selected;
        try
        {
            selected = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a 2:1 equirectangular panorama",
                FileTypes = PanoramaFileTypes,
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await Dialogs.AlertAsync(
                "Could not open the file picker",
                "The phone did not allow the file picker to open. Try again or choose the image from Files.");
            return;
        }

        if (selected is null)
        {
            StatusMessage = "Import cancelled.";
            return;
        }

        IsBusy = true;
        string? temporaryPath = null;
        try
        {
            temporaryPath = await CopyToPrivateTemporaryFileAsync(selected);
            var validation = PanoramaValidation.ValidateFile(temporaryPath, selected.FileName);
            if (!validation.IsValid || validation.Image is null)
            {
                TryDelete(temporaryPath);
                StatusMessage = validation.Message;
                await Dialogs.AlertAsync("Cannot use this image", validation.Message);
                return;
            }

            pending = new PendingImport(temporaryPath, selected.FileName, validation.Image);
            PendingPreviewPath = temporaryPath;
            PendingName = NormalizeDisplayName(selected.FileName);
            PendingDetails = validation.Message;
            StatusMessage = "Preview ready. Save it as a local draft or discard it.";
            OnPropertyChanged(nameof(HasPending));
        }
        catch (Exception exception)
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }

            Debug.WriteLine(exception);
            StatusMessage = "The image could not be imported.";
            await Dialogs.AlertAsync("Import failed", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SavePendingAsync()
    {
        if (isBusy || pending is not { } current)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var draft = await store.AcceptAsync(current.TempPath, current.OriginalFileName, current.Image);
            ClearPending();
            await LoadAsync();
            StatusMessage = $"Saved locally as '{draft.DisplayName}'. Open it to view the 360° scene.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await Dialogs.AlertAsync(
                "Could not save the panorama",
                "The image is still in the preview. Check that the phone has free storage and try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenAsync(PanoramaDraftItem item)
    {
        await Navigation.GoToStopAsync(Routes.Pano, item.Draft.Id);
    }

    private async Task DeleteAsync(PanoramaDraftItem item)
    {
        if (isBusy)
        {
            return;
        }

        if (!await Dialogs.ConfirmAsync(
                "Delete from this phone?",
                item.IsOnline
                    ? $"'{item.DisplayName}' will be removed from this phone. It stays ONLINE for visitors - remove it under \"Online views\" for that."
                    : $"'{item.DisplayName}' will be removed from this phone. It was never uploaded, so it will be gone for good.",
                "Delete",
                "Cancel"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await store.DeleteAsync(item.Draft);
            await LoadAsync();
            StatusMessage = $"Deleted '{item.DisplayName}' from this phone.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await Dialogs.AlertAsync("Could not delete the draft", "Close the panorama if it is open and try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UploadAsync(PanoramaDraftItem item)
    {
        if (isBusy)
        {
            return;
        }

        if (!auth.IsUnlocked || auth.StaffUid is not { Length: > 0 } uid)
        {
            await Dialogs.AlertAsync("Signed out", "Your staff session has ended. Sign in again to upload.");
            return;
        }

        if (!online.IsConfigured)
        {
            await Dialogs.AlertAsync("Firebase not set up", OnlineStatus);
            return;
        }

        IsBusy = true;
        try
        {
            var image = await File.ReadAllBytesAsync(store.GetImagePath(item.Draft));
            StatusMessage = $"Uploading '{item.DisplayName}'...";
            var progress = new Progress<double>(value => StatusMessage = $"Uploading '{item.DisplayName}'... {value:P0}");
            await online.UploadAsync(item.Draft, image, uid, progress);
            await LoadAsync();
            StatusMessage = $"'{item.DisplayName}' is online. Visitors see it the next time they open the 360° tour.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            StatusMessage = "Upload failed - the view is still on this phone.";
            await Dialogs.AlertAsync(
                "Upload failed",
                "Check the internet connection and that your account is on the scanner list, then try again.\n\n" + exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteOnlineAsync(OnlinePanoramaItem item)
    {
        if (isBusy
            || !await Dialogs.ConfirmAsync(
                "Remove for all visitors?",
                $"'{item.Name}' will disappear from the 360° tour on every phone. Copies saved on staff phones are kept.",
                "Remove",
                "Cancel"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await online.DeleteAsync(item.Manifest);
            await LoadAsync();
            StatusMessage = $"'{item.Name}' is no longer online.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            await Dialogs.AlertAsync("Could not remove it", "Check the connection and try again.\n\n" + exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync(PanoramaDraftItem item)
    {
        var draft = item.Draft;
        var sceneId = PanoramaExport.SceneId(draft);
        var folder = Path.Combine(FileSystem.Current.CacheDirectory, "panorama-export");
        Directory.CreateDirectory(folder);

        var imagePath = Path.Combine(folder, sceneId + Path.GetExtension(draft.FileName));
        var scenePath = Path.Combine(folder, sceneId + ".scene.json");
        File.Copy(store.GetImagePath(draft), imagePath, overwrite: true);
        await File.WriteAllTextAsync(scenePath, PanoramaExport.SceneJson(draft));

        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = $"Export {draft.DisplayName}",
            Files = new List<ShareFile>
            {
                new(imagePath, draft.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg"),
                new(scenePath, "application/json"),
            },
        });
    }

    private static async Task<string> CopyToPrivateTemporaryFileAsync(FileResult selected)
    {
        if (!PanoramaImageRules.IsSupportedExtension(selected.FileName))
        {
            throw new InvalidOperationException("Only JPEG and PNG images are supported.");
        }

        var extension = Path.GetExtension(selected.FileName);
        var folder = Path.Combine(FileSystem.Current.CacheDirectory, PanoramaLocalFiles.TempFolderName);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, PanoramaLocalFiles.CreateTempFileName(extension));

        try
        {
            await using var source = await selected.OpenReadAsync();
            await using var destination = File.Create(path);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                total += read;
                if (total > PanoramaImageRules.MaxFileSizeBytes)
                {
                    throw new InvalidOperationException(
                        "The selected image is larger than the 20 MB import limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read));
            }

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private void DiscardPending()
    {
        if (pending is null)
        {
            return;
        }

        TryDelete(pending.TempPath);
        ClearPending();
    }

    private void ClearPending()
    {
        pending = null;
        PendingPreviewPath = string.Empty;
        PendingName = string.Empty;
        PendingDetails = string.Empty;
        OnPropertyChanged(nameof(HasPending));
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

    private static void TryDelete(string path)
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
            Debug.WriteLine($"Could not delete the temporary panorama '{path}': {exception.Message}");
        }
    }

    private sealed record PendingImport(
        string TempPath,
        string OriginalFileName,
        PanoramaImageInfo Image);
}
