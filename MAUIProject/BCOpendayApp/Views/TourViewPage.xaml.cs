using BCOpendayApp.Models;
using BCOpendayApp.Services;
using Microsoft.Maui.Media;

namespace BCOpendayApp.Views;

public partial class TourViewPage : LocationPage, IQueryAttributable
{
    private string selectedId = "";
    private CancellationTokenSource? speech;
    public TourViewPage(ILocationService locations) : base(locations) { InitializeComponent(); }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        selectedId = query.TryGetValue("id", out var value) ? value?.ToString() ?? "" : "";
        if (Catalogue is not null) Build();
    }

    protected override void Build()
    {
        var current = Locations.FirstOrDefault(stop => stop.Id == selectedId);
        Setup(current?.Name ?? "Campus viewer", "Explore the location and its information points.", "Tour", true);
        if (!AddLocationStatus()) return;
        if (current is null)
        {
            Body.Add(Text("Select a valid location from the directory."));
            Body.Add(Action("Select a location", () => Navigate("//LocationsPage")));
            return;
        }
        Body.Add(Picture(current.Image, 300));
        Body.Add(Text("PHOTO VIEW - A true 360-degree scene is not included for this location.", 13, false, Yellow));
        Body.Add(Cards(new View[]
        {
            Action("Info Points", () => Navigate("HotspotInfoOverlayPage?id=" + Uri.EscapeDataString(current.Id)), false),
            Action("Audio", async () =>
            {
                StopSpeech(); speech = new CancellationTokenSource();
                try { await TextToSpeech.SpeakAsync(current.Description, cancelToken: speech.Token); }
                catch (OperationCanceledException) { }
                catch (Exception) { await DisplayAlert("Audio unavailable", "Read the description under Info Points. Check that a speech voice is installed on your device.", "OK"); }
            }, false),
            Action("Gallery", () => Navigation.PushModalAsync(new GalleryPage(current)), false),
            Action("Floor Plan", () => Navigate("//MapPage?id=" + Uri.EscapeDataString(current.Id)), false)
        }, true));
        Body.Add(Action("Stop Audio", () => { StopSpeech(); return Task.CompletedTask; }, false));
        Body.Add(Text("Floor Plan opens the supplied campus map; individual room floor plans have not been supplied.", 14, false, Muted));
        Body.Add(Action(VisitorStore.Visited(current.Id) ? "Visited" : "Mark as visited", () =>
        {
            VisitorStore.MarkVisited(current.Id); Build(); return Task.CompletedTask;
        }));
    }

    private void StopSpeech() { speech?.Cancel(); speech?.Dispose(); speech = null; }
    protected override void OnDisappearing() { StopSpeech(); base.OnDisappearing(); }
}

public sealed class GalleryPage : ContentPage
{
    public GalleryPage(Hotspot stop)
    {
        BackgroundColor = Colors.Black;
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, true);
        var close = new Button { Text = "Close gallery", BackgroundColor = Color.FromArgb("#FA262A"),
            TextColor = Colors.White, MinimumHeightRequest = 48 };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var body = new VerticalStackLayout { Spacing = 16, Padding = 20 };
        body.Add(close);
        body.Add(new Label { Text = stop.Name, FontSize = 24, TextColor = Colors.White });
        body.Add(new Image { Source = stop.Image, Aspect = Aspect.AspectFit, HeightRequest = 340 });
        if (stop.Id == "robotics")
            body.Add(new Image { Source = "robotarm.png", Aspect = Aspect.AspectFit, HeightRequest = 340 });
        body.Add(new Label { Text = "Photos supplied with this project.", TextColor = Colors.White });
        Content = new ScrollView { Content = body };
    }
}
