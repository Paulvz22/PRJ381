using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

public partial class MapPage : ResponsivePage
{
    private string selectedSection = "main";
    private Image campusMap = null!;
    private Label mapTitle = null!;
    private Slider zoom = null!;
    private ScrollView mapViewport = null!;
    private readonly Dictionary<string, Button> sectionButtons = new();

    public MapPage() { InitializeComponent(); }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Setup("Campus map", "Plan your next stop.", "Map");
        sectionButtons.Clear();
        mapTitle = Text("Main Campus Map", 20, true);
        Body.Add(mapTitle);

        var sections = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < 3; i++)
            sections.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        AddSectionButton(sections, "main", "Main", 0);
        AddSectionButton(sections, "north", "North", 1);
        AddSectionButton(sections, "west", "West", 2);
        Body.Add(sections);

        campusMap = new Image { Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(campusMap, "Main campus map");
        mapViewport = new ScrollView { Orientation = ScrollOrientation.Both, HeightRequest = 380, Content = campusMap };
        zoom = new Slider { Minimum = 1, Maximum = 3, Value = 1, MinimumTrackColor = Red };
        SemanticProperties.SetDescription(zoom, "Campus map zoom");
        zoom.ValueChanged += (_, _) => ResizeMap();
        mapViewport.SizeChanged += (_, _) => ResizeMap();
        Body.Add(Card(mapViewport));
        Body.Add(Text("Map zoom", 16, true));
        Body.Add(zoom);
        Body.Add(Action("Reset map zoom", () => { zoom.Value = 1; ResizeMap(); return Task.CompletedTask; }, false));
        Body.Add(Text("Use the map as a visual reference. Ask campus staff for directions; this app does not provide live positioning or verified turn-by-turn routes.", 15, false, Muted));
        Body.Add(Cards(CampusCatalog.Stops.Select(StopCard)));
        SelectSection(selectedSection);
    }

    private void AddSectionButton(Grid grid, string section, string caption, int column)
    {
        var button = new Button
        {
            Text = caption, TextColor = Colors.White, CornerRadius = 12,
            MinimumHeightRequest = 48, Padding = new Thickness(4, 10), FontSize = 14
        };
        button.Clicked += (_, _) => SelectSection(section);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        sectionButtons.Add(section, button);
    }

    private void SelectSection(string section)
    {
        // Explicit resource names keep the switcher tied to the three bundled maps.
        string source;
        string title;
        switch (section)
        {
            case "north": source = "campus_map_north.png"; title = "North Campus Map"; break;
            case "west": source = "campus_map_west.png"; title = "West Campus Map"; break;
            default: section = "main"; source = "campus_map_main.png"; title = "Main Campus Map"; break;
        }
        selectedSection = section;
        campusMap.Source = ImageSource.FromFile(source);
        mapTitle.Text = title;
        SemanticProperties.SetDescription(campusMap, title);
        foreach (var pair in sectionButtons)
        {
            pair.Value.BackgroundColor = pair.Key == section ? Red : Color.FromArgb("#24262C");
            SemanticProperties.SetDescription(pair.Value, pair.Value.Text + (pair.Key == section ? ", selected" : ""));
        }
        zoom.Value = 1;
        ResizeMap();
    }

    private void ResizeMap()
    {
        // Fit the viewport at 1x; enlarging the canvas makes zoomed maps scrollable.
        double width = mapViewport.Width;
        if (!double.IsFinite(width) || width <= 0) width = 320;
        campusMap.WidthRequest = width * zoom.Value;
        campusMap.HeightRequest = 360 * zoom.Value;
    }
}
