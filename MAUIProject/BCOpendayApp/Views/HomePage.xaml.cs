using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

public partial class HomePage : LocationPage
{
    private readonly IAnalyticsService? _analyticsService;
    public HomePage(ILocationService locations) : base(locations)
    {
        InitializeComponent();

        _analyticsService = Application.Current?
            .Handler?
            .MauiContext?
            .Services
            .GetService<IAnalyticsService>();

        _analyticsService?.LogScreenView("HomePage");
    }

    protected override void Build()
    {
        Setup("BELGIUM CAMPUS", "AR TOUR - Explore. Discover. Belong.", "Home");
        var hero = new VerticalStackLayout { Spacing = 10 };
        hero.Add(Picture("campus_banner.png", 220));
        hero.Add(Text("Welcome to Belgium Campus", 22, true));
        hero.Add(Text("Scan. Explore. Experience.", 15, false, Yellow));
        Body.Add(Card(hero));
        Body.Add(Text("Explore Campus", 20, true));
        var links = new[]
        {
            ("Start AR Tour", "ar_tour.png", "QrScanPage"),
            ("Locations", "locations.png", "//LocationsPage"),
            ("Map", "map.png", "//MapPage"),
            ("How It Works", "how_it_works.png", "HowItWorksPage"),
            ("About Campus", "about.png", "AboutPage")
        };
        Body.Add(Cards(links.Select(link =>
        {
            var tile = new VerticalStackLayout { Spacing = 8 };
            tile.Add(Picture(link.Item2, 44));
            tile.Add(Action(link.Item1, () => Navigate(link.Item3), false));
            return (View)Card(tile);
        }), true));
        if (AddLocationStatus())
        {
            int completed = Locations.Count(stop => VisitorStore.Visited(stop.Id));
            Body.Add(Text($"Your journey - {completed} / {Locations.Count} visited", 16, true, Yellow));
            Body.Add(new ProgressBar { Progress = Locations.Count == 0 ? 0 : (double)completed / Locations.Count,
                ProgressColor = Yellow });
        }
        Body.Add(Action("Visitor information", () => Navigate("NotificationsPage"), false));
    }
}
