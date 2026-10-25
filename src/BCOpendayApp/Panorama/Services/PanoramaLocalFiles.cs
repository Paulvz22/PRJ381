using System.Text.RegularExpressions;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>Names and paths used by the app-private panorama draft store. All names are validated before use.</summary>
public static class PanoramaLocalFiles
{
    public const string DraftsFolderName = "panorama-drafts";
    public const string ManifestFileName = "panorama-drafts.json";
    public const string TempFolderName = "panorama-imports";

    private static readonly Regex DraftIdPattern = new(
        "^[a-f0-9]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSafeDraftId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DraftIdPattern.IsMatch(value);

    public static string ExtensionFor(PanoramaImageFormat format) =>
        format == PanoramaImageFormat.Png ? ".png" : ".jpg";

    public static string CreateDraftFileName(string id, PanoramaImageFormat format) =>
        id + ExtensionFor(format);

    public static bool IsSafeDraftFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName != Path.GetFileName(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsSafeDraftId(Path.GetFileNameWithoutExtension(fileName));
    }

    public static string CreateTempFileName(string extension)
    {
        var normalized = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ".jpg",
            ".png" => ".png",
            _ => throw new InvalidOperationException("Only JPEG and PNG panorama images are supported."),
        };

        return "import-" + Guid.NewGuid().ToString("N") + normalized;
    }
}
