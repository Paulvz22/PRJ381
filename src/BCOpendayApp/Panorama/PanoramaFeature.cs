using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Panorama.ViewModels;
using BCOpendayApp.Panorama.Views;

namespace BCOpendayApp.Panorama;

public static class PanoramaRoutes
{
    public const string Drafts = "panorama-drafts";
}

/// <summary>Registers the local-only panorama import and draft workflow.</summary>
public static class PanoramaFeature
{
    public static MauiAppBuilder UseLocalPanoramas(this MauiAppBuilder builder)
    {
        builder.Services
            .AddSingleton<PanoramaDraftStore>()
            .AddTransient<PanoramaDraftsViewModel>()
            .AddTransient<PanoramaDraftsPage>();

        Routing.RegisterRoute(PanoramaRoutes.Drafts, typeof(PanoramaDraftsPage));
        return builder;
    }
}
