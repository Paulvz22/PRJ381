using BCOpendayApp.Services;
using BCOpendayApp.Views;

namespace BCOpendayApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pages pushed on top of the current tab. The root pages (splash/home/tour/map) are in AppShell.xaml.
        Routing.RegisterRoute(Routes.Locations, typeof(LocationsPage));
        Routing.RegisterRoute(Routes.StartTour, typeof(StartTourPage));
        Routing.RegisterRoute(Routes.StopViewer, typeof(StopViewerPage));
        Routing.RegisterRoute(Routes.StopInfo, typeof(StopInfoPage));
        Routing.RegisterRoute(Routes.StopGallery, typeof(StopGalleryPage));
        Routing.RegisterRoute(Routes.HowItWorks, typeof(HowItWorksPage));
        Routing.RegisterRoute(Routes.About, typeof(AboutPage));
        Routing.RegisterRoute(Routes.Pano, typeof(PanoramaPage));
        Routing.RegisterRoute(Routes.VisitorInfo, typeof(VisitorInfoPage));
    }
}
