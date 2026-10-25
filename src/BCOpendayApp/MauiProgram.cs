using BCOpendayApp.AR;
using BCOpendayApp.Panorama;
using BCOpendayApp.Scanner;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;
using BCOpendayApp.Views;
using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.LifecycleEvents;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Bundled.Shared;
#if ANDROID
using Plugin.Firebase.Bundled.Platforms.Android;
#elif IOS
using Plugin.Firebase.Bundled.Platforms.iOS;
#endif
#endif

namespace BCOpendayApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Camera preview for the AR guide. Must stay in THIS method: the toolkit's build analyzer
            // (MCTC001) fails the build if it cannot find this call next to UseMauiApp.
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureLifecycleEvents(ConfigureFirebase);

        // AR building guide (AR/ folder) and the staff-only building scanner (Scanner/ folder).
        builder
            .UseBuildingAr()
            .UseBuildingScanner()
            .UseLocalPanoramas();

        builder.Services
            .AddServices()
            .AddViewModels()
            .AddPages();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Starts Firebase when the platform config file was found at build time (FIREBASE_CONFIGURED - see the
    /// csproj). If it is missing or start-up fails, FirebaseAvailability stays "not ready" and the app runs
    /// on the bundled offline locations.
    /// </summary>
    private static void ConfigureFirebase(ILifecycleBuilder events)
    {
#if FIREBASE_CONFIGURED && ANDROID
        events.AddAndroid(android => android.OnCreate((activity, bundle) =>
        {
            try
            {
                CrossFirebase.Initialize(
                    activity,
                    () => Platform.CurrentActivity!,
                    new CrossFirebaseSettings(
                        isAuthEnabled: true,
                        isFirestoreEnabled: true,
                        isAnalyticsEnabled: true,
                        isCrashlyticsEnabled: true));

                if (global::Firebase.FirebaseApp.Instance is not null)
                {
                    FirebaseAvailability.MarkReady();
                }
            }
            catch (Exception exception)
            {
                FirebaseAvailability.MarkUnavailable(exception);
            }
        }));
#elif FIREBASE_CONFIGURED && IOS
        events.AddiOS(ios => ios.WillFinishLaunching((application, options) =>
        {
            try
            {
                CrossFirebase.Initialize(new CrossFirebaseSettings(
                    isAuthEnabled: true,
                    isFirestoreEnabled: true,
                    isAnalyticsEnabled: true,
                    isCrashlyticsEnabled: true));

                if (global::Firebase.Core.App.DefaultInstance is not null)
                {
                    FirebaseAvailability.MarkReady();
                }
            }
            catch (Exception exception)
            {
                FirebaseAvailability.MarkUnavailable(exception);
            }

            return true;
        }));
#endif
    }

    // One instance for the whole app: stateless helpers or things that must be shared.
    private static IServiceCollection AddServices(this IServiceCollection services) => services
        // Firebase wrappers (each one is a safe no-op when Firebase is not configured)
        .AddSingleton<IAuthService, FirebaseAuthService>()
        .AddSingleton<IFirestoreService, FirestoreService>()
        .AddSingleton<IAnalyticsService, FirebaseAnalyticsService>()
        // Tour stops: Firestore -> last good copy -> bundled Resources/Raw/locations.json
        .AddSingleton<ILocationEnvironment, MauiLocationEnvironment>()
        .AddSingleton<ILocationRemoteSource, FirestoreLocationSource>()
        .AddSingleton<ILocationService>(provider => new LocationService(
            provider.GetRequiredService<ILocationRemoteSource>(),
            provider.GetRequiredService<ILocationEnvironment>()))
        // App services
        .AddSingleton<IVisitorProgressService, VisitorProgressService>()
        .AddSingleton<INarrationService, NarrationService>()
        .AddSingleton<INavigationService, NavigationService>()
        .AddSingleton<IDialogService, DialogService>();

    // A fresh view model per page instance.
    private static IServiceCollection AddViewModels(this IServiceCollection services) => services
        .AddTransient<SplashViewModel>()
        .AddTransient<HomeViewModel>()
        .AddTransient<TourViewModel>()
        .AddTransient<MapViewModel>()
        .AddTransient<LocationsViewModel>()
        .AddTransient<StartTourViewModel>()
        .AddTransient<StopViewerViewModel>()
        .AddTransient<StopInfoViewModel>()
        .AddTransient<GalleryViewModel>()
        .AddTransient<HowItWorksViewModel>()
        .AddTransient<AboutViewModel>();

    // Every page must be registered here so Shell can create it with its view model injected.
    private static IServiceCollection AddPages(this IServiceCollection services) => services
        .AddTransient<SplashPage>()
        .AddTransient<HomePage>()
        .AddTransient<TourPage>()
        .AddTransient<MapPage>()
        .AddTransient<LocationsPage>()
        .AddTransient<StartTourPage>()
        .AddTransient<StopViewerPage>()
        .AddTransient<StopInfoPage>()
        .AddTransient<StopGalleryPage>()
        .AddTransient<HowItWorksPage>()
        .AddTransient<AboutPage>()
        .AddTransient<PanoramaPage>()
        .AddTransient<VisitorInfoPage>();
}
