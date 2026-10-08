namespace BCOpendayApp.Views;

public partial class MapPage : ContentPage
{
    private readonly List<BCOpendayApp.Models.Location> _locations = new()
    {
        new BCOpendayApp.Models.Location { Name = "Library", Description = "Books, study spaces, resources." },
        new BCOpendayApp.Models.Location { Name = "Reception", Description = "Main reception building." },
        new BCOpendayApp.Models.Location { Name = "Classroom Alpha", Description = "Classroom Alpha." },
        new BCOpendayApp.Models.Location { Name = "IT Lab", Description = "Computers, software, technical support." },
        new BCOpendayApp.Models.Location { Name = "Cafeteria", Description = "Food, drinks, seating." },
        new BCOpendayApp.Models.Location { Name = "Gym", Description = "Fitness, wellness." },
        new BCOpendayApp.Models.Location { Name = "Student Lounge", Description = "Relax, meet, connect." }
    };

    public MapPage()
    {
        InitializeComponent();
    }

    // Used by the map pins AND the Quick Access cards
    private async void OnPinTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is string locationName)
        {
            var location = _locations.FirstOrDefault(l => l.Name == locationName);
            if (location != null)
            {
                await DisplayAlert(location.Name, location.Description, "OK");
            }
        }
    }

    // Main / North / West switcher
    private void OnSectionSwitched(object sender, EventArgs e)
    {
        if (sender is Button tapped && tapped.CommandParameter is string section)
        {
            CampusMapImage.Source = $"campus_map_{section}.png";
            MapTitleLabel.Text = char.ToUpper(section[0]) + section.Substring(1) + " Campus Map";
            MainPins.IsVisible = section == "main";

            foreach (var tab in new[] { MainTab, NorthTab, WestTab })
            {
                tab.TextColor = tab == tapped ? Color.FromArgb("#F82B2E") : Colors.White;
            }
        }
    }

    private async void OnBackToMenuTapped(object sender, TappedEventArgs e)
    {
        await Navigation.PopAsync();
    }
}