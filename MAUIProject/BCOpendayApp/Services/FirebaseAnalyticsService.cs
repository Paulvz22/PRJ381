using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin.Firebase.Analytics;
using Plugin.Firebase.Crashlytics;

namespace BCOpendayApp.Services
{
    public class FirebaseAnalyticsService : IAnalyticsService
    {
        public void LogScreenView(string screenName)
        {
            CrossFirebaseAnalytics.Current.LogEvent(
                "screen_view",
                ("screen_name", screenName));
        }

        public void LogHotspotTapped(string hotspotId, string hotspotName)
        {
            CrossFirebaseAnalytics.Current.LogEvent(
                "hotspot_tapped",
                ("hotspot_id", hotspotId),
                ("hotspot_name", hotspotName));
        }

        public void LogLocationOpened(string locationId, string locationName)
        {
            CrossFirebaseAnalytics.Current.LogEvent(
                "location_opened",
                ("location_id", locationId),
                ("location_name", locationName));
        }

        public void LogError(string area, Exception exception)
        {
            CrossFirebaseCrashlytics.Current.SetCustomKey("error_area", area);
            CrossFirebaseCrashlytics.Current.Log($"Error in {area}: {exception.Message}");
            CrossFirebaseCrashlytics.Current.RecordException(exception);

            CrossFirebaseAnalytics.Current.LogEvent(
                "app_error",
                ("error_area", area),
                ("error_message", exception.Message));
        }

        public void TestCrashlytics()
        {
            var testException = new Exception("Test non-fatal Crashlytics error from Member 2 analytics service");

            CrossFirebaseCrashlytics.Current.SetCustomKey("test_type", "member_2_safe_test");
            CrossFirebaseCrashlytics.Current.Log("Member 2 triggered a safe Crashlytics test.");
            CrossFirebaseCrashlytics.Current.RecordException(testException);

            CrossFirebaseAnalytics.Current.LogEvent(
                "crashlytics_test",
                ("test_type", "non_fatal"),
                ("triggered_by", "member_2"));
        }
    }
}