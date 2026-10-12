using BCOpendayApp.Services;
using BCOpendayApp.Views.Components;
using Microsoft.Maui.Controls.Shapes;
namespace BCOpendayApp.Views;

public class ResponsivePage : ContentPage
{
    protected static readonly Color Red = Color.FromArgb("#FA262A");
    protected static readonly Color Yellow = Color.FromArgb("#FFD400");
    protected static readonly Color Muted = Color.FromArgb("#B7B7B7");
    protected VerticalStackLayout Body = new() { Spacing = 18 };
    private readonly List<Grid> adaptiveGrids = new();
    protected ResponsivePage()
    {
        BackgroundColor = Color.FromArgb("#08090B");
        Shell.SetNavBarIsVisible(this, false);
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, true);
    }
    protected void Setup(string title, string subtitle, string tab = "", bool back = false)
    {
        adaptiveGrids.Clear();
        Title = title;
        Body = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(20, 18, 20, 28), MaximumWidthRequest = 1100, HorizontalOptions = LayoutOptions.Center };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Add(Text("BELGIUM CAMPUS / OPEN DAY", 12, true, Red));
        Body.Add(header);
        if (back) Body.Add(Action("‹ Back", () => Navigate(".."), false));
        Body.Add(Text(title, 30, true)); Body.Add(Text(subtitle, 15, false, Muted));
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) } };
        var scroll = new ScrollView { Content = Body };
        root.Add(scroll); root.Add(new BottomNavBar { ActiveTab = tab }, 0, 1); Content = root;
        scroll.SizeChanged += (_, _) => { Body.WidthRequest = Math.Max(0, Math.Min(1100, scroll.Width)); Reflow(); };
    }
    protected Label Text(string value, double size = 16, bool bold = false, Color? color = null) => new()
    {
        Text = value, FontSize = size * (VisitorStore.LargeText ? 1.2 : 1), TextColor = color ?? Colors.White,
        FontFamily = bold ? "OpenSansSemibold" : "OpenSansRegular", LineBreakMode = LineBreakMode.WordWrap
    };
    protected Button Action(string title, Func<Task> action, bool primary = true)
    {
        var b = new Button { Text = title, BackgroundColor = primary ? Red : Color.FromArgb("#24262C"), TextColor = Colors.White, MinimumHeightRequest = 48, CornerRadius = 12, Padding = new Thickness(14, 12), FontSize = VisitorStore.LargeText ? 18 : 14 };
        SemanticProperties.SetDescription(b, title);
        b.Clicked += async (_, _) => { if (!b.IsEnabled) return; b.IsEnabled = false; try { await action(); } catch (Exception) { await DisplayAlert("Unable to complete action", "Please try again. If this continues, restart the app.", "OK"); } finally { b.IsEnabled = true; } };
        return b;
    }
    protected static Task Navigate(string route) => Shell.Current.GoToAsync(route);
    protected Border Card(View child) => new() { Content = child, Padding = 18, BackgroundColor = Color.FromArgb("#15171C"), Stroke = Color.FromArgb("#30333B"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 18 } };
    protected View Picture(string source, double height = 210) => new Image { Source = source, Aspect = Aspect.AspectFit, HeightRequest = height };
    protected Grid Cards(IEnumerable<View> views, bool tiles = false)
    {
        var grid = new Grid { RowSpacing = 14, ColumnSpacing = 14, ClassId = tiles ? "menuTiles" : "cards" };
        foreach (var view in views) grid.Children.Add(view);
        adaptiveGrids.Add(grid); Reflow(); return grid;
    }
    protected View LocationRow(CampusStop stop)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(76)), new(GridLength.Star) }, ColumnSpacing = 12 };
        row.Add(new Image { Source = stop.Image, HeightRequest = 78, Aspect = Aspect.AspectFill });
        var details = new VerticalStackLayout { Spacing = 6 }; details.Add(Text(stop.Name, 18, true));
        details.Add(Action("Explore ›", () => Navigate("TourViewPage?id=" + stop.Id), false)); row.Add(details, 1); return Card(row);
    }
    private void Reflow()
    {
        var width = Body.WidthRequest > 0 ? Body.WidthRequest : Width;
        int defaultColumns = width >= 960 ? 3 : width >= 620 ? 2 : 1;
        foreach (var g in adaptiveGrids)
        {
            int columns = g.ClassId == "menuTiles" ? (width >= 620 ? 3 : 2) : defaultColumns;
            if (g.ColumnDefinitions.Count != columns)
            {
                g.ColumnDefinitions.Clear(); g.RowDefinitions.Clear();
                for (int c = 0; c < columns; c++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                for (int r = 0; r < (g.Children.Count + columns - 1) / columns; r++) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            for (int i = 0; i < g.Children.Count; i++) { Grid.SetColumn((BindableObject)g.Children[i], i % columns); Grid.SetRow((BindableObject)g.Children[i], i / columns); }
        }
    }
    protected View StopCard(CampusStop stop)
    {
        var content = new VerticalStackLayout { Spacing = 12 };
        content.Add(Picture(stop.Image, 160)); content.Add(Text(stop.Name, 21, true)); content.Add(Text(stop.Summary, 15, false, Muted));
        content.Add(Text(VisitorStore.Visited(stop.Id) ? "✓ Visited" : "Ready to explore", 13, false, Red));
        content.Add(Action("Explore " + stop.Name, () => Navigate("TourViewPage?id=" + stop.Id)));
        return Card(content);
    }
}
