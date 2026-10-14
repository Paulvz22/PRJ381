namespace BCOpendayApp.Views;
public partial class HowItWorksPage : ResponsivePage
{
    public HowItWorksPage() { InitializeComponent(); }
    protected override void OnAppearing()
    {
        base.OnAppearing(); Setup("How it works", "Explore at your own pace.", "", true);
        foreach (var step in new[] { ("01 · Scan", "Open Start AR Tour and enter the campus location code, or choose a stop from Locations. Live camera scanning requires the scanner integration."), ("02 · Explore", "Open a location photo and use Info Points, Gallery and Floor Plan. True 360° viewing requires panoramic media and a tour engine."), ("03 · Learn", "Listen to narration, read location information and save favourite stops. Mark visited locations to record your progress.") })
        { var card = new VerticalStackLayout { Spacing = 10 }; card.Add(Text(step.Item1, 21, true, Red)); card.Add(Text(step.Item2)); Body.Add(Card(card)); }
        Body.Add(Text("Campus photos are included. Camera AR, QR scanning and 360° viewing are not available in this source package.", 15, false, Muted));
        Body.Add(Action("Start exploring", () => Navigate("//TourPage")));
    }
}
