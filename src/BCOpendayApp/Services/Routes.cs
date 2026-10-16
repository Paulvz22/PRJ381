namespace BCOpendayApp.Services;

/// <summary>
/// Every Shell route in one place. Tab/root routes are declared in AppShell.xaml; pushed (detail)
/// routes are registered in AppShell.xaml.cs. Never type a route string anywhere else.
/// </summary>
public static class Routes
{
    // Declared in AppShell.xaml (navigate with INavigationService.GoToTabAsync)
    public const string Splash = "splash";
    public const string Home = "home";
    public const string Tour = "tour";
    public const string Map = "map";

    // Registered in AppShell.xaml.cs (navigate with INavigationService.GoToAsync)
    public const string Locations = "locations";
    public const string StartTour = "start-tour";
    public const string StopViewer = "stop-viewer";
    public const string StopInfo = "stop-info";
    public const string StopGallery = "stop-gallery";
    public const string HowItWorks = "how-it-works";
    public const string About = "about";
    public const string Pano = "pano";
    public const string VisitorInfo = "visitor-info";

    /// <summary>Name of the query parameter that carries a stop id to the stop pages.</summary>
    public const string StopIdParameter = "id";
}
