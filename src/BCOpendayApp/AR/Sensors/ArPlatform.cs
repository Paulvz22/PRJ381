namespace BCOpendayApp.AR.Sensors;

/// <summary>Camera/location permissions, back-camera optics and screen helpers for the AR pages.</summary>
public static class ArPlatform
{
    /// <summary>Asks for camera (optional) and precise location. Must be called on the UI thread.</summary>
    public static async Task<(bool Camera, bool Location)> RequestPermissionsAsync(bool needCamera)
    {
        var location = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (location != PermissionStatus.Granted)
        {
            location = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }

        var camera = PermissionStatus.Granted;
        if (needCamera)
        {
            camera = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (camera != PermissionStatus.Granted)
            {
                camera = await Permissions.RequestAsync<Permissions.Camera>();
            }
        }

        return (camera == PermissionStatus.Granted, location == PermissionStatus.Granted);
    }

    /// <summary>
    /// Focal length divided by the sensor's short and long side, for the back camera. Used to place labels on
    /// the preview. Falls back to a typical phone camera (~60° across the short side) if it cannot be read.
    /// </summary>
    public static (double FocalOverShort, double FocalOverLong) BackCameraOptics()
    {
        const double fallbackShort = 0.866; // 1 / (2 * tan(30°))
        const double fallbackLong = 0.65;

#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Android.Content.Context.CameraService) is Android.Hardware.Camera2.CameraManager manager)
            {
                foreach (var id in manager.GetCameraIdList())
                {
                    var characteristics = manager.GetCameraCharacteristics(id);
                    var facing = characteristics.Get(Android.Hardware.Camera2.CameraCharacteristics.LensFacing) as Java.Lang.Integer;
                    if (facing is null || facing.IntValue() != 1) // 1 = CameraMetadata.LENS_FACING_BACK
                    {
                        continue;
                    }

                    var focalLengths = (float[]?)characteristics.Get(Android.Hardware.Camera2.CameraCharacteristics.LensInfoAvailableFocalLengths);
                    var size = characteristics.Get(Android.Hardware.Camera2.CameraCharacteristics.SensorInfoPhysicalSize) as Android.Util.SizeF;
                    if (focalLengths is { Length: > 0 } && size is not null && size.Width > 0 && size.Height > 0)
                    {
                        var shortSide = Math.Min(size.Width, size.Height);
                        var longSide = Math.Max(size.Width, size.Height);
                        return (focalLengths[0] / shortSide, focalLengths[0] / longSide);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Camera optics unavailable: {exception.Message}");
        }
#endif
        return (fallbackShort, fallbackLong);
    }

    /// <summary>Keeps the AR screen in portrait (sensor maths assume it) and awake while it is open.</summary>
    public static void EnterArMode()
    {
        DeviceDisplay.Current.KeepScreenOn = true;
#if ANDROID
        if (Platform.CurrentActivity is { } activity)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Portrait;
        }
#endif
    }

    public static void ExitArMode()
    {
        DeviceDisplay.Current.KeepScreenOn = false;
#if ANDROID
        if (Platform.CurrentActivity is { } activity)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Unspecified;
        }
#endif
    }
}
