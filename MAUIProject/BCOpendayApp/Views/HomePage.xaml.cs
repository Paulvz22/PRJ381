namespace BCOpendayApp.Views;

public partial class HomePage : ContentPage
{
	public HomePage()
	{
		InitializeComponent();
	}

    private void HomeTapped(object sender, TappedEventArgs e)
    {
        // Already on the Home page
    }

    private void TourTapped(object sender, TappedEventArgs e)
    {
        // await Navigation.PushAsync(new TourPage());
    }

    private void MapTapped(object sender, TappedEventArgs e)
    {
        Navigation.PushAsync(new MapPage());
    }

    private void ProfileTapped(object sender, TappedEventArgs e)
    {
        // await Navigation.PushAsync(new ProfilePage());
    }
}