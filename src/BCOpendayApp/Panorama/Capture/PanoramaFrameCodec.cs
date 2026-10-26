namespace BCOpendayApp.Panorama.Capture;

/// <summary>
/// Turns camera JPEGs into pixel arrays for <see cref="PanoramaStitcher"/> and writes the result back as a JPEG.
/// Uses Android's own Bitmap APIs, so no image library has to be added to the project. Capturing panoramas is
/// therefore Android-only (the target platform of the app); on other platforms <see cref="IsSupported"/> is false.
/// </summary>
public static class PanoramaFrameCodec
{
    /// <summary>Photos are scaled to this width (portrait) before stitching: enough for a 4096-wide panorama.</summary>
    public const int FrameWidth = 720;

    public static bool IsSupported =>
#if ANDROID
        true;
#else
        false;
#endif

    /// <summary>
    /// Decodes a camera JPEG, applies its EXIF rotation, turns it upright for a phone held in portrait (camera
    /// sensors are landscape, and some phones store the rotation only as a tag) and scales it to
    /// <see cref="FrameWidth"/> wide. Returns the pixels (0xAARRGGBB) and the size.
    /// </summary>
    public static (int[] Pixels, int Width, int Height) DecodePortrait(byte[] jpeg, int sensorOrientationDegrees)
    {
#if ANDROID
        var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            throw new InvalidDataException("The camera returned an unreadable photo.");
        }

        // Decode at roughly 2x the size needed (power-of-two subsampling keeps memory low and is fast).
        var shortSide = Math.Min(bounds.OutWidth, bounds.OutHeight);
        var sample = 1;
        while (shortSide / (sample * 2) >= FrameWidth * 2)
        {
            sample *= 2;
        }

        var options = new Android.Graphics.BitmapFactory.Options { InSampleSize = sample };
        var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, options)
            ?? throw new InvalidDataException("The camera photo could not be decoded.");

        var rotation = ExifRotation(jpeg);
        if (rotation == 0 && decoded.Width > decoded.Height)
        {
            // No rotation tag but still landscape: the pixels are in sensor orientation (usually 90°).
            rotation = sensorOrientationDegrees is 90 or 270 ? sensorOrientationDegrees : 90;
        }

        var upright = decoded;
        if (rotation != 0)
        {
            using var matrix = new Android.Graphics.Matrix();
            matrix.PostRotate(rotation);
            upright = Android.Graphics.Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)
                ?? throw new InvalidDataException("The camera photo could not be rotated.");
            decoded.Recycle();
        }

        var width = FrameWidth;
        var height = (int)Math.Round(upright.Height * (double)FrameWidth / upright.Width);
        var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(upright, width, height, true)
            ?? throw new InvalidDataException("The camera photo could not be scaled.");
        if (!ReferenceEquals(scaled, upright))
        {
            upright.Recycle();
        }

        var pixels = new int[width * height];
        scaled.GetPixels(pixels, 0, width, 0, 0, width, height);
        scaled.Recycle();
        return (pixels, width, height);
#else
        throw new PlatformNotSupportedException("Panorama capture is only available on Android.");
#endif
    }

    /// <summary>Writes the stitched panorama as a JPEG file.</summary>
    public static void SaveJpeg(int[] pixels, int width, int height, string path, int quality = 88)
    {
#if ANDROID
        using var bitmap = Android.Graphics.Bitmap.CreateBitmap(pixels, width, height, Android.Graphics.Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException("Could not create the panorama image.");
        using (var stream = File.Create(path))
        {
            if (!bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, quality, stream))
            {
                throw new IOException("Could not encode the panorama as JPEG.");
            }
        }

        bitmap.Recycle();
#else
        throw new PlatformNotSupportedException("Panorama capture is only available on Android.");
#endif
    }

    /// <summary>
    /// Turns any photo (camera or gallery) into an upright JPEG whose longest side is at most
    /// <paramref name="maxSide"/> and whose size is at most <paramref name="maxBytes"/> (quality is lowered,
    /// then the size, until it fits). Used for the single location photo.
    /// </summary>
    public static byte[] ShrinkJpeg(byte[] image, int maxSide, int maxBytes)
    {
#if ANDROID
        var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(image, 0, image.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            throw new InvalidDataException("This file is not a readable photo.");
        }

        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxSide)
        {
            sample *= 2;
        }

        var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(
                image, 0, image.Length, new Android.Graphics.BitmapFactory.Options { InSampleSize = sample })
            ?? throw new InvalidDataException("The photo could not be decoded.");

        var rotation = ExifRotation(image);
        for (var side = maxSide; side >= 320; side = side * 3 / 4)
        {
            var scale = Math.Min(1.0, side / (double)Math.Max(bitmap.Width, bitmap.Height));
            using var matrix = new Android.Graphics.Matrix();
            matrix.PostScale((float)scale, (float)scale);
            matrix.PostRotate(rotation);
            var resized = Android.Graphics.Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)
                ?? throw new InvalidDataException("The photo could not be resized.");
            try
            {
                foreach (var quality in new[] { 85, 75, 65 })
                {
                    using var stream = new MemoryStream();
                    resized.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, quality, stream);
                    if (stream.Length <= maxBytes)
                    {
                        return stream.ToArray();
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(resized, bitmap))
                {
                    resized.Recycle();
                }
            }
        }

        throw new InvalidDataException("The photo could not be made small enough.");
#else
        throw new PlatformNotSupportedException("Photos can only be added on Android.");
#endif
    }

#if ANDROID
    private static int ExifRotation(byte[] jpeg)
    {
        try
        {
            using var stream = new MemoryStream(jpeg);
            var exif = new Android.Media.ExifInterface(stream);
            return exif.GetAttributeInt(Android.Media.ExifInterface.TagOrientation, 1) switch
            {
                6 => 90,
                3 => 180,
                8 => 270,
                _ => 0,
            };
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"EXIF unreadable: {exception.Message}");
            return 0;
        }
    }
#endif
}
