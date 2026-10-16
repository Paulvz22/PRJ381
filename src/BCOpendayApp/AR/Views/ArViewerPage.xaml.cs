using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Sensors;
using BCOpendayApp.AR.ViewModels;
using BCOpendayApp.Views;
using CommunityToolkit.Maui.Views;

namespace BCOpendayApp.AR.Views;

/// <summary>
/// Draws the AR overlay. Each building's label is placed with a pinhole-camera projection:
/// x = centre + focalPx · tan(angle between camera heading and the building), and y follows the horizon
/// (camera pitch) so labels stay "on" the buildings while the phone tilts. Which building is SELECTED does not
/// depend on this drawing - that is decided from the heading alone in <see cref="BuildingTargeting"/>.
/// </summary>
public partial class ArViewerPage : BasePage
{
    private const int MaxLabels = 8;

    private readonly ArViewerViewModel viewModel;
    private readonly Dictionary<string, (Border Card, Label Name, Label Distance)> labels = new();
    private readonly (double FocalOverShort, double FocalOverLong) optics = ArPlatform.BackCameraOptics();
    private readonly SolidColorBrush yellow = AppBrush("BcYellow");
    private readonly SolidColorBrush red = AppBrush("BcRed");
    private readonly SolidColorBrush white = new(Colors.White);
    private CancellationTokenSource? cameraStart;
    private CameraView? camera;
    private Border? destinationMarker;
    private bool isOnScreen;

    public ArViewerPage(ArViewerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        viewModel.FrameChanged += (_, _) => DrawOverlay();
        Overlay.SizeChanged += (_, _) => DrawOverlay();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isOnScreen = true;
        ArPlatform.EnterArMode();

        try
        {
            // Asks for location and camera ONE AT A TIME and waits for the answers before anything touches the camera.
            var cameraAllowed = await viewModel.StartAsync();

            // The visitor may have left the page while a permission prompt was open.
            if (cameraAllowed && isOnScreen)
            {
                await StartCameraAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AR start failed: {exception}");
        }
    }

    protected override void OnDisappearing()
    {
        isOnScreen = false;
        cameraStart?.Cancel();
        try
        {
            camera?.StopCameraPreview();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }

        viewModel.Stop();
        ArPlatform.ExitArMode();
        base.OnDisappearing();
    }

    private async Task StartCameraAsync()
    {
        if (camera is null)
        {
            // First time: create the CameraView now that the camera permission is granted. When its handler
            // connects, CommunityToolkit.Maui.Camera 4.0.0 asks for camera + microphone itself (camera is already
            // granted so that returns at once; the microphone is declared in the manifest only because the toolkit
            // asks for it - its answer does not matter) and then starts the preview on its own.
            camera = new CameraView();
            CameraHost.Content = camera;
            return;
        }

        // Coming back to the page: the view is already connected, so just restart the preview.
        cameraStart?.Cancel();
        cameraStart = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = cameraStart.Token;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (camera.SelectedCamera is null)
                {
                    var cameras = await camera.GetAvailableCameras(token);
                    camera.SelectedCamera = cameras.FirstOrDefault(c => c.Position.ToString() == "Rear") ?? cameras.FirstOrDefault();
                }

                await camera.StartCameraPreview(token);
                return;
            }
            catch (Exception) when (attempt < 2 && !token.IsCancellationRequested)
            {
                await Task.Delay(700, token);
            }
        }
    }

    private void DrawOverlay()
    {
        // The arrow always shows where the destination is relative to the camera, even before a GPS fix.
        NavArrow.Rotation = viewModel.NavTurnDegrees;

        var width = Overlay.Width;
        var height = Overlay.Height;
        if (viewModel.Pose is not { } pose || width <= 0 || height <= 0)
        {
            if (destinationMarker is not null)
            {
                destinationMarker.IsVisible = false;
            }

            return;
        }

        // Preview fills the screen (centre crop), so the focal length in screen points is the larger of the two fits.
        var focal = Math.Max(width * optics.FocalOverShort, height * optics.FocalOverLong);
        var cx = width / 2;
        var horizonY = height / 2 + focal * Math.Tan(GeoMath.ToRadians(Math.Clamp(pose.Pitch, -60, 60)));

        var visible = new HashSet<string>();
        foreach (var sighting in viewModel.Sightings.Where(s => !s.IsInside).Take(MaxLabels))
        {
            var offset = sighting.OffsetFrom(pose.Heading);
            if (Math.Abs(offset) > 75)
            {
                continue;
            }

            var x = cx + focal * Math.Tan(GeoMath.ToRadians(offset));
            if (x < -120 || x > width + 120)
            {
                continue;
            }

            // Assume ~12 m tall buildings: nearer ones get their label higher above the horizon.
            var elevation = Math.Atan2(12, Math.Max(sighting.DistanceMeters, 5));
            var y = Math.Clamp(horizonY - focal * Math.Tan(elevation), 120, Math.Max(120, height - 260));

            var (card, name, distance) = GetLabel(sighting.Building.Id);
            var isTarget = sighting.Building.Id == viewModel.TargetId;
            name.Text = sighting.Building.Name;
            distance.Text = ArViewerViewModel.FormatDistance(sighting.DistanceMeters);
            card.Stroke = isTarget ? yellow : red;
            card.Opacity = isTarget ? 1 : 0.8;
            card.ZIndex = isTarget ? 2 : 1;

            AbsoluteLayout.SetLayoutBounds(card, new Rect(x, y, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
            card.AnchorX = 0.5;
            card.TranslationX = -(card.Width > 0 ? card.Width : 120) / 2;
            card.IsVisible = true;
            visible.Add(sighting.Building.Id);
        }

        foreach (var (id, label) in labels)
        {
            if (!visible.Contains(id))
            {
                label.Card.IsVisible = false;
            }
        }

        Reticle.Stroke = viewModel.HasTarget ? yellow : white;

        DrawDestinationMarker(pose, focal, cx, horizonY, width, height);
    }

    /// <summary>
    /// When the destination is roughly in front of the camera (within ~40°) a marker is placed over it with
    /// the same pinhole projection the building labels use.
    /// </summary>
    private void DrawDestinationMarker(CameraPose pose, double focal, double cx, double horizonY, double width, double height)
    {
        var marker = GetDestinationMarker();
        var sighting = viewModel.IsNavigating && viewModel.DestinationId is { } id
            ? viewModel.Sightings.FirstOrDefault(s => s.Building.Id == id)
            : null;

        if (sighting is null)
        {
            marker.IsVisible = false;
            return;
        }

        var offset = sighting.OffsetFrom(pose.Heading);
        if (Math.Abs(offset) > 40)
        {
            marker.IsVisible = false;
            return;
        }

        var x = cx + focal * Math.Tan(GeoMath.ToRadians(offset));
        var elevation = Math.Atan2(12, Math.Max(sighting.DistanceMeters, 5));
        var y = Math.Clamp(horizonY - focal * Math.Tan(elevation), 120, Math.Max(120, height - 260));

        AbsoluteLayout.SetLayoutBounds(marker, new Rect(x, y, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        marker.AnchorX = 0.5;
        marker.TranslationX = -(marker.Width > 0 ? marker.Width : 60) / 2;
        marker.IsVisible = true;
    }

    private Border GetDestinationMarker()
    {
        if (destinationMarker is not null)
        {
            return destinationMarker;
        }

        destinationMarker = new Border
        {
            BackgroundColor = Color.FromArgb("#CC0D0D0D"),
            Stroke = yellow,
            StrokeThickness = 2,
            Padding = new Thickness(10, 4),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Content = new Label
            {
                Text = "▼",
                TextColor = AppColor("BcYellow"),
                FontFamily = "OpenSansSemibold",
                FontSize = 22,
            },
            InputTransparent = true,
        };

        Overlay.Children.Add(destinationMarker);
        return destinationMarker;
    }

    private (Border Card, Label Name, Label Distance) GetLabel(string id)
    {
        if (labels.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var name = new Label { TextColor = Colors.White, FontFamily = "OpenSansSemibold", FontSize = 14, MaxLines = 1 };
        var distance = new Label { TextColor = AppColor("BcYellow"), FontSize = 11 };
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#CC0D0D0D"),
            StrokeThickness = 2,
            Padding = new Thickness(10, 6),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new VerticalStackLayout { Spacing = 0, Children = { name, distance } },
            InputTransparent = true,
        };

        Overlay.Children.Add(card);
        labels[id] = (card, name, distance);
        return labels[id];
    }

    private static Color AppColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : Colors.Red;

    private static SolidColorBrush AppBrush(string key) => new(AppColor(key));
}
