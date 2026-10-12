using BCOpendayApp.Services;
namespace BCOpendayApp.Views;
public partial class TourPage : ResponsivePage
{
    public TourPage() { InitializeComponent(); }
    protected override void OnAppearing()
    {
        base.OnAppearing(); Setup("Your campus tour", "A self-guided journey through five campus stops.", "Tour");
        Body.Add(Text("This version provides a photo and information tour. Camera AR, QR scanning and 360° scenes require a connected tour engine and matching media.", 15, false, Muted));
        Body.Add(Text($"{VisitorStore.Completed} of {CampusCatalog.Stops.Count} completed", 23, true));
        Body.Add(new ProgressBar { Progress = (double)VisitorStore.Completed / CampusCatalog.Stops.Count, ProgressColor = Red });
        var next = CampusCatalog.Stops.FirstOrDefault(s => !VisitorStore.Visited(s.Id));
        if (next != null) Body.Add(Action("Continue: " + next.Name, () => Navigate("TourViewPage?id=" + next.Id)));
        else Body.Add(Text("Tour complete! Explore your favourite stops again.", 19, true, Red));
        Body.Add(Cards(CampusCatalog.Stops.Select(StopCard)));
    }
}
