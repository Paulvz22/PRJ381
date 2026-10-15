namespace BCOpendayApp;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // The design is dark-only; force it so pickers, dialogs and system bars match.
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell());
}
