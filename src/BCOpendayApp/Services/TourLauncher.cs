namespace BCOpendayApp.Services;

public static class TourLauncher
{
    public const string TourPackage = "com.belgiumcampus.campustour";

    public static bool Launch()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var intent = activity?.PackageManager?.GetLaunchIntentForPackage(TourPackage);
        if (activity == null || intent == null) return false;
        activity.StartActivity(intent);
        return true;
#else
           return false;
#endif
    }
}
