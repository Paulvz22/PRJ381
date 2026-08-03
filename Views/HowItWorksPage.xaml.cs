namespace BelgiumCampusARTour.Views;

public partial class HowItWorksPage : ContentPage
{
    public HowItWorksPage() => InitializeComponent();

    private async void OnBackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");
    private async void OnGotItClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}