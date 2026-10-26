using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

namespace BCOpendayApp.Views;

/// <summary>
/// The 360° viewer. Shows ONE scene at a time - from the bundled tour or a panorama saved on this phone - with
/// arrows to the other scenes (hand-made ones from tour.json plus automatic ones from GPS, see
/// <see cref="PanoramaTour"/>). Tapping an arrow navigates to "bcpano://go?id=...", which this page intercepts and
/// answers by loading that scene. Images are inlined as base64 data URIs because WebView WebGL refuses to load
/// textures from packaged file:///asset URLs.
///
/// Look-around by moving the phone: Pannellum's own device-orientation support only works on https pages, so
/// the phone's orientation sensor is read here and sent to the page (window.bcOrient) about 30 times a second.
/// It is relative: wherever the phone points when it starts (or after a swipe) is the current view.
/// </summary>
[QueryProperty(nameof(SceneId), Routes.StopIdParameter)]
public partial class PanoramaPage : BasePage
{
    private const string AssetRoot = "pano/";
    private const string TourAsset = AssetRoot + "tour.json";
    private const string CssAsset = AssetRoot + "pannellum.css";
    private const string JsAsset = AssetRoot + "pannellum.js";
    private const string ViewerCssAsset = AssetRoot + "bc-viewer.css";
    private const string ViewerJsAsset = AssetRoot + "bc-viewer.js";
    private const string NavigationScheme = "bcpano://";
    private static readonly TimeSpan OrientationInterval = TimeSpan.FromMilliseconds(33);

    private readonly IDialogService dialogs;
    private readonly PanoramaDraftStore drafts;
    private readonly PanoramaOnlineLibrary online;
    private readonly IVisitorProgressService visitorProgress;
    private readonly HeadingFilter heading = new(0.35);
    private PanoramaTour? tour;
    private string loadedSceneId = string.Empty;
    private bool isLoading;
    private bool motionEnabled = true;
    private bool vrEnabled;
    private bool sensorRunning;
    private bool sendingOrientation;
    private double pitch;
    private DateTimeOffset lastSent = DateTimeOffset.MinValue;

    public PanoramaPage(
        IDialogService dialogs,
        PanoramaDraftStore drafts,
        PanoramaOnlineLibrary online,
        IVisitorProgressService visitorProgress)
    {
        this.dialogs = dialogs;
        this.drafts = drafts;
        this.online = online;
        this.visitorProgress = visitorProgress;
        InitializeComponent();
        TourWebView.Navigating += OnWebViewNavigating;
    }

    /// <summary>Scene to open (a bundled scene id or a local panorama id); empty opens the tour's first scene.</summary>
    public string? SceneId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            tour = await LoadTourAsync();

            // Coming back to this page: stay on the scene the visitor walked to, if it still exists.
            if (loadedSceneId.Length == 0 || tour.Find(loadedSceneId) is null)
            {
                var requested = tour.Find(SceneId);
                if (requested is null)
                {
                    await dialogs.AlertAsync("Panorama not found", "This 360° view is no longer on this phone.");
                    await Shell.Current.GoToAsync("..");
                    return;
                }

                await ShowSceneAsync(requested, initialYaw: null);
            }

            StartMotion();
        }
        catch (Exception exception)
        {
            LoadingState.IsVisible = false;
            await dialogs.AlertAsync(
                "360 view unavailable",
                $"The panorama could not be loaded. Please try again.\n\n{exception.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        if (vrEnabled)
        {
            vrEnabled = false;
            SetVrChrome(false);
            _ = TourWebView.EvaluateJavaScriptAsync("window.bcSetVr&&window.bcSetVr(false,false);");
        }

        StopMotion();
        base.OnDisappearing();
    }

    /// <summary>In VR the phone is in a headset: Back leaves VR instead of the page.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (vrEnabled)
        {
            _ = SetVrAsync(false);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async Task<PanoramaTour> LoadTourAsync()
    {
        JsonObject? bundled = null;
        try
        {
            bundled = JsonNode.Parse(await ReadTextAssetAsync(TourAsset)) as JsonObject;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Bundled tour unreadable: {exception.Message}");
        }

        IReadOnlyList<PanoramaDraft> local = Array.Empty<PanoramaDraft>();
        try
        {
            local = await drafts.LoadAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Local panoramas unreadable: {exception.Message}");
        }

        // Views staff uploaded (Firestore), or the saved list when offline.
        IReadOnlyList<PanoramaManifest> uploaded = Array.Empty<PanoramaManifest>();
        try
        {
            uploaded = (await online.GetListAsync()).Manifests;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Online views unreadable: {exception.Message}");
        }

        return PanoramaTour.Create(bundled, local, uploaded);
    }

    private async Task ShowSceneAsync(PanoramaScene scene, double? initialYaw)
    {
        if (isLoading || tour is null)
        {
            return;
        }

        isLoading = true;
        try
        {
            LoadingText.Text = "Loading the 360° tour...";
            LoadingState.IsVisible = true;
            TourWebView.IsVisible = false;
            Title = scene.Title;

            string image;
            if (scene.IsOnPhone && scene.Draft is { } draft)
            {
                image = await BuildDraftDataUriAsync(draft);
            }
            else if (scene.Online is { } manifest)
            {
                // First time on this phone: download it (later visits use the saved copy).
                LoadingText.Text = "Downloading this 360° view...";
                var path = await online.GetImagePathAsync(manifest);
                image = $"data:{manifest.MimeType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(path))}";
            }
            else
            {
                image = await BuildAssetDataUriAsync(scene.BundledImagePath
                    ?? throw new InvalidOperationException($"Scene '{scene.Id}' has no panorama image."));
            }

            var config = tour.BuildViewerConfig(scene, image, initialYaw);
            TourWebView.Source = new HtmlWebViewSource { Html = await BuildHtmlAsync(scene.Title, config) };

            loadedSceneId = scene.Id;
            LoadingState.IsVisible = false;
            TourWebView.IsVisible = true;

            // Opening a 360° view counts as visiting its campus location (journey progress on Home).
            visitorProgress.MarkVisited(scene.Draft is { } viewed ? CampusLocationCatalog.LocationIdFor(viewed) : scene.Id);
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>Arrow taps arrive as a navigation to bcpano://go?id=...&amp;yaw=...; load that scene instead.</summary>
    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url is null || !e.Url.StartsWith(NavigationScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        try
        {
            var query = ParseQuery(e.Url);
            if (tour?.Find(query.GetValueOrDefault("id")) is not { } target || target.Id == loadedSceneId)
            {
                return;
            }

            double? yaw = double.TryParse(query.GetValueOrDefault("yaw"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
            await ShowSceneAsync(target, yaw);
        }
        catch (Exception exception)
        {
            LoadingState.IsVisible = false;
            TourWebView.IsVisible = true;
            await dialogs.AlertAsync("Could not open that view", exception.Message);
        }
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var start = url.IndexOf('?');
        if (start < 0)
        {
            return result;
        }

        foreach (var pair in url[(start + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return result;
    }

    // ------------------------------------------------------------------ look around by moving the phone

    private void StartMotion()
    {
        if (!OrientationSensor.Default.IsSupported)
        {
            Chips.IsVisible = false;
            return;
        }

        Chips.IsVisible = !vrEnabled;
        UpdateMotionLabel();
        if (!(motionEnabled || vrEnabled) || sensorRunning)
        {
            return;
        }

        try
        {
            heading.Reset();
            OrientationSensor.Default.ReadingChanged += OnOrientation;
            if (!OrientationSensor.Default.IsMonitoring)
            {
                OrientationSensor.Default.Start(SensorSpeed.Game);
            }

            sensorRunning = true;
            _ = SetMotionInPageAsync(motionEnabled);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Orientation sensor unavailable: {exception.Message}");
            OrientationSensor.Default.ReadingChanged -= OnOrientation;
            Chips.IsVisible = false;
        }
    }

    private void StopMotion()
    {
        if (!sensorRunning)
        {
            return;
        }

        sensorRunning = false;
        try
        {
            OrientationSensor.Default.ReadingChanged -= OnOrientation;
            if (OrientationSensor.Default.IsMonitoring)
            {
                OrientationSensor.Default.Stop();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }

        _ = SetMotionInPageAsync(false);
    }

    private void OnMotionToggleTapped(object? sender, TappedEventArgs e)
    {
        motionEnabled = !motionEnabled;
        if (motionEnabled)
        {
            StartMotion();
        }
        else
        {
            StopMotion();
            UpdateMotionLabel();
        }
    }

    // ------------------------------------------------------------------ VR (phone in a headset)

    private async void OnVrTapped(object? sender, TappedEventArgs e)
    {
        if (!OrientationSensor.Default.IsSupported)
        {
            await dialogs.AlertAsync("VR not available", "This phone has no motion sensor, so VR mode cannot follow your head.");
            return;
        }

        await SetVrAsync(true);
    }

    /// <summary>
    /// VR: side-by-side view for both eyes, landscape, full screen, screen kept on, moving with the head.
    /// The visitor moves to another view by looking at its arrow for 1.5 s (bc-viewer.js).
    /// </summary>
    private async Task SetVrAsync(bool on)
    {
        if (on == vrEnabled)
        {
            return;
        }

        vrEnabled = on;
        SetVrChrome(on);
        if (on)
        {
            StartMotion(); // VR always follows the phone, whatever the "Move phone" setting is
        }
        else
        {
            if (!motionEnabled)
            {
                StopMotion();
            }

            Chips.IsVisible = OrientationSensor.Default.IsSupported;
        }

        try
        {
            await TourWebView.EvaluateJavaScriptAsync(on ? "window.bcSetVr&&window.bcSetVr(true,true);" : "window.bcSetVr&&window.bcSetVr(false,false);");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }

    private void SetVrChrome(bool on)
    {
        Shell.SetNavBarIsVisible(this, !on);
        Chips.IsVisible = !on && OrientationSensor.Default.IsSupported;
        DeviceDisplay.Current.KeepScreenOn = on;
#if ANDROID
        try
        {
            if (Platform.CurrentActivity is { } activity)
            {
                activity.RequestedOrientation = on
                    ? Android.Content.PM.ScreenOrientation.SensorLandscape
                    : Android.Content.PM.ScreenOrientation.Unspecified;

                if (activity.Window is { } window)
                {
                    var bars = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
                    if (on)
                    {
                        bars.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                        bars.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
                    }
                    else
                    {
                        bars.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
                    }
                }
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"VR screen setup failed: {exception.Message}");
        }
#endif
    }

    private void UpdateMotionLabel() =>
        MotionToggleLabel.Text = motionEnabled ? "Move phone: ON" : "Move phone: OFF (swipe)";

    private void OnOrientation(object? sender, OrientationSensorChangedEventArgs e)
    {
        var q = e.Reading.Orientation;
        var w = SensorMath.RepairW(q.X, q.Y, q.Z, q.W);
        if (SensorMath.CameraPoseFromQuaternion(q.X, q.Y, q.Z, w) is not { } pose)
        {
            return;
        }

        // Straight up/down has no usable heading: keep the last one and only follow the tilt.
        var smoothed = Math.Abs(pose.Pitch) < 80 || heading.Value is null ? heading.Add(pose.Heading) : heading.Value.Value;
        pitch += (pose.Pitch - pitch) * 0.35;

        var now = DateTimeOffset.UtcNow;
        if (sendingOrientation || now - lastSent < OrientationInterval || isLoading)
        {
            return;
        }

        lastSent = now;
        var script = string.Create(
            CultureInfo.InvariantCulture,
            $"window.bcOrient&&window.bcOrient({smoothed:0.00},{pitch:0.00});");
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (sendingOrientation || !sensorRunning)
            {
                return;
            }

            sendingOrientation = true;
            try
            {
                await TourWebView.EvaluateJavaScriptAsync(script);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception.Message);
            }
            finally
            {
                sendingOrientation = false;
            }
        });
    }

    private async Task SetMotionInPageAsync(bool on)
    {
        try
        {
            await TourWebView.EvaluateJavaScriptAsync(on ? "window.bcSetMotion&&window.bcSetMotion(true);" : "window.bcSetMotion&&window.bcSetMotion(false);");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }

    // ------------------------------------------------------------------ page building

    private async Task<string> BuildHtmlAsync(string sceneTitle, string config)
    {
        var css = await ReadTextAssetAsync(CssAsset);
        var js = await ReadTextAssetAsync(JsAsset);
        var viewerCss = await ReadTextAssetAsync(ViewerCssAsset);
        var viewerJs = await ReadTextAssetAsync(ViewerJsAsset);
        var title = WebUtility.HtmlEncode(sceneTitle);
        var options = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"motion\":{Js(motionEnabled && OrientationSensor.Default.IsSupported)},\"vr\":{Js(vrEnabled)},\"hint\":false}}");

        // Pannellum + its config, then bc-viewer.js (arrows, look-around by moving the phone, VR mode).
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no" />
            <title>{{title}}</title>
            <style>
            {{css}}
            </style>
            <style>
            {{viewerCss}}
            </style>
            </head>
            <body>
            <div id="panorama"></div>
            <div id="panoramaR"></div>
            <div id="bc-vr-tap"></div>
            <script>
            {{js}}
            </script>
            <script>
            {{viewerJs}}
            </script>
            <script>
            window.bcStart({{config}}, {{options}});
            </script>
            </body>
            </html>
            """;
    }

    private static string Js(bool value) => value ? "true" : "false";

    private async Task<string> BuildDraftDataUriAsync(PanoramaDraft draft)
    {
        var path = drafts.GetImagePath(draft);
        var mimeType = string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";
        var bytes = await File.ReadAllBytesAsync(path);
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static async Task<string> BuildAssetDataUriAsync(string relativePath)
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
