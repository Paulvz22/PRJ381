namespace BCOpendayApp.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage() => InitializeComponent();
    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");
}