using System.ComponentModel;
using BCOpendayApp.Models;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

/// <summary>
/// Lays out the campus map: the image fills a canvas whose size follows the zoom, and each stop that has a
/// map position gets a numbered pin placed with <see cref="MapGeometry"/> (fractions of the image size).
/// </summary>
public partial class MapPage : BasePage
{
    private const double PinSize = 44;
    private const double FallbackViewportWidth = 320;

    private readonly MapViewModel viewModel;
    private readonly List<(Hotspot Stop, Button Pin)> pins = new();

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;

        MapViewport.SizeChanged += (_, _) => ResizeMap();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RebuildPins();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapViewModel.SelectedMap):
            case nameof(MapViewModel.PinnedStops):
            case nameof(MapViewModel.RequestedStopId):
                RebuildPins();
                break;
            case nameof(MapViewModel.Zoom):
                ResizeMap();
                break;
        }
    }

    private void RebuildPins()
    {
        foreach (var (_, pin) in pins)
        {
            MapCanvas.Children.Remove(pin);
        }

        pins.Clear();

        var map = viewModel.SelectedMap;
        MapImage.Source = ImageSource.FromFile(map.Image);
        SemanticProperties.SetDescription(MapImage, map.Title);

        foreach (var stop in viewModel.PinnedStops)
        {
            var requested = stop.Id == viewModel.RequestedStopId;
            var pin = new Button
            {
                Text = stop.SortOrder.ToString(),
                TextColor = Colors.White,
                BackgroundColor = AppColor("BcRed", Colors.Red),
                WidthRequest = PinSize,
                HeightRequest = PinSize,
                MinimumWidthRequest = PinSize,
                MinimumHeightRequest = PinSize,
                Padding = 0,
                CornerRadius = (int)(PinSize / 2),
                BorderWidth = 2,
                BorderColor = requested ? AppColor("BcYellow", Colors.Yellow) : Colors.White,
                Command = viewModel.OpenInfoCommand,
                CommandParameter = stop,
            };
            SemanticProperties.SetDescription(pin, $"{stop.Name}, map reference point. {stop.PositionNote}");

            MapCanvas.Children.Add(pin);
            pins.Add((stop, pin));
        }

        ResizeMap();
    }

    private static Color AppColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : fallback;

    /// <summary>Enlarging the canvas inside the two-way ScrollView is what makes a zoomed map pannable.</summary>
    private void ResizeMap()
    {
        var map = viewModel.SelectedMap;
        var viewportWidth = MapViewport.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            viewportWidth = FallbackViewportWidth;
        }

        var width = viewportWidth * viewModel.Zoom;
        var height = width * map.PixelHeight / map.PixelWidth;

        MapCanvas.WidthRequest = width;
        MapCanvas.HeightRequest = height;
        MapImage.WidthRequest = width;
        MapImage.HeightRequest = height;
        AbsoluteLayout.SetLayoutBounds(MapImage, new Rect(0, 0, width, height));

        foreach (var (stop, pin) in pins)
        {
            var (left, top) = MapGeometry.PinPosition(stop.X!.Value, stop.Y!.Value, width, height, PinSize);
            AbsoluteLayout.SetLayoutBounds(pin, new Rect(left, top, PinSize, PinSize));
        }
    }
}
