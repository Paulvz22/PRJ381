namespace BelgiumCampusARTour.Views;

public partial class SplashPage : ContentPage
{
    public SplashPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await Task.Delay(2500);
        await Shell.Current.GoToAsync("//MainTabs");
    }
}