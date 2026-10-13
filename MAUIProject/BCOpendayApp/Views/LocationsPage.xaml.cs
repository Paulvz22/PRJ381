using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

public partial class LocationsPage : LocationPage
{
    private string query = "";
    public LocationsPage(ILocationService locations) : base(locations) { InitializeComponent(); }

    protected override void Build()
    {
        Setup("Campus locations", "Explore learning spaces and student life.", "Tour");
        if (!AddLocationStatus()) return;
        var search = new SearchBar { Placeholder = "Search locations", Text = query,
            TextColor = Colors.White, PlaceholderColor = Muted };
        var results = new ContentView();
        void Filter()
        {
            var matches = Locations.Where(stop => (stop.Name + " " + stop.Summary + " " + stop.Highlight)
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            results.Content = matches.Count == 0
                ? Text("No locations match your search.", 16, false, Muted) : LocationRows(matches);
        }
        search.TextChanged += (_, args) => { query = args.NewTextValue ?? ""; Filter(); };
        Body.Add(search);
        Body.Add(results);
        Filter();
    }

    private View LocationRows(IEnumerable<Hotspot> stops)
    {
        var list = new VerticalStackLayout { Spacing = 12 };
        foreach (var stop in stops) list.Add(LocationRow(stop));
        return list;
    }
}
