#if ANDROID
using Android.Content;
using Android.Locations;
using Android.OS;
using AndroidLocation = Android.Locations.Location;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace BCOpendayApp.AR.Sensors;

/// <summary>
/// Continuous location on Android straight from <see cref="LocationManager"/>: one fix per second, minimum
/// distance 0. This replaces Geolocation.StartListeningForegroundAsync, which (in .NET MAUI 9) registers its
/// listener with the requested accuracy as Android's minimum DISTANCE between updates - 50 m for "Best" - so a
/// phone carried from one building corner to the next got no new position at all.
/// GPS fixes are preferred; a network (Wi-Fi/cell) fix is only passed on when no GPS fix arrived recently.
/// </summary>
internal sealed class AndroidLocationFeed : Java.Lang.Object, ILocationListener
{
    private static readonly TimeSpan PreferGpsFor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxSeedAge = TimeSpan.FromMinutes(2);
    private const long MinTimeMilliseconds = 1000;

    private readonly Action<MauiLocation> onLocation;
    private LocationManager? manager;
    private DateTimeOffset lastGpsFix = DateTimeOffset.MinValue;
    private bool isListening;

    public AndroidLocationFeed(Action<MauiLocation> onLocation) => this.onLocation = onLocation;

    /// <summary>Starts updates (permission must already be granted). Returns false when no provider is on.</summary>
    public bool Start()
    {
        if (isListening)
        {
            return true;
        }

        manager ??= Android.App.Application.Context.GetSystemService(Context.LocationService) as LocationManager;
        if (manager is not { } locationManager)
        {
            return false;
        }

        var providers = new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider }
            .Where(provider => !string.IsNullOrEmpty(provider) && locationManager.IsProviderEnabled(provider))
            .ToList();
        if (providers.Count == 0)
        {
            return false; // Location is switched off in the phone's settings.
        }

        SeedFromLastKnown(providers);

        var looper = Looper.MainLooper;
        foreach (var provider in providers)
        {
            locationManager.RequestLocationUpdates(provider, MinTimeMilliseconds, 0f, this, looper);
        }

        isListening = true;
        return true;
    }

    public void Stop()
    {
        if (!isListening)
        {
            return;
        }

        isListening = false;
        try
        {
            manager?.RemoveUpdates(this);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Location stop failed: {exception.Message}");
        }
    }

    void ILocationListener.OnLocationChanged(AndroidLocation location)
    {
        var isGps = location.Provider == LocationManager.GpsProvider;
        var now = DateTimeOffset.UtcNow;
        if (isGps)
        {
            lastGpsFix = now;
        }
        else if (now - lastGpsFix < PreferGpsFor)
        {
            return; // a fresh GPS fix is better than this Wi-Fi/cell estimate
        }

        onLocation(Convert(location));
    }

    void ILocationListener.OnProviderDisabled(string provider)
    {
    }

    void ILocationListener.OnProviderEnabled(string provider)
    {
    }

    void ILocationListener.OnStatusChanged(string? provider, Availability status, Bundle? extras)
    {
    }

    /// <summary>Gives the pages a position straight away when the phone already knows a recent one.</summary>
    private void SeedFromLastKnown(IEnumerable<string> providers)
    {
        AndroidLocation? best = null;
        foreach (var provider in providers)
        {
            var candidate = manager?.GetLastKnownLocation(provider);
            if (candidate is not null && (best is null || candidate.Time > best.Time))
            {
                best = candidate;
            }
        }

        if (best is not null
            && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(best.Time) < MaxSeedAge)
        {
            onLocation(Convert(best));
        }
    }

    private static MauiLocation Convert(AndroidLocation location) =>
        new(location.Latitude, location.Longitude)
        {
            Altitude = location.HasAltitude ? location.Altitude : null,
            Accuracy = location.HasAccuracy ? location.Accuracy : null,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time),
        };
}
#endif
