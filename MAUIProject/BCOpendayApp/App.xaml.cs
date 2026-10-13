namespace BCOpendayApp;

public partial class App : Application
{
    private readonly AppShell shell;
    public App(AppShell shell)
    {
        InitializeComponent();
        this.shell = shell;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(shell);
}
