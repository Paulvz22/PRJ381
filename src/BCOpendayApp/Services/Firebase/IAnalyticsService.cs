namespace BCOpendayApp.Services;

/// <summary>Usage analytics and non-fatal error reporting. Does nothing when Firebase is not configured.</summary>
public interface IAnalyticsService
{
    void LogScreenView(string screenName);
    void LogHotspotTapped(string hotspotId, string hotspotName);
    void LogLocationOpened(string locationId, string locationName);
    void LogError(string area, Exception exception);

    /// <summary>Sends one harmless non-fatal error so you can confirm Crashlytics is receiving reports.</summary>
    void TestCrashlytics();
}
