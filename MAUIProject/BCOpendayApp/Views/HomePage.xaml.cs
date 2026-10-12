using BCOpendayApp.Services;
namespace BCOpendayApp.Views;
public partial class HomePage : ResponsivePage
{
    public HomePage() { InitializeComponent(); }
    protected override void OnAppearing()
    {
        base.OnAppearing(); Setup("BELGIUM CAMPUS", "AR TOUR · Explore. Discover. Belong.", "Home");
        var hero = new VerticalStackLayout { Spacing = 10 };
        hero.Add(Picture("campus_banner.png", 220)); hero.Add(Text("Welcome to Belgium Campus", 22, true));
        hero.Add(Text("Scan. Explore. Experience.", 15, false, Yellow)); Body.Add(Card(hero));
        Body.Add(Text("Explore Campus", 20, true));
        var links = new[] { ("Start AR Tour", "ar_tour.png", "QrScanPage"), ("Locations", "locations.png", "//LocationsPage"), ("Map", "map.png", "//MapPage"), ("How It Works", "how_it_works.png", "HowItWorksPage"), ("About Campus", "about.png", "AboutPage") };
        Body.Add(Cards(links.Select(l => { var s = new VerticalStackLayout { Spacing = 8 }; s.Add(Picture(l.Item2, 44)); s.Add(Action(l.Item1, () => Navigate(l.Item3), false)); return (View)Card(s); }), true));
        Body.Add(Text($"Your journey · {VisitorStore.Completed} / {CampusCatalog.Stops.Count} visited", 16, true, Yellow));
        Body.Add(new ProgressBar { Progress = (double)VisitorStore.Completed / CampusCatalog.Stops.Count, ProgressColor = Yellow });
        Body.Add(Action("Visitor information", () => Navigate("NotificationsPage"), false));
    }
}
