using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Scanner.ViewModels;
using BCOpendayApp.Scanner.Views;

namespace BCOpendayApp.Scanner;

/// <summary>Shell routes of the scanner. The entry point is always <see cref="Login"/>.</summary>
public static class ScannerRoutes
{
    public const string Login = "scanner-login";
    public const string Home = "scanner-home";
    public const string Capture = "scanner-capture";
}

/// <summary>
/// Registers the staff building scanner:  builder.UseBuildingScanner();  (call AFTER builder.UseBuildingAr(),
/// it reuses the AR feature's building repository and sensors).
/// </summary>
public static class ScannerFeature
{
    public static MauiAppBuilder UseBuildingScanner(this MauiAppBuilder builder)
    {
        builder.Services
            .AddSingleton<IScannerAuthService, ScannerAuthService>()
            .AddSingleton<ScannerDraftStore>()
            .AddTransient<ScannerLoginViewModel>()
            .AddTransient<ScannerHomeViewModel>()
            .AddTransient<ScannerCaptureViewModel>()
            .AddTransient<ScannerLoginPage>()
            .AddTransient<ScannerHomePage>()
            .AddTransient<ScannerCapturePage>();

        Routing.RegisterRoute(ScannerRoutes.Login, typeof(ScannerLoginPage));
        Routing.RegisterRoute(ScannerRoutes.Home, typeof(ScannerHomePage));
        Routing.RegisterRoute(ScannerRoutes.Capture, typeof(ScannerCapturePage));
        return builder;
    }
}
