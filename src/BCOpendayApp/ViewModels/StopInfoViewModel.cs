using BCOpendayApp.Infrastructure;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

/// <summary>Detail text for one stop, with narration, favourites and a "next stop" shortcut.</summary>
public sealed class StopInfoViewModel : StopDetailViewModelBase
{
    private readonly INarrationService narration;

    public StopInfoViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        INarrationService narration,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
        this.narration = narration;

        MarkVisitedCommand = CreateCommand(() =>
        {
            if (Stop is not null)
            {
                VisitorProgress.MarkVisited(Stop.Id);
                OnPropertyChanged(nameof(VisitButtonText));
            }

            return Task.CompletedTask;
        });
        ToggleFavouriteCommand = CreateCommand(() =>
        {
            if (Stop is not null)
            {
                VisitorProgress.SetFavourite(Stop.Id, !VisitorProgress.IsFavourite(Stop.Id));
                OnPropertyChanged(nameof(FavouriteButtonText));
            }

            return Task.CompletedTask;
        });
        ListenCommand = CreateCommand(ListenAsync);
        StopAudioCommand = CreateCommand(() =>
        {
            narration.Stop();
            return Task.CompletedTask;
        });
        NextStopCommand = CreateCommand(() =>
        {
            var next = NextStop;
            if (next is not null)
            {
                narration.Stop();
                LoadStop(next);
            }

            return Task.CompletedTask;
        });
        ShowMapCommand = CreateCommand(() =>
            Stop is null ? Navigation.GoToTabAsync(Routes.Map) : Navigation.GoToTabAsync(Routes.Map, Stop.Id));
        ChooseLocationCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
    }

    public string PageTitle => Stop?.Name ?? "Location information";

    /// <summary>The second photo when a stop has one (e.g. the robot arm), otherwise the main photo.</summary>
    public string Image => Stop is null ? string.Empty : CampusCatalog.ExtraImageFor(Stop.Id) ?? Stop.Image;

    public string Highlight => Stop?.Highlight ?? string.Empty;

    public string Summary => Stop?.Summary ?? string.Empty;

    public string Details => Stop?.Description ?? string.Empty;

    public string PositionNote => Stop?.PositionNote ?? string.Empty;

    public bool HasPositionNote => PositionNote.Length > 0;

    public string VisitButtonText => VisitedText("Mark as visited");

    public string FavouriteButtonText =>
        Stop is not null && VisitorProgress.IsFavourite(Stop.Id) ? "Remove from favourites" : "Save to favourites";

    public bool HasNextStop => NextStop is not null;

    public string NextStopText => NextStop is { } next ? $"Next: {next.Name}" : string.Empty;

    public AsyncCommand MarkVisitedCommand { get; }

    public AsyncCommand ToggleFavouriteCommand { get; }

    public AsyncCommand ListenCommand { get; }

    public AsyncCommand StopAudioCommand { get; }

    public AsyncCommand NextStopCommand { get; }

    public AsyncCommand ShowMapCommand { get; }

    public AsyncCommand ChooseLocationCommand { get; }

    private Hotspot? NextStop
    {
        get
        {
            if (Stop is null)
            {
                return null;
            }

            var index = Locations.ToList().FindIndex(s => s.Id == Stop.Id);
            return index >= 0 && index + 1 < Locations.Count ? Locations[index + 1] : null;
        }
    }

    public override void OnDisappearing()
    {
        narration.Stop();
        base.OnDisappearing();
    }

    protected override void OnStopLoaded() =>
        OnPropertiesChanged(
            nameof(PageTitle),
            nameof(Image),
            nameof(Highlight),
            nameof(Summary),
            nameof(Details),
            nameof(PositionNote),
            nameof(HasPositionNote),
            nameof(VisitButtonText),
            nameof(FavouriteButtonText),
            nameof(HasNextStop),
            nameof(NextStopText));

    private async Task ListenAsync()
    {
        if (Stop is null)
        {
            return;
        }

        try
        {
            await narration.SpeakAsync($"{Stop.Name}. {Stop.Description}");
        }
        catch (Exception)
        {
            await Dialogs.AlertAsync(
                "Audio unavailable",
                "Speech is unavailable on this device. The complete description is shown on this page.");
        }
    }
}
