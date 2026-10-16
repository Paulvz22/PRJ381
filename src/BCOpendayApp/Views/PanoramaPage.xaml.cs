using System.Text.Json.Nodes;
using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

/// <summary>
/// Bundled 360° sample tour. Pannellum and the tour config are read from Resources/Raw/pano and merged
/// into one HTML document. The equirectangular photos are inlined as base64 data URIs: WebView WebGL
/// refuses to load texture images from packaged file:///asset URLs.
/// </summary>
public partial class PanoramaPage : BasePage
{
    private const string AssetRoot = "pano/";
    private const string TourAsset = AssetRoot + "tour.json";
    private const string CssAsset = AssetRoot + "pannellum.css";
    private const string JsAsset = AssetRoot + "pannellum.js";

    private readonly IDialogService dialogs;
    private bool isLoaded;

    public PanoramaPage(IDialogService dialogs)
    {
        this.dialogs = dialogs;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (isLoaded)
        {
            return;
        }

        try
        {
            TourWebView.Source = new HtmlWebViewSource { Html = await BuildHtmlAsync() };
            LoadingState.IsVisible = false;
            TourWebView.IsVisible = true;
            isLoaded = true;
        }
        catch (Exception exception)
        {
            LoadingState.IsVisible = false;
            await dialogs.AlertAsync(
                "360 tour unavailable",
                $"The sample panorama tour could not be loaded. Please try again.\n\n{exception.Message}");
        }
    }

    private static async Task<string> BuildHtmlAsync()
    {
        var css = await ReadTextAssetAsync(CssAsset);
        var js = await ReadTextAssetAsync(JsAsset);
        var tour = await BuildTourConfigAsync();

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no" />
            <title>360 virtual tour</title>
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

    /// <summary>Reads tour.json and swaps every scene's panorama path for an inlined base64 image.</summary>
    private static async Task<string> BuildTourConfigAsync()
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

        // Escaping the slash keeps a "</script>" inside a string from ending the script block early.
        return root.ToJsonString().Replace("</", "<\\/");
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
