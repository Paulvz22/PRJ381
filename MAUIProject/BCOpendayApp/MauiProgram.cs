using BCOpendayApp.Services;
using BCOpendayApp.Views;
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
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
        builder.ConfigureLifecycleEvents(events =>
        {
#if FIREBASE_CONFIGURED && ANDROID
            events.AddAndroid(android => android.OnCreate((activity, bundle) =>
            {
                try
                {
                    CrossFirebase.Initialize(activity, () => Platform.CurrentActivity!,
                        new CrossFirebaseSettings(
                                                    isAuthEnabled: true,
                                                    isFirestoreEnabled: true,
                                                    isAnalyticsEnabled: true,
                                                    isCrashlyticsEnabled: true));
                    if (global::Firebase.FirebaseApp.Instance is not null) FirebaseAvailability.MarkReady();
                }
                catch (Exception exception) { FirebaseAvailability.MarkUnavailable(exception); }
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
                    if (global::Firebase.Core.App.DefaultInstance is not null) FirebaseAvailability.MarkReady();
                }
                catch (Exception exception) { FirebaseAvailability.MarkUnavailable(exception); }
                return true;
            }));
#endif
        });
        builder.Services.AddSingleton<IAuthService, FirebaseAuthService>();
        builder.Services.AddSingleton<IFirestoreService, FirestoreService>();
        builder.Services.AddSingleton<ILocationEnvironment, MauiLocationEnvironment>();
        builder.Services.AddSingleton<ILocationRemoteSource, FirestoreLocationSource>();
        builder.Services.AddSingleton<ILocationService, LocationService>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<SplashPage1>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TourPage>();
        builder.Services.AddTransient<LocationsPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<QrScanPage>();
        builder.Services.AddTransient<TourViewPage>();
        builder.Services.AddTransient<HotspotInfoOverlayPage>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
