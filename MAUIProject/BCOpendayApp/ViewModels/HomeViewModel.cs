using BCOpendayApp.ViewModels;
using BCOpendayApp.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MenuItem = BCOpendayApp.Models.MenuItem;

namespace BelgiumCampusARTour.ViewModels;

public class HomeViewModel : BaseViewModel
{
    public ObservableCollection<MenuItem> MenuItems { get; } = new();
    public string WelcomeMessage { get; set; } = "Welcome to Belgium Campus";
    public string Tagline { get; set; } = "Scan. Explore. Experience.";
    public ICommand NavigateToDetailCommand { get; }
    public ICommand OpenNotificationsCommand { get; }
    public ICommand OpenMenuCommand { get; }

    public HomeViewModel()
    {
        Title = "AR Tour";
        MenuItems.Add(new MenuItem { Title = "Start AR Tour", Icon = "??", Route = "TourPage", IconColor = Color.FromArgb("#F4A261") });
        MenuItems.Add(new MenuItem { Title = "Locations", Icon = "??", Route = "TourPage", IconColor = Color.FromArgb("#E63946") });
        MenuItems.Add(new MenuItem { Title = "Map", Icon = "??", Route = "MapPage", IconColor = Color.FromArgb("#E9C46A") });
        MenuItems.Add(new MenuItem { Title = "How It Works", Icon = "?", Route = "HowItWorksPage", IconColor = Color.FromArgb("#F4A261") });
        MenuItems.Add(new MenuItem { Title = "About Campus", Icon = "??", Route = "AboutCampusPage", IconColor = Color.FromArgb("#E63946") });
        MenuItems.Add(new MenuItem { Title = "Settings", Icon = "?", Route = "SettingsPage", IconColor = Color.FromArgb("#A8A8A8") });

        NavigateToDetailCommand = new Command<MenuItem>(async (item) => { if (item != null) await Shell.Current.GoToAsync(item.Route); });
        OpenNotificationsCommand = new Command(async () => await Shell.Current.DisplayAlert("Notifications", "No new notifications.", "OK"));
        OpenMenuCommand = new Command(async () => await Shell.Current.DisplayAlert("Menu", "Menu options would appear here.", "OK"));
    }
}