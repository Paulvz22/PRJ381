namespace BelgiumCampusARTour.Views;

public partial class AboutCampusPage : ContentPage
{
    public AboutCampusPage() => InitializeComponent();
    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");
}