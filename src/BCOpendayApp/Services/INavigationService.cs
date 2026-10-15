namespace BCOpendayApp.Services;

/// <summary>Lets view models navigate without referencing Shell or any page.</summary>
public interface INavigationService
{
    /// <summary>Switches to a root tab (absolute navigation, clears any pushed pages).</summary>
    Task GoToTabAsync(string route);

    /// <summary>Switches to a root tab and tells it which stop to focus (e.g. the Map tab highlighting a pin).</summary>
    Task GoToTabAsync(string route, string stopId);

    /// <summary>Pushes a registered page on top of the current one.</summary>
    Task GoToAsync(string route);

    /// <summary>Opens one of the stop pages for the given stop.</summary>
    Task GoToStopAsync(string route, string stopId);

    Task GoBackAsync();
}
