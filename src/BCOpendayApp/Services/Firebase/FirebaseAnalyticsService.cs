using System.Diagnostics;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.Analytics;
using Plugin.Firebase.Crashlytics;
#endif

namespace BCOpendayApp.Services;

/// <summary>
/// Firebase Analytics + Crashlytics. Every call is a safe no-op when the build has no Firebase config
/// (no google-services.json) or Firebase has not finished starting, and a failure here can never crash the app.
/// </summary>
public sealed class FirebaseAnalyticsService : IAnalyticsService
{
    public void LogScreenView(string screenName)
    {
#if FIREBASE_CONFIGURED
        Safely(() => CrossFirebaseAnalytics.Current.LogEvent(
            "screen_view",
            ("screen_name", screenName)));
#endif
    }

    public void LogHotspotTapped(string hotspotId, string hotspotName)
    {
#if FIREBASE_CONFIGURED
        Safely(() => CrossFirebaseAnalytics.Current.LogEvent(
            "hotspot_tapped",
            ("hotspot_id", hotspotId),
            ("hotspot_name", hotspotName)));
#endif
    }

    public void LogLocationOpened(string locationId, string locationName)
    {
#if FIREBASE_CONFIGURED
        Safely(() => CrossFirebaseAnalytics.Current.LogEvent(
            "location_opened",
            ("location_id", locationId),
            ("location_name", locationName)));
#endif
    }

    public void LogError(string area, Exception exception)
    {
#if FIREBASE_CONFIGURED
        Safely(() =>
        {
            CrossFirebaseCrashlytics.Current.SetCustomKey("error_area", area);
            CrossFirebaseCrashlytics.Current.Log($"Error in {area}: {exception.Message}");
            CrossFirebaseCrashlytics.Current.RecordException(exception);

            CrossFirebaseAnalytics.Current.LogEvent(
                "app_error",
                ("error_area", area),
                ("error_message", exception.Message));
        });
#endif
    }

    public void TestCrashlytics()
    {
#if FIREBASE_CONFIGURED
        Safely(() =>
        {
            var testException = new Exception("Test non-fatal Crashlytics error from the analytics service");

            CrossFirebaseCrashlytics.Current.SetCustomKey("test_type", "safe_test");
            CrossFirebaseCrashlytics.Current.Log("A safe Crashlytics test was triggered.");
            CrossFirebaseCrashlytics.Current.RecordException(testException);

            CrossFirebaseAnalytics.Current.LogEvent(
                "crashlytics_test",
                ("test_type", "non_fatal"));
        });
#endif
    }

#if FIREBASE_CONFIGURED
    private static void Safely(Action action)
    {
        if (!FirebaseAvailability.IsReady)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Analytics call failed: {exception.Message}");
        }
    }
#endif
}
