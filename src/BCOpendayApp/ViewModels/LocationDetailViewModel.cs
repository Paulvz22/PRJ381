using System.Diagnostics;
using BCOpendayApp.AR;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama.Capture;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

/// <summary>One location: photo, name, description, code, and buttons for its 360° view and the AR camera.</summary>
public sealed class LocationDetailViewModel : ViewModelBase, IQueryAttributable
{
    private readonly ICampusLocationService campus;
    private readonly IVisitorProgressService visitorProgress;
    private readonly IScannerAuthService staff;

    private string locationId = string.Empty;
    private CampusLocation? location;
    private ImageSource? photo;
    private string message = string.Empty;
    private bool isBusy;

    public LocationDetailViewModel(
        ICampusLocationService campus,
        IVisitorProgressService visitorProgress,
        IScannerAuthService staff,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.campus = campus;
        this.visitorProgress = visitorProgress;
        this.staff = staff;

        OpenPanoramaCommand = CreateCommand(() =>
            location?.FirstViewId is { } viewId ? Navigation.GoToStopAsync(Routes.Pano, viewId) : Task.CompletedTask);
        OpenArCommand = CreateCommand(() => Navigation.GoToAsync(ArRoutes.Viewer));
        TakePhotoCommand = CreateCommand(() => ReplacePhotoAsync(fromCamera: true));
        PickPhotoCommand = CreateCommand(() => ReplacePhotoAsync(fromCamera: false));
    }

    public string Name => location?.Name ?? "Location";

    public string Description => location is null
        ? string.Empty
        : location.Description.Length > 0 ? location.Description : "No description has been added yet.";

    public string Code => location is null ? string.Empty : $"Location code: {location.Code}";

    public string Details => location is null
        ? string.Empty
        : string.Join("  ·  ", new[] { location.Badges, visitorProgress.IsVisited(location.Id) ? "✓ Visited" : null }.OfType<string>());

    public bool HasPanorama => location?.HasPanorama == true;

    public bool HasAr => location?.HasAr == true;

    public string ArHint => "Open the AR camera outdoors and point it at the building to see its name.";

    public ImageSource? Photo
    {
        get => photo;
        private set
        {
            if (SetProperty(ref photo, value))
            {
                OnPropertyChanged(nameof(HasPhoto));
            }
        }
    }

    public bool HasPhoto => photo is not null;

    /// <summary>Staff (signed in on the scanner) can add or replace the location's photo.</summary>
    public bool IsStaff => staff.IsUnlocked;

    public string Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => message.Length > 0;

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public AsyncCommand OpenPanoramaCommand { get; }

    public AsyncCommand OpenArCommand { get; }

    public AsyncCommand TakePhotoCommand { get; }

    public AsyncCommand PickPhotoCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        locationId = query.TryGetValue(Routes.StopIdParameter, out var value) && value is string text ? text : string.Empty;
    }

    public override void OnAppearing() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            location = await campus.GetAsync(locationId);
            Message = location is null ? "This location no longer exists." : string.Empty;
            OnPropertiesChanged(
                nameof(Name), nameof(Description), nameof(Code), nameof(Details),
                nameof(HasPanorama), nameof(HasAr), nameof(IsStaff));

            if (location is not null && await campus.GetPhotoPathAsync(location.Id) is { } path)
            {
                Photo = ImageSource.FromFile(path);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Message = "This location could not be loaded.";
        }
    }

    private async Task ReplacePhotoAsync(bool fromCamera)
    {
        if (location is null || !staff.IsUnlocked || staff.StaffUid is not { } uid)
        {
            return;
        }

        try
        {
            FileResult? file;
            if (fromCamera)
            {
                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    await Dialogs.AlertAsync("No camera", "This phone cannot take a photo here. Choose one from the gallery instead.");
                    return;
                }

                file = await MediaPicker.Default.CapturePhotoAsync();
            }
            else
            {
                file = await MediaPicker.Default.PickPhotoAsync();
            }

            if (file is null)
            {
                return;
            }

            IsBusy = true;
            Message = "Uploading photo...";
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var original = buffer.ToArray();

            var jpeg = await Task.Run(() => PanoramaFrameCodec.ShrinkJpeg(
                original, LocationPhotoRules.MaxSidePixels, LocationPhotoRules.MaxBytes));
            await campus.SavePhotoAsync(location.Id, jpeg, uid);

            Photo = null;
            Photo = ImageSource.FromStream(() => new MemoryStream(jpeg));
            Message = "Photo saved. Visitors see it the next time they open this location.";
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            Message = $"The photo was not saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
