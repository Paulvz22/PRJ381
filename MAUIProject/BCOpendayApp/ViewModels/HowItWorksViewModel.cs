using BCOpendayApp.ViewModels;
using System.Windows.Input;

namespace BCOpendayApp.ViewModels;

public class HowItWorksViewModel : BaseViewModel
{
    public ICommand GotItCommand { get; }
    public HowItWorksViewModel() { Title = "How It Works"; GotItCommand = new Command(async () => await Shell.Current.GoToAsync("..")); }
}