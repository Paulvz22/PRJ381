namespace BCOpendayApp.Services;

public sealed class NavigationService : INavigationService
{
    public Task GoToTabAsync(string route) => Shell.Current.GoToAsync("//" + route);

    public Task GoToTabAsync(string route, string stopId) =>
        Shell.Current.GoToAsync("//" + route, StopParameter(stopId));

    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoToStopAsync(string route, string stopId) =>
        Shell.Current.GoToAsync(route, StopParameter(stopId));

    public Task GoBackAsync() => Shell.Current.GoToAsync("..");

    private static Dictionary<string, object> StopParameter(string stopId) =>
        new() { [Routes.StopIdParameter] = stopId };
}
