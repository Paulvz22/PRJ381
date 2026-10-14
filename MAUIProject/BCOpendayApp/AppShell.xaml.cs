using BCOpendayApp.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BCOpendayApp;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        // Explicit factories ensure XAML root pages receive the SAME singleton repository.
        SplashContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<SplashPage1>());
        HomeContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<HomePage>());
        TourContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<TourPage>());
        LocationsContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<LocationsPage>());
        MapContent.ContentTemplate = new DataTemplate(() => services.GetRequiredService<MapPage>());
        Routing.RegisterRoute(nameof(QrScanPage), new InjectedRoute<QrScanPage>(services));
        Routing.RegisterRoute(nameof(TourViewPage), new InjectedRoute<TourViewPage>(services));
        Routing.RegisterRoute(nameof(HotspotInfoOverlayPage), new InjectedRoute<HotspotInfoOverlayPage>(services));
        Routing.RegisterRoute(nameof(HowItWorksPage), typeof(HowItWorksPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(NotificationsPage), typeof(NotificationsPage));
        Routing.RegisterRoute(nameof(SplashPage1), typeof(SplashPage1));
    }

    private sealed class InjectedRoute<TPage>(IServiceProvider services) : RouteFactory where TPage : Element
    {
        public override Element GetOrCreate() => services.GetRequiredService<TPage>();
        public override Element GetOrCreate(IServiceProvider serviceProvider) => services.GetRequiredService<TPage>();
    }
}
