using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.ViewModels;

public sealed class HomeViewModel : CampusLocationsViewModelBase
{
    private readonly IAnalyticsService analytics;

    private string journeyText = "Your journey";
    private double journeyProgress;

    public HomeViewModel(
        ICampusLocationService campus,
        IVisitorProgressService visitorProgress,
        IAnalyticsService analytics,
        INavigationService navigation,
        IDialogService dialogs)
        : base(campus, visitorProgress, navigation, dialogs)
    {
        this.analytics = analytics;

        StartTourCommand = CreateCommand(() => Navigation.GoToTabAsync(Routes.Tour));
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

    /// <summary>A location counts as visited once it was seen in the AR camera or opened as a 360° view.</summary>
    protected override void OnLocationsLoaded()
    {
        var total = Locations.Count;
        var completed = VisitorProgress.CountVisited(Locations.Select(location => location.Id));

        JourneyText = $"Your journey · {completed} / {total} visited";
        JourneyProgress = total == 0 ? 0 : (double)completed / total;
    }
}
