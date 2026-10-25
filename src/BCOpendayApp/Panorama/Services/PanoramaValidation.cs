using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>Documented limits for a local panorama import.</summary>
public static class PanoramaImageRules
{
    /// <summary>A full equirectangular sphere is 2:1 (width:height).</summary>
    public const double FullSphereAspectRatio = 2.0;

    /// <summary>
    /// Accept a ratio within 2% of 2:1 (1.96 to 2.04). This tolerates cameras that round one dimension while
    /// still rejecting ordinary phone photos and partial panoramas.
    /// </summary>
    public const double AspectRatioTolerance = 0.02;

    public const double MinAspectRatio = FullSphereAspectRatio * (1 - AspectRatioTolerance);
    public const double MaxAspectRatio = FullSphereAspectRatio * (1 + AspectRatioTolerance);

    public const int MinWidth = 2048;
    public const int MinHeight = 1024;
    public const int MaxWidth = 8192;
    public const int MaxHeight = 4096;

    /// <summary>Keeps the WebView's base64 HTML document within a practical memory limit.</summary>
    public const long MaxFileSizeBytes = 20L * 1024 * 1024;

    public static bool IsFullSphereAspectRatio(double width, double height) =>
        width > 0 && height > 0
        && width / height >= MinAspectRatio
        && width / height <= MaxAspectRatio;

    public static bool IsSupportedExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        return string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record PanoramaImageInfo(
    PanoramaImageFormat Format,
    int Width,
    int Height,
    long FileSizeBytes)
{
    public double AspectRatio => Height == 0 ? 0 : Width / (double)Height;
}

public sealed record PanoramaValidationResult(bool IsValid, string Message, PanoramaImageInfo? Image);

/// <summary>Validates an imported file before it can become a local full-sphere draft.</summary>
public static class PanoramaValidation
{
    public static PanoramaValidationResult ValidateFile(string filePath, string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Invalid("The selected file could not be found. Choose it again.");
        }

        if (!PanoramaImageRules.IsSupportedExtension(originalFileName))
        {
            return Invalid("Only JPEG and PNG images are supported. Choose a .jpg, .jpeg or .png file.");
        }

        long fileSize;
        try
        {
            fileSize = new FileInfo(filePath).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("The selected file could not be opened. Check that it is readable and try again.");
        }

        if (fileSize <= 0)
        {
            return Invalid("The selected file is empty.");
        }

        if (fileSize > PanoramaImageRules.MaxFileSizeBytes)
        {
            return Invalid(
                $"The image is {FormatMegabytes(fileSize)}. The limit is "
                + $"{FormatMegabytes(PanoramaImageRules.MaxFileSizeBytes)}.");
        }

        PanoramaImageFormat format;
        int width;
        int height;
        try
        {
            using var stream = File.OpenRead(filePath);
            if (!PanoramaImageReader.TryReadDimensions(stream, out format, out width, out height))
            {
                return Invalid("The file is not a readable JPEG or PNG image. It may be corrupt or unsupported.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("The selected file could not be read. Check that it is not damaged and try again.");
        }

        var expected = ExpectedFormat(originalFileName);
        if (expected != format)
        {
            return Invalid("The file name does not match the image contents.");
        }

        if (width < PanoramaImageRules.MinWidth || height < PanoramaImageRules.MinHeight)
        {
            return Invalid(
                $"The image is too small ({width} × {height}). Use at least "
                + $"{PanoramaImageRules.MinWidth} × {PanoramaImageRules.MinHeight}.");
        }

        if (width > PanoramaImageRules.MaxWidth || height > PanoramaImageRules.MaxHeight)
        {
            return Invalid(
                $"The image is too large ({width} × {height}). The maximum is "
                + $"{PanoramaImageRules.MaxWidth} × {PanoramaImageRules.MaxHeight}.");
        }

        if (!PanoramaImageRules.IsFullSphereAspectRatio(width, height))
        {
            var ratio = width / (double)height;
            return Invalid(
                $"This image is {width} × {height} ({ratio:0.00}:1). A full 360° panorama must be "
                + "about 2:1 (for example 4096 × 2048); a normal photo or partial panorama is not accepted.");
        }

        var info = new PanoramaImageInfo(format, width, height, fileSize);
        return new PanoramaValidationResult(
            true,
            $"Valid 2:1 full-sphere image ({width} × {height}, {FormatMegabytes(fileSize)}). "
            + "The preview is flat; open the saved draft for the 360° view.",
            info);
    }

    private static PanoramaImageFormat ExpectedFormat(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? PanoramaImageFormat.Png
            : PanoramaImageFormat.Jpeg;
    }

    private static PanoramaValidationResult Invalid(string message) => new(false, message, null);

    private static string FormatMegabytes(long bytes) => $"{bytes / (1024d * 1024d):0.0} MB";
}

/// <summary>Re-validates metadata read back from the local manifest before it is trusted.</summary>
public static class PanoramaDraftValidation
{
    private const long MaxUnixTimeMilliseconds = 253402300799999;

    public static bool IsValid(PanoramaDraft? draft)
    {
        if (draft is null
            || !PanoramaLocalFiles.IsSafeDraftId(draft.Id)
            || !PanoramaLocalFiles.IsSafeDraftFileName(draft.FileName)
            || string.IsNullOrWhiteSpace(draft.DisplayName)
            || draft.DisplayName.Length > 100
            || draft.Width < PanoramaImageRules.MinWidth
            || draft.Height < PanoramaImageRules.MinHeight
            || draft.Width > PanoramaImageRules.MaxWidth
            || draft.Height > PanoramaImageRules.MaxHeight
            || !PanoramaImageRules.IsFullSphereAspectRatio(draft.Width, draft.Height)
            || draft.Projection != PanoramaDraft.EquirectangularProjection
            || !draft.IsFullSphere
            || draft.CaptureMethod != PanoramaDraft.GalleryImportMethod
            || draft.FileSizeBytes <= 0
            || draft.FileSizeBytes > PanoramaImageRules.MaxFileSizeBytes
            || draft.CreatedAtUnixMs <= 0
            || draft.CreatedAtUnixMs > MaxUnixTimeMilliseconds)
        {
            return false;
        }

        var expectedFileName = PanoramaLocalFiles.CreateDraftFileName(
            draft.Id,
            string.Equals(Path.GetExtension(draft.FileName), ".png", StringComparison.OrdinalIgnoreCase)
                ? PanoramaImageFormat.Png
                : PanoramaImageFormat.Jpeg);

        return string.Equals(draft.FileName, expectedFileName, StringComparison.Ordinal);
    }
}
