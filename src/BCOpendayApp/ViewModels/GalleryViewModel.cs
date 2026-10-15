using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class GalleryViewModel : StopDetailViewModelBase
{
    public GalleryViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
    }

    public string PageTitle => Stop?.Name ?? "Gallery";

    public IReadOnlyList<string> Images => Stop is null ? Array.Empty<string>() : CampusCatalog.GalleryImagesFor(Stop);

    protected override void OnStopLoaded() => OnPropertiesChanged(nameof(PageTitle), nameof(Images));
}
