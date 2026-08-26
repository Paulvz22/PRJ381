namespace BCOpendayApp.Views;

public partial class MapPage : ContentPage
{
	public MapPage()
	{
		InitializeComponent();
	}

    private async void OnLibraryPinTapped(object sender, EventArgs e)
    {
        await DisplayAlert("Library", "This is the Library building.", "OK");
    }
}