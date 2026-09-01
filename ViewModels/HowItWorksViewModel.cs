using System.Windows.Input;

namespace BelgiumCampusARTour.ViewModels;

public class HowItWorksViewModel : BaseViewModel
{
    public ICommand GotItCommand { get; }
    public HowItWorksViewModel() { Title = "How It Works"; GotItCommand = new Command(async () => await Shell.Current.GoToAsync("..")); }
}