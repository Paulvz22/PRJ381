using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama;
using BCOpendayApp.Services;

namespace BCOpendayApp.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    public const string WebsiteUrl = "https://www.belgiumcampus.ac.za/";

    public AboutViewModel(INavigationService navigation, IDialogService dialogs)
        : base(navigation, dialogs)
    {
        ExploreLocationsCommand = CreateCommand(() => Navigation.GoToAsync(Routes.Locations));
        HowItWorksCommand = CreateCommand(() => Navigation.GoToAsync(Routes.HowItWorks));
        WebsiteCommand = CreateCommand(OpenWebsiteAsync);
        PanoramaDraftsCommand = CreateCommand(() => Navigation.GoToAsync(PanoramaRoutes.Drafts));
    }

    public AsyncCommand ExploreLocationsCommand { get; }

    public AsyncCommand HowItWorksCommand { get; }

    /// <summary>Opens the official Belgium Campus website in the browser.</summary>
    public AsyncCommand WebsiteCommand { get; }

    /// <summary>Staff: 360° views saved on this phone (the page itself asks for the staff sign-in).</summary>
    public AsyncCommand PanoramaDraftsCommand { get; }

    private async Task OpenWebsiteAsync()
    {
        if (!await Launcher.Default.TryOpenAsync(new Uri(WebsiteUrl)))
        {
            await Dialogs.AlertAsync("Could not open the website", WebsiteUrl);
        }
    }
}
