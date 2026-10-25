using System.Diagnostics;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace BCOpendayApp.Panorama.ViewModels;

public sealed record PanoramaDraftItem(PanoramaDraft Draft)
{
    public string DisplayName => Draft.DisplayName;

    public string Badge => "LOCAL";

    public string Details =>
        $"{Draft.Width} × {Draft.Height} · {FormatSize(Draft.FileSizeBytes)} · "
        + DateTimeOffset.FromUnixTimeMilliseconds(Draft.CreatedAtUnixMs).ToLocalTime().ToString("g");

    private static string FormatSize(long bytes) => $"{bytes / (1024d * 1024d):0.0} MB";
}

public sealed class PanoramaDraftsViewModel : ViewModelBase
{
    private static readonly FilePickerFileType PanoramaFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = new[] { "image/jpeg", "image/png" },
        });

    private readonly PanoramaDraftStore store;
    private IReadOnlyList<PanoramaDraftItem> drafts = Array.Empty<PanoramaDraftItem>();
    private PendingImport? pending;
    private string pendingPreviewPath = string.Empty;
    private string pendingName = string.Empty;
    private string pendingDetails = string.Empty;
    private string statusMessage = "No local panorama drafts yet.";
    private bool isBusy;

    public PanoramaDraftsViewModel(
        PanoramaDraftStore store,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.store = store;

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

    public override async void OnAppearing()
    {
        try
        {
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
            Drafts = loaded.Select(draft => new PanoramaDraftItem(draft)).ToList();
            StatusMessage = Drafts.Count == 0
                ? "No local panorama drafts yet."
                : $"{Drafts.Count} local draft(s). Stored on this phone only.";
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
                "Delete local draft?",
                $"'{item.DisplayName}' will be removed from this phone. This cannot be undone.",
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
