using BCOpendayApp.Services;
namespace BCOpendayApp.Views;
public partial class LocationsPage : ResponsivePage
{
    private string query = "";
    public LocationsPage() { InitializeComponent(); }
    protected override void OnAppearing() { base.OnAppearing(); Build(); }
    private View LocationRows(IEnumerable<CampusStop> stops) { var list = new VerticalStackLayout { Spacing = 12 }; foreach (var stop in stops) list.Add(LocationRow(stop)); return list; }
    private void Build()
    {
        Setup("Campus locations", "Explore learning spaces and student life.", "Tour");
        var search = new SearchBar { Placeholder = "Search locations", Text = query, TextColor = Colors.White, PlaceholderColor = Muted };
        var results = new ContentView();
        void Filter() { var matches = CampusCatalog.Stops.Where(s => (s.Name + " " + s.Summary + " " + s.Highlight).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList(); results.Content = matches.Count == 0 ? Text("No locations match your search.", 16, false, Muted) : LocationRows(matches); }
        search.TextChanged += (_, e) => { query = e.NewTextValue ?? ""; Filter(); };
        Body.Add(search); Body.Add(results); Filter();
    }
}
