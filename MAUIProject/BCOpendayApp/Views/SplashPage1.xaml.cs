namespace BCOpendayApp.Views;
public partial class SplashPage1 : ResponsivePage
{
    private CancellationTokenSource? delay;
    private bool entered;
    public SplashPage1()
    {
        InitializeComponent();
        var body = new VerticalStackLayout { Spacing = 18, Padding = 28, VerticalOptions = LayoutOptions.Center };
        body.Add(Picture("belgium_logo.png", 190));
        var title = Text("BELGIUM CAMPUS", 28, true); title.HorizontalTextAlignment = TextAlignment.Center; body.Add(title);
        var subtitle = Text("AR CAMPUS TOUR", 23, true, Red); subtitle.HorizontalTextAlignment = TextAlignment.Center; body.Add(subtitle);
        var line = Text("Explore. Discover. Belong.", 16); line.HorizontalTextAlignment = TextAlignment.Center; body.Add(line);
        body.Add(new ActivityIndicator { IsRunning = true, Color = Yellow });
        body.Add(Action("Explore campus", Enter)); Content = new ScrollView { Content = body };
    }
    private async Task Enter() { if (entered) return; entered = true; delay?.Cancel(); await Navigate("//HomePage"); }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); delay = new CancellationTokenSource();
        try { await Task.Delay(5000, delay.Token); await Enter(); } catch (OperationCanceledException) { }
    }
    protected override void OnDisappearing() { delay?.Cancel(); base.OnDisappearing(); }
}
