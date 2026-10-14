using System.Diagnostics;
using BCOpendayApp.Models;
using BCOpendayApp.Services;

namespace BCOpendayApp.Views;

/// <summary>Shared asynchronous loading, retry and lifecycle cancellation for catalogue pages.</summary>
public abstract class LocationPage : ResponsivePage
{
    protected ILocationService LocationService { get; }
    protected LocationLoadResult? Catalogue { get; private set; }
    protected IReadOnlyList<Hotspot> Locations => Catalogue?.Locations ?? Array.Empty<Hotspot>();
    protected bool IsLoading { get; private set; }
    private string? error;
    private CancellationTokenSource? load;

    protected LocationPage(ILocationService locationService) => LocationService = locationService;
    protected abstract void Build();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshLocationsAsync();
    }

    protected async Task RefreshLocationsAsync(bool forceRefresh = false)
    {
        load?.Cancel();
        load?.Dispose();
        var request = new CancellationTokenSource();
        load = request;
        var token = request.Token;
        try
        {
            IsLoading = true;
            error = null;
            Build();
            var result = await LocationService.GetLocationsAsync(forceRefresh, token);
            if (!token.IsCancellationRequested) Catalogue = result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            error = "The bundled catalogue could not be loaded. Check Resources/Raw/locations.json and retry.";
        }
        finally
        {
            if (ReferenceEquals(load, request) && !token.IsCancellationRequested)
            {
                IsLoading = false;
                try { Build(); }
                catch (Exception exception) { Debug.WriteLine(exception); }
            }
        }
    }

    // Call just after Setup(). False means there is not yet any data to display.
    protected bool AddLocationStatus()
    {
        string message = IsLoading ? "Loading campus locations..."
            : error ?? Catalogue?.Message ?? "Locations have not been loaded.";
        Body.Add(Text(message, 13, false, Muted));
        var refresh = Action("Refresh locations", () => RefreshLocationsAsync(true), false);
        refresh.IsEnabled = !IsLoading;
        Body.Add(refresh);
        if (IsLoading && Catalogue is null)
            Body.Add(new ActivityIndicator { IsRunning = true, Color = Red });
        return Catalogue is not null;
    }

    protected override void OnDisappearing()
    {
        load?.Cancel();
        base.OnDisappearing();
    }
}
