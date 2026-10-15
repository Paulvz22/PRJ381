using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Sensors;
using BCOpendayApp.AR.ViewModels;
using BCOpendayApp.AR.Views;

namespace BCOpendayApp.AR;

/// <summary>Shell routes of the AR feature. Navigate with INavigationService.GoToAsync(ArRoutes.Viewer).</summary>
public static class ArRoutes
{
    public const string Viewer = "ar-viewer";
}

/// <summary>
/// Everything the AR guide needs, in one call from MauiProgram:  builder.UseBuildingAr();
/// (The scanner in the Scanner folder builds on the services registered here.)
/// </summary>
public static class ArFeature
{
    public static MauiAppBuilder UseBuildingAr(this MauiAppBuilder builder)
    {
        // NOTE: the camera itself is switched on in MauiProgram.cs (.UseMauiCommunityToolkitCamera()),
        // because the toolkit's analyzer requires that call in the same method as UseMauiApp.

        builder.Services
            .AddSingleton<IArSensorService, ArSensorService>()
            .AddSingleton<IBuildingRemoteStore, FirestoreBuildingStore>()
            .AddSingleton<IBuildingLocalStore, MauiBuildingLocalStore>()
            .AddSingleton<IBuildingRepository, BuildingRepository>()
            .AddTransient<ArViewerViewModel>()
            .AddTransient<ArViewerPage>();

        Routing.RegisterRoute(ArRoutes.Viewer, typeof(ArViewerPage));
        return builder;
    }
}
