using System.Windows.Input;

namespace BelgiumCampusARTour.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    public bool NotificationsEnabled { get; set; } = true;
    public bool DarkModeEnabled { get; set; } = true;
    public bool LocationServicesEnabled { get; set; } = true;
    public ICommand GoBackCommand { get; }
    public SettingsViewModel() { Title = "Settings"; GoBackCommand = new Command(async () => await Shell.Current.GoToAsync("..")); }
}