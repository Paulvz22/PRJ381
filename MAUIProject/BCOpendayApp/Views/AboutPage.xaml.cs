namespace BCOpendayApp.Views;
public partial class AboutPage : ResponsivePage
{
    public AboutPage() { InitializeComponent(); }
    protected override void OnAppearing()
    {
        base.OnAppearing(); Setup("About the campus experience", "Learn more during your open-day visit.", "", true);
        Body.Add(Picture("belgium_logo.png", 180));
        Body.Add(Card(Text("Welcome to Belgium Campus ITversity. This visitor guide introduces campus learning spaces and student life through a self-guided photo tour.")));
        Body.Add(Text("Make your visit count", 23, true));
        Body.Add(Text("Ask campus staff about programmes, entry requirements, fees, student support and the application process. Current official information should be confirmed with the admissions team."));
        Body.Add(Action("Explore locations", () => Navigate("//LocationsPage")));
        Body.Add(Action("How this guide works", () => Navigate("HowItWorksPage"), false));
    }
}
