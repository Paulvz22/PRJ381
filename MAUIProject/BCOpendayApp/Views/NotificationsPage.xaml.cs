namespace BCOpendayApp.Views;
public partial class NotificationsPage : ResponsivePage
{
    public NotificationsPage() { InitializeComponent(); }
    protected override void OnAppearing()
    {
        base.OnAppearing(); Setup("Visitor information", "Helpful reminders for your campus visit.", "", true);
        foreach (var item in new[] { ("Welcome", "Start at reception and ask staff about today's programme. This page contains general guidance, not live event notices."), ("Explore safely", "Follow your guide's instructions. Ask permission before entering restricted areas or touching laboratory equipment."), ("Need assistance?", "Speak to a campus staff member or return to reception for help and accessibility support."), ("Your progress stays with you", "Favourites and visited stops are retained when you close and reopen the app on this device.") })
        { var s = new VerticalStackLayout { Spacing = 10 }; s.Add(Text(item.Item1, 21, true)); s.Add(Text(item.Item2)); Body.Add(Card(s)); }
    }
}
