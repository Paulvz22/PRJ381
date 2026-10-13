using BCOpendayApp.Services;
using Microsoft.Maui.Controls.Shapes;
namespace BCOpendayApp.Views;
public partial class QrScanPage : LocationPage
{
    public QrScanPage(ILocationService locations) : base(locations) { InitializeComponent(); }
    protected override void Build()
    {
        Setup("Start AR Tour", "Choose a campus stop to begin exploring.", "Tour", true);
        if (!AddLocationStatus()) return;
        var frame = new Grid { HeightRequest = 270 };
        frame.Add(new Image { Source = "campus_banner.png", Aspect = Aspect.AspectFill, Opacity = 0.65 });
        frame.Add(new Border { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, WidthRequest = 170, HeightRequest = 170, Stroke = Red, StrokeThickness = 3, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = new Image { Source = "qr.png", Aspect = Aspect.AspectFit, Margin = 24 } });
        Body.Add(frame);
        Body.Add(Text("Camera scanning is not enabled in this build. Enter a location code below or choose from the directory.", 15, false, Muted));
        var code = new Entry { Placeholder = "e.g. BC-ROBOTICS", TextColor = Colors.White, PlaceholderColor = Muted, MaxLength = 40, ReturnType = ReturnType.Go }; Body.Add(code);
        async Task OpenCode()
        {
            var id = (code.Text ?? "").Trim().ToLowerInvariant(); if (id.StartsWith("bc-")) id = id[3..];
            var stop = Locations.FirstOrDefault(location => location.Id == id);
            if (stop == null) { await DisplayAlert("Unknown location code", "Use BC-ROBOTICS, BC-MANUFACTURING, BC-LECTURE, BC-LIBRARY or BC-LOUNGE.", "OK"); return; }
            await Navigate("TourViewPage?id=" + stop.Id);
        }
        Body.Add(Action("Enter Code Manually", OpenCode));
        Body.Add(Action("Choose from locations", () => Navigate("//LocationsPage"), false));
        Body.Add(Action("How scanning works", () => Navigate("HowItWorksPage"), false));
    }
}
