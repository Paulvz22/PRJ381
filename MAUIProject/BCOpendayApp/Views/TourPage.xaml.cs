using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

public partial class TourPage : LocationPage
{
    public TourPage(ILocationService locations) : base(locations) { InitializeComponent(); }

    protected override void Build()
    {
        Setup("Your campus tour", "A self-guided journey through five campus stops.", "Tour");
        if (!AddLocationStatus()) return;
        Body.Add(Text("This version provides a photo and information tour. Camera AR, QR scanning and 360-degree scenes require a connected tour engine and matching media.", 15, false, Muted));
        int completed = Locations.Count(stop => VisitorStore.Visited(stop.Id));
        Body.Add(Text($"{completed} of {Locations.Count} completed", 23, true));
        Body.Add(new ProgressBar { Progress = Locations.Count == 0 ? 0 : (double)completed / Locations.Count,
            ProgressColor = Red });
        var next = Locations.FirstOrDefault(stop => !VisitorStore.Visited(stop.Id));
        if (next is not null)
            Body.Add(Action("Continue: " + next.Name, () => Navigate("TourViewPage?id=" + Uri.EscapeDataString(next.Id))));
        else Body.Add(Text("Tour complete! Explore your favourite stops again.", 19, true, Red));
        Body.Add(Cards(Locations.Select(StopCard)));
    }
}
