using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class HomeViewModel : CatalogViewModelBase
{
    private readonly IAnalyticsService analytics;

    private string journeyText = string.Empty;
    private double journeyProgress;

    public HomeViewModel(
        ILocationService locationService,
        IVisitorProgressService visitorProgress,
        IAnalyticsService analytics,
        INavigationService navigation,
        IDialogService dialogs)
        : base(locationService, visitorProgress, navigation, dialogs)
    {
        this.analytics = analytics;

        StartTourCommand = CreateCommand(() => Navigation.GoToAsync(Routes.StartTour));
        ShowLocationsCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
        ShowMapCommand = CreateCommand(() => Navigation.GoToTabAsync(Routes.Map));
        HowItWorksCommand = CreateCommand(() => Navigation.GoToAsync(Routes.HowItWorks));
        AboutCommand = CreateCommand(() => Navigation.GoToAsync(Routes.About));
        VisitorInfoCommand = CreateCommand(() => Navigation.GoToAsync(Routes.VisitorInfo));
    }

    public string JourneyText
    {
        get => journeyText;
        private set => SetProperty(ref journeyText, value);
    }

    public double JourneyProgress
    {
        get => journeyProgress;
        private set => SetProperty(ref journeyProgress, value);
    }

    public AsyncCommand StartTourCommand { get; }

    public AsyncCommand ShowLocationsCommand { get; }

    public AsyncCommand ShowMapCommand { get; }

    public AsyncCommand HowItWorksCommand { get; }

    public AsyncCommand AboutCommand { get; }

    public AsyncCommand VisitorInfoCommand { get; }

    public override void OnAppearing()
    {
        analytics.LogScreenView("HomePage");
        base.OnAppearing();
    }

    protected override void OnCatalogueLoaded()
    {
        var total = Locations.Count;
        var completed = VisitorProgress.CountVisited(Locations.Select(stop => stop.Id));

        JourneyText = $"Your journey · {completed} / {total} visited";
        JourneyProgress = total == 0 ? 0 : (double)completed / total;
    }
}
