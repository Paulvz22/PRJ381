using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>
/// The "tour view" of one stop. Today it shows a photo; this is the page the AR / 360° view will replace.
/// </summary>
public sealed class StopViewerViewModel : StopDetailViewModelBase
{
    private readonly INarrationService narration;

    public StopViewerViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INarrationService narration,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
        this.narration = narration;

        ShowInfoCommand = CreateCommand(() => OpenAsync(Routes.StopInfo));
        ShowGalleryCommand = CreateCommand(() => OpenAsync(Routes.StopGallery));
        ShowFloorPlanCommand = CreateCommand(() =>
            Stop is null ? Navigation.GoToTabAsync(Routes.Map) : Navigation.GoToTabAsync(Routes.Map, Stop.Id));
        ListenCommand = CreateCommand(ListenAsync);
        StopAudioCommand = CreateCommand(() =>
        {
            narration.Stop();
            return Task.CompletedTask;
        });
        MarkVisitedCommand = CreateCommand(() =>
        {
            if (Stop is not null)
            {
                VisitorProgress.MarkVisited(Stop.Id);
                OnPropertyChanged(nameof(VisitButtonText));
            }

            return Task.CompletedTask;
        });
        ChooseLocationCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
    }

    public string PageTitle => Stop?.Name ?? "Campus viewer";

    public string Image => Stop?.Image ?? string.Empty;

    public string VisitButtonText => VisitedText("Mark as visited");

    public AsyncCommand ShowInfoCommand { get; }

    public AsyncCommand ShowGalleryCommand { get; }

    public AsyncCommand ShowFloorPlanCommand { get; }

    public AsyncCommand ListenCommand { get; }

    public AsyncCommand StopAudioCommand { get; }

    public AsyncCommand MarkVisitedCommand { get; }

    public AsyncCommand ChooseLocationCommand { get; }

    public override void OnDisappearing()
    {
        narration.Stop();
        base.OnDisappearing();
    }

    protected override void OnStopLoaded() =>
        OnPropertiesChanged(nameof(PageTitle), nameof(Image), nameof(VisitButtonText));

    private Task OpenAsync(string route) =>
        Stop is null ? Task.CompletedTask : Navigation.GoToStopAsync(route, Stop.Id);

    private async Task ListenAsync()
    {
        if (Stop is null)
        {
            return;
        }

        try
        {
            await narration.SpeakAsync(Stop.Description);
        }
        catch (Exception)
        {
            await Dialogs.AlertAsync(
                "Audio unavailable",
                "Read the description under Info Points. Check that a speech voice is installed on your device.");
        }
    }
}
