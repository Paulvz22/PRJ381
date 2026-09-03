using Microsoft.Extensions.Logging;
using BCOpendayApp.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.Auth;
#if IOS
using Plugin.Firebase.Core.Platforms.iOS;
#elif ANDROID
using Plugin.Firebase.Core.Platforms.Android;
#endif

namespace BCOpendayApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .RegisterFirebaseServices();
#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }

        private static MauiAppBuilder RegisterFirebaseServices(this MauiAppBuilder builder)
        {
            builder.ConfigureLifecycleEvents(events =>
            {
#if IOS
                events.AddiOS(iOS => iOS.WillFinishLaunching((_, __) =>
                {
                    CrossFirebase.Initialize();
                    _ = new FirebaseAuthService().EnsureSignedInAsync();
                    return false;
                }));
#elif ANDROID
                events.AddAndroid(android => android.OnCreate((activity, bundle) =>
                {
                    CrossFirebase.Initialize(activity, activityLocator: () => Platform.CurrentActivity);
                    _ = new FirebaseAuthService().EnsureSignedInAsync();
                }));
#endif
            });

            builder.Services.AddSingleton<FirebaseAuthService>();
            builder.Services.AddSingleton<FirestoreService>();

            return builder;
        }
    }
}