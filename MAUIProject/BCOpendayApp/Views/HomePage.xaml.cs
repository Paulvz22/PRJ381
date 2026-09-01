namespace BCOpendayApp.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void OnMenuTapped(object sender, TappedEventArgs e)
    {
        // TODO: Open flyout menu or side drawer
    }

    private void OnNotificationsTapped(object sender, TappedEventArgs e)
    {
        // TODO: Open notifications page
    }

    private async void OnARTourTapped(object sender, TappedEventArgs e)
    {
        // TODO: Navigate to AR Scanner page when you create it
        await DisplayAlert("AR Tour", "AR Scanner coming soon!", "OK");
    }

    private async void OnLocationsTapped(object sender, TappedEventArgs e)
    {
        // TODO: Navigate to Locations list page when you create it
        await DisplayAlert("Locations", "Locations list coming soon!", "OK");
    }

    private void OnMapTapped(object sender, TappedEventArgs e)
    {
        // Switch to Map tab
        var tabBar = Shell.Current.Items.OfType<TabBar>().FirstOrDefault();
        if (tabBar != null && tabBar.Items.Count > 2)
        {
            Shell.Current.CurrentItem = tabBar;
            tabBar.CurrentItem = tabBar.Items[2]; // Map tab
        }
    }

    private async void OnHowItWorksTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//HowItWorksPage");
    }

    private async void OnAboutCampusTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//AboutCampusPage");
    }

    private async void OnSettingsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}