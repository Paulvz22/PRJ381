using BCOpendayApp.AR;
using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Scanner.ViewModels;

/// <summary>One row in the scanner's building list.</summary>
public sealed record ScannedBuildingItem(BuildingAnchor Building, bool IsDraft, string Details)
{
    public string Name => Building.Name;

    public string Badge => IsDraft ? "NOT UPLOADED" : Building.IsActive ? "LIVE" : "HIDDEN";
}

public sealed class ScannerHomeViewModel : ViewModelBase
{
    private readonly IScannerAuthService auth;
    private readonly IBuildingRepository repository;
    private readonly ScannerDraftStore drafts;
    private IReadOnlyList<ScannedBuildingItem> buildings = Array.Empty<ScannedBuildingItem>();
    private IReadOnlyList<ScannedBuildingItem> pending = Array.Empty<ScannedBuildingItem>();
    private string statusMessage = "Loading...";
    private bool isLoading;

    public ScannerHomeViewModel(
        IScannerAuthService auth,
        IBuildingRepository repository,
        ScannerDraftStore drafts,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.auth = auth;
        this.repository = repository;
        this.drafts = drafts;

        NewBuildingCommand = CreateCommand(() => Navigation.GoToAsync(ScannerRoutes.Capture));
        EditCommand = CreateCommand<ScannedBuildingItem>(item =>
            item.IsDraft
                ? Navigation.GoToStopAsync(ScannerRoutes.Capture, "draft:" + item.Building.Id)
                : Navigation.GoToStopAsync(ScannerRoutes.Capture, item.Building.Id));
        UploadDraftsCommand = CreateCommand(UploadDraftsAsync);
        ExportCommand = CreateCommand(ExportAsync);
        TestArCommand = CreateCommand(() => Navigation.GoToAsync(ArRoutes.Viewer));
        RefreshCommand = CreateCommand(() => LoadAsync(forceRefresh: true));
        SignOutCommand = CreateCommand(async () =>
        {
            await auth.SignOutAsync();
            await Navigation.GoBackAsync();
        });
    }

    public string SignedInAs => $"Signed in as {auth.StaffEmail}";

    public IReadOnlyList<ScannedBuildingItem> Buildings
    {
        get => buildings;
        private set
        {
            if (SetProperty(ref buildings, value))
            {
                OnPropertyChanged(nameof(HasNoBuildings));
            }
        }
    }

    public bool HasNoBuildings => buildings.Count == 0 && !isLoading;

    public IReadOnlyList<ScannedBuildingItem> Pending
    {
        get => pending;
        private set
        {
            if (SetProperty(ref pending, value))
            {
                OnPropertyChanged(nameof(HasPending));
            }
        }
    }

    public bool HasPending => pending.Count > 0;

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public AsyncCommand NewBuildingCommand { get; }

    public AsyncCommand EditCommand { get; }

    public AsyncCommand UploadDraftsCommand { get; }

    public AsyncCommand ExportCommand { get; }

    public AsyncCommand TestArCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand SignOutCommand { get; }

    public override async void OnAppearing()
    {
        try
        {
            if (!auth.IsUnlocked)
            {
                await Dialogs.AlertAsync("Scanner locked", "Your scanner session has ended. Sign in again.");
                await Navigation.GoBackAsync();
                return;
            }

            OnPropertyChanged(nameof(SignedInAs));
            await LoadAsync(forceRefresh: true);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        isLoading = true;
        try
        {
            var result = await repository.GetBuildingsAsync(forceRefresh);
            StatusMessage = result.Source == BuildingSource.Firestore
                ? result.Message
                : result.Message + " Buildings shown may be out of date.";
            Buildings = result.Buildings.Select(b => new ScannedBuildingItem(b, false, Describe(b))).ToList();
            Pending = (await drafts.LoadAsync()).Select(b => new ScannedBuildingItem(b, true, Describe(b))).ToList();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            StatusMessage = "Could not load buildings.";
        }
        finally
        {
            isLoading = false;
            OnPropertyChanged(nameof(HasNoBuildings));
        }
    }

    private async Task UploadDraftsAsync()
    {
        var failed = 0;
        foreach (var item in pending)
        {
            try
            {
                var building = item.Building with
                {
                    UpdatedBy = auth.StaffUid ?? item.Building.UpdatedBy,
                    UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                await repository.SaveAsync(building);
                await drafts.RemoveAsync(building.Id);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                failed++;
            }
        }

        await LoadAsync(forceRefresh: true);
        await Dialogs.AlertAsync(
            failed == 0 ? "Uploaded" : "Some uploads failed",
            failed == 0 ? "All drafts are now live." : $"{failed} draft(s) could not be uploaded. Check the connection and try again.");
    }

    /// <summary>
    /// Shares buildings.json. Replacing src/BCOpendayApp/Resources/Raw/buildings.json with this file bundles the
    /// buildings into the next app build, so even a phone that has never been online has them.
    /// </summary>
    private async Task ExportAsync()
    {
        var result = await repository.GetBuildingsAsync(forceRefresh: true);
        var path = Path.Combine(FileSystem.Current.CacheDirectory, "buildings.json");
        await File.WriteAllTextAsync(path, repository.ExportJson(result.Buildings));
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export buildings.json",
            File = new ShareFile(path, "application/json"),
        });
    }

    private static string Describe(BuildingAnchor b)
    {
        var shape = b.HasFootprint ? $"{b.Footprint.Count} corners" : $"circle, {Math.Round(b.RadiusMeters)} m radius";
        var updated = b.UpdatedAtUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(b.UpdatedAtUnixMs).ToLocalTime().ToString("d MMM yyyy HH:mm")
            : "never";
        return $"{b.Id} · {shape} · updated {updated}";
    }
}
