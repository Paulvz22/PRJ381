using BCOpendayApp.Services;
using Microsoft.Maui.Media;

namespace BCOpendayApp.Views;

public partial class HotspotInfoOverlayPage : LocationPage, IQueryAttributable
{
    private string selectedId = "";
    private CancellationTokenSource? speech;
    public HotspotInfoOverlayPage(ILocationService locations) : base(locations) { InitializeComponent(); }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        selectedId = query.TryGetValue("id", out var value) ? value?.ToString() ?? "" : "";
        if (Catalogue is not null) Build();
    }

    protected override void Build()
    {
        var current = Locations.FirstOrDefault(stop => stop.Id == selectedId);
        Setup(current?.Name ?? "Location information", "Discover this campus space.", "Tour", true);
        if (!AddLocationStatus()) return;
        if (current is null)
        {
            Body.Add(Text("Location not found. Select a location from the campus directory."));
            Body.Add(Action("View locations", () => Navigate("//LocationsPage")));
            return;
        }
        Body.Add(Picture(current.Id == "robotics" ? "robotarm.png" : current.Image, 280));
        Body.Add(Text(current.Highlight, 16, true, Red));
        Body.Add(Text(current.Summary, 22, true));
        var info = Card(Text(current.Description));
        info.Stroke = Yellow;
        Body.Add(info);
        Body.Add(Text(current.PositionNote, 14, false, Muted));
        Body.Add(Action(VisitorStore.Visited(current.Id) ? "Visited - mark again" : "Mark as visited", () =>
        {
            VisitorStore.MarkVisited(current.Id); Build(); return Task.CompletedTask;
        }));
        Body.Add(Action(VisitorStore.Saved(current.Id) ? "Remove from favourites" : "Save to favourites", () =>
        {
            VisitorStore.Save(current.Id, !VisitorStore.Saved(current.Id)); Build(); return Task.CompletedTask;
        }, false));
        Body.Add(Action("Listen to description", async () =>
        {
            StopSpeech();
            speech = new CancellationTokenSource();
            try { await TextToSpeech.SpeakAsync(current.Name + ". " + current.Description, cancelToken: speech.Token); }
            catch (OperationCanceledException) { }
            catch (Exception) { await DisplayAlert("Audio unavailable", "Speech is unavailable on this device. The complete description is shown on this page.", "OK"); }
        }, false));
        Body.Add(Action("Stop audio", () => { StopSpeech(); return Task.CompletedTask; }, false));
        int index = Locations.ToList().FindIndex(stop => stop.Id == current.Id);
        if (index >= 0 && index + 1 < Locations.Count)
        {
            var next = Locations[index + 1];
            Body.Add(Action("Next: " + next.Name, () =>
            {
                StopSpeech(); selectedId = next.Id; Build(); return Task.CompletedTask;
            }));
        }
        Body.Add(Action("View campus map", () => Navigate("//MapPage?id=" + Uri.EscapeDataString(current.Id)), false));
    }

    private void StopSpeech() { speech?.Cancel(); speech?.Dispose(); speech = null; }
    protected override void OnDisappearing() { StopSpeech(); base.OnDisappearing(); }
}
