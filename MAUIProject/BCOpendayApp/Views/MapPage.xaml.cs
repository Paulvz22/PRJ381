using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

public partial class MapPage : LocationPage, IQueryAttributable
{
    private string selectedSection = "main";
    private string requestedId = "";
    private Image campusMap = null!;
    private AbsoluteLayout canvas = null!;
    private Label mapTitle = null!;
    private Label mapStatus = null!;
    private Slider zoom = null!;
    private ScrollView mapViewport = null!;
    private readonly Dictionary<string, Button> sectionButtons = new();
    private readonly List<(Hotspot Stop, Button Button)> pins = new();

    public MapPage(ILocationService locations) : base(locations) { InitializeComponent(); }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        requestedId = query.TryGetValue("id", out var value) ? value?.ToString() ?? "" : "";
        if (Catalogue is not null) Build();
    }

    protected override void Build()
    {
        Setup("Campus map", "Plan your next stop.", "Map");
        if (!AddLocationStatus()) return;
        var requested = Locations.FirstOrDefault(location => location.Id == requestedId);
        if (requested?.HasMapPosition == true) selectedSection = requested.MapSection;
        sectionButtons.Clear();
        pins.Clear();
        mapTitle = Text(CampusCatalog.Map(selectedSection).Title, 20, true);
        Body.Add(mapTitle);
        var sections = new Grid { ColumnSpacing = 8 };
        for (int index = 0; index < CampusCatalog.Maps.Count; index++)
        {
            sections.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            var section = CampusCatalog.Maps[index];
            AddSectionButton(sections, section.Id, char.ToUpperInvariant(section.Id[0]) + section.Id[1..], index);
        }
        Body.Add(sections);
        campusMap = new Image { Aspect = Aspect.Fill,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        canvas = new AbsoluteLayout { HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start };
        mapViewport = new ScrollView { Orientation = ScrollOrientation.Both,
            HeightRequest = 420, Content = canvas };
        zoom = new Slider { Minimum = 1, Maximum = 3, Value = 1, MinimumTrackColor = Red };
        SemanticProperties.SetDescription(zoom, "Campus map zoom");
        zoom.ValueChanged += (_, _) => ResizeMap();
        mapViewport.SizeChanged += (_, _) => ResizeMap();
        Body.Add(Card(mapViewport));
        mapStatus = Text("", 14, false, Muted);
        Body.Add(mapStatus);
        Body.Add(Text("Map zoom", 16, true));
        Body.Add(zoom);
        Body.Add(Action("Reset map zoom", async () =>
        {
            zoom.Value = 1; ResizeMap(); await mapViewport.ScrollToAsync(0, 0, false);
        }, false));
        Body.Add(Text("Tap a numbered reference pin for location information. Pins follow the supplied map image, not live GPS or turn-by-turn directions.", 15, false, Muted));
        if (requested is not null && !requested.HasMapPosition)
            Body.Add(Text(requested.Name + ": " + requested.PositionNote, 15, false, Yellow));
        int unplaced = Locations.Count(location => !location.HasMapPosition);
        if (unplaced > 0)
            Body.Add(Text($"{unplaced} tour stops have no supplied map position. They remain available below; ask campus staff for directions.", 15, false, Yellow));
        Body.Add(Text("All tour locations", 20, true));
        Body.Add(Cards(Locations.Select(StopCard)));
        SelectSection(selectedSection);
    }

    private void AddSectionButton(Grid grid, string section, string caption, int column)
    {
        var button = new Button { Text = caption, TextColor = Colors.White, CornerRadius = 12,
            MinimumHeightRequest = 48, Padding = new Thickness(4, 10), FontSize = 14 };
        button.Clicked += (_, _) => SelectSection(section);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        sectionButtons.Add(section, button);
    }

    private void SelectSection(string section)
    {
        var map = CampusCatalog.Map(section);
        selectedSection = map.Id;
        campusMap.Source = ImageSource.FromFile(map.Image);
        mapTitle.Text = map.Title;
        SemanticProperties.SetDescription(campusMap, map.Title);
        canvas.Children.Clear();
        canvas.Children.Add(campusMap);
        pins.Clear();
        foreach (var stop in Locations.Where(location => location.HasMapPosition && location.MapSection == selectedSection))
        {
            var pin = Action(stop.SortOrder.ToString(), () => Navigate("HotspotInfoOverlayPage?id=" + Uri.EscapeDataString(stop.Id)));
            pin.WidthRequest = 44; pin.HeightRequest = 44; pin.MinimumHeightRequest = 44;
            pin.MinimumWidthRequest = 44; pin.Padding = 0; pin.CornerRadius = 22;
            pin.BorderWidth = 2; pin.BorderColor = stop.Id == requestedId ? Yellow : Colors.White;
            SemanticProperties.SetDescription(pin, stop.Name + ", map reference point. " + stop.PositionNote);
            canvas.Children.Add(pin);
            pins.Add((stop, pin));
        }
        mapStatus.Text = pins.Count == 0 ? "No tour-stop positions are assigned to this map yet."
            : string.Join(" | ", pins.Select(pin => $"{pin.Stop.SortOrder}: {pin.Stop.Name}")) + " - reference positions only.";
        foreach (var pair in sectionButtons)
        {
            pair.Value.BackgroundColor = pair.Key == selectedSection ? Red : Color.FromArgb("#24262C");
            SemanticProperties.SetDescription(pair.Value, pair.Value.Text + (pair.Key == selectedSection ? ", selected" : ""));
        }
        zoom.Value = 1;
        ResizeMap();
    }

    private void ResizeMap()
    {
        if (canvas is null || mapViewport is null || zoom is null) return;
        var map = CampusCatalog.Map(selectedSection);
        double viewportWidth = mapViewport.Width;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0) viewportWidth = 320;
        double width = viewportWidth * zoom.Value;
        double height = width * map.PixelHeight / map.PixelWidth;
        canvas.WidthRequest = width; canvas.HeightRequest = height;
        campusMap.WidthRequest = width; campusMap.HeightRequest = height;
        AbsoluteLayout.SetLayoutBounds(campusMap, new Rect(0, 0, width, height));
        foreach (var (stop, button) in pins)
        {
            var (left, top) = MapGeometry.PinPosition(stop.X!.Value, stop.Y!.Value, width, height);
            AbsoluteLayout.SetLayoutBounds(button, new Rect(left, top, 44, 44));
        }
    }
}
