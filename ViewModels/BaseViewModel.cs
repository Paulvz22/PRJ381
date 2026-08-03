using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BelgiumCampusARTour.ViewModels;

public class BaseViewModel : INotifyPropertyChanged
{
    private bool _isBusy;
    private string _title = string.Empty;

    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); } }
    public bool IsNotBusy => !IsBusy;
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    // ─── Centralized Navigation ───
    protected static Task NavigateTo(string route) => Shell.Current.GoToAsync(route);
    protected static Task GoBack() => Shell.Current.GoToAsync("..");
    protected static Task ShowAlert(string title, string message, string cancel = "OK") =>
        Shell.Current.DisplayAlert(title, message, cancel);

    protected static void SwitchToTab(int tabIndex)
    {
        var tabBar = Shell.Current.Items.OfType<Microsoft.Maui.Controls.TabBar>().FirstOrDefault();
        if (tabBar != null && tabIndex >= 0 && tabIndex < tabBar.Items.Count)
        {
            Shell.Current.CurrentItem = tabBar;
            tabBar.CurrentItem = tabBar.Items[tabIndex];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}