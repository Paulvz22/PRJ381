using BCOpendayApp.Views;
namespace BCOpendayApp;
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(QrScanPage), typeof(QrScanPage));
        Routing.RegisterRoute(nameof(TourViewPage), typeof(TourViewPage));
        Routing.RegisterRoute(nameof(HowItWorksPage), typeof(HowItWorksPage));
        Routing.RegisterRoute(nameof(HotspotInfoOverlayPage), typeof(HotspotInfoOverlayPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(NotificationsPage), typeof(NotificationsPage));
        Routing.RegisterRoute(nameof(SplashPage1), typeof(SplashPage1));
    }
}
