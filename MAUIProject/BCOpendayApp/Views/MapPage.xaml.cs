using BCOpendayApp.Models;

namespace BCOpendayApp.Views;

public partial class MapPage : ContentPage
{
    private readonly List<BCOpendayApp.Models.Location> _locations = new()
    {
        new BCOpendayApp.Models.Location { Name = "Library", Description = "Main library and study area." },
        new BCOpendayApp.Models.Location { Name = "Reception", Description = "Main reception building." },
        new BCOpendayApp.Models.Location { Name = "Classroom Alpha", Description = "Classroom Alpha." }
        // will add more pins for other locations
    };

    public MapPage()
    {
        InitializeComponent();
    }

    private async void OnPinTapped(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string locationName)
        {
            var location = _locations.FirstOrDefault(l => l.Name == locationName);
            if (location != null)
            {
                await DisplayAlert(location.Name, location.Description, "OK");
            }
        }
    }
}