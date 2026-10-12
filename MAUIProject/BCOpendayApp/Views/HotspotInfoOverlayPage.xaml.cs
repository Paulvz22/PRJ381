using BCOpendayApp.Services;
using Microsoft.Maui.Media;
namespace BCOpendayApp.Views;
public partial class HotspotInfoOverlayPage : ResponsivePage, IQueryAttributable
{
    private CampusStop? stop;
    private CancellationTokenSource? speech;
    public HotspotInfoOverlayPage() { InitializeComponent(); }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        stop = query.TryGetValue("id", out var value) ? CampusCatalog.Find(value?.ToString() ?? "") : null;
        Build();
    }
    protected override void OnAppearing() { base.OnAppearing(); Build(); }
    private void Build()
    {
        Setup(stop?.Name ?? "Location not found", "Discover this campus space.", "Tour", true);
        if (stop == null) { Body.Add(Text("Select a location from the campus directory.")); Body.Add(Action("View locations", () => Navigate("//LocationsPage"))); return; }
        var current = stop;
        Body.Add(Picture(current.Id == "robotics" ? "robotarm.png" : current.Image, 280)); Body.Add(Text(current.Highlight, 16, true, Red)); Body.Add(Text(current.Summary, 22, true)); var info = Card(Text(current.Details)); info.Stroke = Yellow; Body.Add(info);
        Body.Add(Action(VisitorStore.Visited(current.Id) ? "✓ Visited — mark again" : "Mark as visited", () => { VisitorStore.MarkVisited(current.Id); Build(); return Task.CompletedTask; }));
        Body.Add(Action(VisitorStore.Saved(current.Id) ? "Remove from favourites" : "Save to favourites", () => { VisitorStore.Save(current.Id, !VisitorStore.Saved(current.Id)); Build(); return Task.CompletedTask; }, false));
        Body.Add(Action("Listen to description", async () =>
        {
            speech?.Cancel(); speech?.Dispose(); speech = new CancellationTokenSource();
            try { await TextToSpeech.SpeakAsync(current.Name + ". " + current.Details, cancelToken: speech.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { await DisplayAlert("Audio unavailable", "Speech is unavailable on this device. The complete description is shown on this page.", "OK"); }
        }, false));
        Body.Add(Action("Stop audio", () => { speech?.Cancel(); return Task.CompletedTask; }, false));
        int index = CampusCatalog.Stops.ToList().FindIndex(s => s.Id == current.Id);
        if (index + 1 < CampusCatalog.Stops.Count) Body.Add(Action("Next: " + CampusCatalog.Stops[index + 1].Name, () => ShowNext(CampusCatalog.Stops[index + 1])));
        Body.Add(Action("View campus map", () => Navigate("//MapPage"), false));
    }
    private Task ShowNext(CampusStop next) { speech?.Cancel(); stop = next; Build(); return Task.CompletedTask; }
    protected override void OnDisappearing() { speech?.Cancel(); base.OnDisappearing(); }
}
