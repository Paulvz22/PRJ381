using System.Numerics;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Sensors;

/// <summary>Live camera direction and visitor position for the AR view and the scanner.</summary>
public interface IArSensorService
{
    /// <summary>Raised (on the UI thread) when the smoothed camera heading/pitch changes.</summary>
    event EventHandler? PoseChanged;

    /// <summary>Raised (on the UI thread) when a new GPS fix arrives.</summary>
    event EventHandler? PositionChanged;

    /// <summary>Smoothed camera direction, heading relative to TRUE north when <see cref="IsTrueNorth"/>.</summary>
    CameraPose? Pose { get; }

    /// <summary>Filtered position (null until the first fix).</summary>
    GeoPoint? Position { get; }

    /// <summary>Latest raw (unfiltered) fix and its reported accuracy - the scanner averages these itself.</summary>
    (GeoPoint Point, double AccuracyMeters)? RawFix { get; }

    /// <summary>Estimated position accuracy in metres (lower is better).</summary>
    double AccuracyMeters { get; }

    /// <summary>False when the magnetic declination could not be applied (headings are then magnetic).</summary>
    bool IsTrueNorth { get; }

    /// <summary>False on devices without the orientation sensor; the less accurate compass is used instead.</summary>
    bool HasOrientationSensor { get; }

    /// <summary>Starts GPS + orientation. Permissions must already be granted. Returns false if GPS could not start.</summary>
    Task<bool> StartAsync();

    void Stop();
}

/// <summary>
/// Uses .NET MAUI's OrientationSensor (Android "rotation vector": gyroscope + accelerometer + magnetometer
/// fusion) for the camera direction, and Geolocation for the position. Android reports magnetic north, so the
/// local magnetic declination (about -18° around Pretoria) is added to get true north, which is what GPS
/// bearings use. Without that correction every label would be off by ~18°.
/// </summary>
public sealed class ArSensorService : IArSensorService
{
    private readonly HeadingFilter heading = new(0.18);
    private readonly PositionFilter position = new();
    private double pitch;
    private double declination;
    private bool hasDeclination;
    private GeoPoint? declinationAt;
    private int listeners;
    private bool usingCompass;

    public event EventHandler? PoseChanged;

    public event EventHandler? PositionChanged;

    public CameraPose? Pose => heading.Value is { } value ? new CameraPose(value, pitch) : null;

    public GeoPoint? Position => position.Position;

    public (GeoPoint Point, double AccuracyMeters)? RawFix { get; private set; }

    public double AccuracyMeters => position.AccuracyMeters;

    public bool IsTrueNorth => hasDeclination;

    public bool HasOrientationSensor => OrientationSensor.Default.IsSupported;

    public async Task<bool> StartAsync()
    {
        // Pages can overlap (e.g. scanner -> capture). Keep the sensors on while anyone needs them.
        if (Interlocked.Increment(ref listeners) > 1)
        {
            return true;
        }

        heading.Reset();
        StartOrientation();

        try
        {
            Geolocation.Default.LocationChanged += OnLocationChanged;
            if (await Geolocation.Default.GetLastKnownLocationAsync() is { } last
                && DateTimeOffset.UtcNow - last.Timestamp < TimeSpan.FromMinutes(2))
            {
                OnLocation(last);
            }

            return await Geolocation.Default.StartListeningForegroundAsync(
                new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1)));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"GPS start failed: {exception}");
            return false;
        }
    }

    public void Stop()
    {
        if (Interlocked.Decrement(ref listeners) > 0)
        {
            return;
        }

        listeners = 0;
        try
        {
            Geolocation.Default.LocationChanged -= OnLocationChanged;
            Geolocation.Default.StopListeningForeground();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }

        StopOrientation();
    }

    private void StartOrientation()
    {
        try
        {
#if ANDROID
            if (OrientationSensor.Default.IsSupported)
            {
                OrientationSensor.Default.ReadingChanged += OnOrientation;
                if (!OrientationSensor.Default.IsMonitoring)
                {
                    OrientationSensor.Default.Start(SensorSpeed.UI);
                }

                usingCompass = false;
                return;
            }
#endif
            // iOS reports orientation against an arbitrary north, and some phones lack the fused sensor:
            // fall back to the compass (heading only, less stable when the phone is held upright).
            if (Compass.Default.IsSupported)
            {
                Compass.Default.ReadingChanged += OnCompass;
                if (!Compass.Default.IsMonitoring)
                {
                    Compass.Default.Start(SensorSpeed.UI, applyLowPassFilter: true);
                }

                usingCompass = true;
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Orientation start failed: {exception}");
        }
    }

    private void StopOrientation()
    {
        try
        {
            if (usingCompass)
            {
                Compass.Default.ReadingChanged -= OnCompass;
                if (Compass.Default.IsMonitoring)
                {
                    Compass.Default.Stop();
                }
            }
            else
            {
                OrientationSensor.Default.ReadingChanged -= OnOrientation;
                if (OrientationSensor.Default.IsMonitoring)
                {
                    OrientationSensor.Default.Stop();
                }
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    private void OnOrientation(object? sender, OrientationSensorChangedEventArgs e)
    {
        Quaternion q = e.Reading.Orientation;
        var w = SensorMath.RepairW(q.X, q.Y, q.Z, q.W);
        if (SensorMath.CameraPoseFromQuaternion(q.X, q.Y, q.Z, w) is not { } pose)
        {
            return;
        }

        // Pointing straight up/down gives no usable heading.
        if (Math.Abs(pose.Pitch) < 75)
        {
            heading.Add(pose.Heading + (hasDeclination ? declination : 0));
        }

        pitch += (pose.Pitch - pitch) * 0.2;
        Raise(PoseChanged);
    }

    private void OnCompass(object? sender, CompassChangedEventArgs e)
    {
        heading.Add(e.Reading.HeadingMagneticNorth + (hasDeclination ? declination : 0));
        pitch = 0;
        Raise(PoseChanged);
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e) => OnLocation(e.Location);

    private void OnLocation(Location location)
    {
        var accuracy = location.Accuracy ?? 30;
        var fix = new GeoPoint(location.Latitude, location.Longitude);
        RawFix = (fix, accuracy);
        position.Add(fix, accuracy, location.Timestamp);
        UpdateDeclination(fix, location.Altitude ?? 1300);
        Raise(PositionChanged);
    }

    private void UpdateDeclination(GeoPoint fix, double altitude)
    {
        if (declinationAt is { } at && GeoMath.DistanceMeters(at, fix) < 5_000)
        {
            return;
        }

#if ANDROID
        try
        {
            var field = new Android.Hardware.GeomagneticField(
                (float)fix.Lat, (float)fix.Lng, (float)altitude, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var newDeclination = field.Declination;
            if (hasDeclination)
            {
                heading.Reset();
            }

            declination = newDeclination;
            hasDeclination = true;
            declinationAt = fix;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Declination unavailable: {exception.Message}");
        }
#endif
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        if (MainThread.IsMainThread)
        {
            handler(this, EventArgs.Empty);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => handler(this, EventArgs.Empty));
        }
    }
}
