using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Panorama.ViewModels;
using BCOpendayApp.Panorama.Views;

namespace BCOpendayApp.Panorama;

public static class PanoramaRoutes
{
    /// <summary>Staff: list of panoramas on this phone (import, capture, export, delete).</summary>
    public const string Drafts = "panorama-drafts";

    /// <summary>Staff: capture a new 360° view with the camera.</summary>
    public const string Capture = "panorama-capture";
}

/// <summary>
/// Registers the staff panorama tools (call AFTER UseBuildingScanner - they use its staff sign-in, and the AR
/// feature's sensors). The visitor-facing 360° viewer is Views/PanoramaPage.
/// </summary>
public static class PanoramaFeature
{
    public static MauiAppBuilder UseLocalPanoramas(this MauiAppBuilder builder)
    {
        builder.Services
            .AddSingleton<PanoramaDraftStore>()
            // Online views: Firestore documents + a copy on each phone (see docs/panoramas.md).
            .AddSingleton<IPanoramaRemoteStore, FirestorePanoramaStore>()
            .AddSingleton(provider => new PanoramaOnlineLibrary(
                provider.GetRequiredService<IPanoramaRemoteStore>(),
                FileSystem.Current.AppDataDirectory))
            .AddTransient<PanoramaDraftsViewModel>()
            .AddTransient<PanoramaDraftsPage>()
            .AddTransient<PanoramaCaptureViewModel>()
            .AddTransient<PanoramaCapturePage>();

        Routing.RegisterRoute(PanoramaRoutes.Drafts, typeof(PanoramaDraftsPage));
        Routing.RegisterRoute(PanoramaRoutes.Capture, typeof(PanoramaCapturePage));
        return builder;
    }
}
