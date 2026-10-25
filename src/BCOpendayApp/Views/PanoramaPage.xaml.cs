using System.Net;
using System.Text.Json.Nodes;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

/// <summary>
/// Shows either the bundled sample tour or one validated local full-sphere draft. Pannellum and the tour config
/// are merged into one HTML document. Images are inlined as base64 data URIs because WebView WebGL refuses to
/// load texture images from packaged file:///asset URLs.
/// </summary>
[QueryProperty(nameof(DraftId), Routes.StopIdParameter)]
public partial class PanoramaPage : BasePage
{
    private const string AssetRoot = "pano/";
    private const string TourAsset = AssetRoot + "tour.json";
    private const string CssAsset = AssetRoot + "pannellum.css";
    private const string JsAsset = AssetRoot + "pannellum.js";

    private readonly IDialogService dialogs;
    private readonly PanoramaDraftStore drafts;
    private bool isLoaded;
    private string loadedKey = string.Empty;

    public PanoramaPage(IDialogService dialogs, PanoramaDraftStore drafts)
    {
        this.dialogs = dialogs;
        this.drafts = drafts;
        InitializeComponent();
    }

    /// <summary>Set by Shell when a local draft is opened; empty/null opens the bundled tour.</summary>
    public string? DraftId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var key = DraftId ?? string.Empty;
        if (isLoaded && string.Equals(loadedKey, key, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            PanoramaDraft? draft = null;
            if (!string.IsNullOrWhiteSpace(DraftId))
            {
                draft = await drafts.FindAsync(DraftId);
                if (draft is null)
                {
                    await dialogs.AlertAsync(
                        "Panorama not found",
                        "This local draft is no longer on this phone.");
                    await Shell.Current.GoToAsync("..");
                    return;
                }
            }

            LoadingState.IsVisible = true;
            TourWebView.IsVisible = false;
            Title = draft is null ? "360 virtual tour" : draft.DisplayName;
            TourWebView.Source = new HtmlWebViewSource { Html = await BuildHtmlAsync(draft) };
            LoadingState.IsVisible = false;
            TourWebView.IsVisible = true;
            isLoaded = true;
            loadedKey = key;
        }
        catch (Exception exception)
        {
            LoadingState.IsVisible = false;
            await dialogs.AlertAsync(
                "360 view unavailable",
                $"The panorama could not be loaded. Please try again.\n\n{exception.Message}");
        }
    }

    private async Task<string> BuildHtmlAsync(PanoramaDraft? draft)
    {
        var css = await ReadTextAssetAsync(CssAsset);
        var js = await ReadTextAssetAsync(JsAsset);
        var tour = draft is null
            ? await BuildStaticTourConfigAsync()
            : await BuildLocalTourConfigAsync(draft);
        var title = WebUtility.HtmlEncode(draft?.DisplayName ?? "360 virtual tour");

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no" />
            <title>{{title}}</title>
            <style>
            html, body, #panorama { width: 100%; height: 100%; margin: 0; padding: 0; overflow: hidden; background: #08090B; }
            </style>
            <style>
            {{css}}
            </style>
            <style>
            .pnlm-container { background: #08090B; }
            </style>
            </head>
            <body>
            <div id="panorama"></div>
            <script>
            {{js}}
            </script>
            <script>
            pannellum.viewer('panorama', {{tour}});
            </script>
            </body>
            </html>
            """;
    }

    /// <summary>Reads the bundled tour.json and swaps every scene's panorama path for an inlined base64 image.</summary>
    private static async Task<string> BuildStaticTourConfigAsync()
    {
        var json = await ReadTextAssetAsync(TourAsset);

        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("tour.json is not a JSON object.");

        if (root["scenes"] is not JsonObject scenes)
        {
            throw new InvalidOperationException("tour.json does not define any scenes.");
        }

        foreach (var scene in scenes.Select(pair => pair.Value).OfType<JsonObject>())
        {
            if (scene["panorama"] is JsonValue value
                && value.TryGetValue<string>(out var path)
                && !string.IsNullOrWhiteSpace(path))
            {
                scene["panorama"] = await BuildDataUriAsync(path);
            }
        }

        PanoramaViewerConfig.DisableDebugOverlays(root);
        return PanoramaViewerConfig.ToSafeScriptJson(root);
    }

    private async Task<string> BuildLocalTourConfigAsync(PanoramaDraft draft)
    {
        var path = drafts.GetImagePath(draft);
        var mimeType = string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
        var bytes = await File.ReadAllBytesAsync(path);
        var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        return PanoramaViewerConfig.BuildFullSphereTourJson(draft.DisplayName, dataUri);
    }

    private static async Task<string> BuildDataUriAsync(string relativePath)
    {
        var assetPath = relativePath.StartsWith(AssetRoot, StringComparison.OrdinalIgnoreCase)
            ? relativePath
            : AssetRoot + relativePath.TrimStart('.', '/');

        var mimeType = assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(assetPath);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return $"data:{mimeType};base64,{Convert.ToBase64String(buffer.ToArray())}";
    }

    private static async Task<string> ReadTextAssetAsync(string assetPath)
    {
        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(assetPath);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
