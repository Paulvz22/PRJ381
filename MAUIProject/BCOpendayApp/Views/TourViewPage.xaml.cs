using BCOpendayApp.Services;
using Microsoft.Maui.Media;
namespace BCOpendayApp.Views;
public partial class TourViewPage : ResponsivePage, IQueryAttributable
{
    private CampusStop? stop;
    private CancellationTokenSource? speech;
    public TourViewPage() { InitializeComponent(); }
    public void ApplyQueryAttributes(IDictionary<string, object> query) { stop = query.TryGetValue("id", out var id) ? CampusCatalog.Find(id?.ToString() ?? "") : null; Build(); }
    protected override void OnAppearing() { base.OnAppearing(); Build(); }
    private void Build()
    {
        Setup(stop?.Name ?? "Campus viewer", "Explore the location and its information points.", "Tour", true);
        if (stop == null) { Body.Add(Action("Select a location", () => Navigate("//LocationsPage"))); return; }
        var current = stop;
        Body.Add(Picture(current.Image, 300));
        Body.Add(Text("PHOTO VIEW · A true 360° scene is not included for this location.", 13, false, Yellow));
        Body.Add(Cards(new View[] {
            Action("Info Points", () => Navigate("HotspotInfoOverlayPage?id=" + current.Id), false),
            Action("Audio", async () => {
                speech?.Cancel(); speech = new CancellationTokenSource();
                try { await TextToSpeech.SpeakAsync(current.Details, cancelToken: speech.Token); }
                catch (OperationCanceledException) { }
                catch (Exception) { await DisplayAlert("Audio unavailable", "Read the description under Info Points. Check that a speech voice is installed on your device.", "OK"); }
            }, false),
            Action("Gallery", () => Navigation.PushModalAsync(new GalleryPage(current)), false),
            Action("Floor Plan", () => Navigate("//MapPage"), false)
        }, true));
        Body.Add(Action("Stop Audio", () => { speech?.Cancel(); return Task.CompletedTask; }, false));
        Body.Add(Text("Floor Plan opens the supplied campus map; individual room floor plans have not been supplied.", 14, false, Muted));
        Body.Add(Action(VisitorStore.Visited(current.Id) ? "✓ Visited" : "Mark as visited", () => { VisitorStore.MarkVisited(current.Id); Build(); return Task.CompletedTask; }));
    }
    protected override void OnDisappearing() { speech?.Cancel(); base.OnDisappearing(); }
}
public sealed class GalleryPage : ContentPage
{
    public GalleryPage(CampusStop stop)
    {
        BackgroundColor = Colors.Black;
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, true);
        var close = new Button { Text = "Close gallery", BackgroundColor = Color.FromArgb("#FA262A"), TextColor = Colors.White, MinimumHeightRequest = 48 };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var body = new VerticalStackLayout { Spacing = 16, Padding = 20 };
        body.Add(close); body.Add(new Label { Text = stop.Name, FontSize = 24, TextColor = Colors.White });
        body.Add(new Image { Source = stop.Image, Aspect = Aspect.AspectFit, HeightRequest = 340 });
        if (stop.Id == "robotics") body.Add(new Image { Source = "robotarm.png", Aspect = Aspect.AspectFit, HeightRequest = 340 });
        body.Add(new Label { Text = "Photos supplied with this project.", TextColor = Colors.White }); Content = new ScrollView { Content = body };
    }
}
