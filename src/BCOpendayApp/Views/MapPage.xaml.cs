using System.ComponentModel;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Views;

/// <summary>
/// Lays out the campus map: the image fills a canvas whose size follows the zoom, inside a two-way ScrollView.
/// The numbered pins were removed (the map image already shows the layout); to restore them apply
/// docs/backups/restore-map-pins.patch.
/// </summary>
public partial class MapPage : BasePage
{
    private const double FallbackViewportWidth = 320;

    private readonly MapViewModel viewModel;

    public MapPage(MapViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;

        MapViewport.SizeChanged += (_, _) => ResizeMap();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ShowMap();
    }

    /// <summary>Map is a tab: Back returns to Home instead of closing the app.</summary>
    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(() => _ = Shell.Current.GoToAsync("//" + Routes.Home));
        return true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapViewModel.SelectedMap):
                ShowMap();
                break;
            case nameof(MapViewModel.Zoom):
                ResizeMap();
                break;
        }
    }

    private void ShowMap()
    {
        var map = viewModel.SelectedMap;
        MapImage.Source = ImageSource.FromFile(map.Image);
        SemanticProperties.SetDescription(MapImage, map.Title);
        ResizeMap();
    }

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
    }
}
