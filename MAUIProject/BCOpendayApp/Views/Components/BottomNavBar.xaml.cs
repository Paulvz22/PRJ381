namespace BCOpendayApp.Views.Components;
public partial class BottomNavBar : ContentView
{
    private bool navigating;
    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(nameof(ActiveTab), typeof(string), typeof(BottomNavBar), "", propertyChanged: (b, _, _) => ((BottomNavBar)b).Refresh());
    public string ActiveTab { get => (string)GetValue(ActiveTabProperty); set => SetValue(ActiveTabProperty, value); }
    public BottomNavBar() { InitializeComponent(); Refresh(); }
    private void Refresh()
    {
        if (HomeButton == null) return;
        foreach (var pair in new[] { (HomeButton, "Home"), (TourButton, "Tour"), (MapButton, "Map") })
        { pair.Item1.BackgroundColor = Color.FromArgb(pair.Item2 == ActiveTab ? "#FA262A" : "#24262C"); SemanticProperties.SetDescription(pair.Item1, pair.Item2 + (pair.Item2 == ActiveTab ? ", selected" : "")); }
    }
    private async Task Go(string route)
    {
        if (navigating) return; navigating = true;
        try { await Shell.Current.GoToAsync(route); }
        catch (Exception) { await Shell.Current.DisplayAlert("Navigation unavailable", "Please try again.", "OK"); }
        finally { navigating = false; }
    }
    private async void OnHomeClicked(object? sender, EventArgs e) => await Go("//HomePage");
    private async void OnTourClicked(object? sender, EventArgs e) => await Go("//TourPage");
    private async void OnMapClicked(object? sender, EventArgs e) => await Go("//MapPage");
}
