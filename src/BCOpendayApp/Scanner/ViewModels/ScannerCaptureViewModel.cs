using System.Collections.ObjectModel;
using BCOpendayApp.AR;
using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.AR.Sensors;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.Scanner.ViewModels;

/// <summary>A captured corner (or centre) shown in the list.</summary>
public sealed record CapturedPointItem(int Number, GeoPoint Point, double AccuracyMeters, int Samples)
{
    public string Title => $"Point {Number}";

    public string Details => Samples == 0
        ? $"{Point.Lat:F6}, {Point.Lng:F6} (saved earlier)"
        : $"±{AccuracyMeters:0.0} m from {Samples} GPS readings";
}

/// <summary>A tour stop the building can link to. ToString() is what the Picker shows.</summary>
public sealed record StopOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// "Scanning" a building = recording WHERE it is, which is what the AR view needs to recognise it from any
/// side and any distance (unlike surface/feature anchors, which only work near the spot they were made).
/// Two ways:
///  - Outline (most accurate): stand at each outside corner and capture it; the outline is the polygon.
///  - Centre + radius (quick): stand in the middle / at the entrance and set how big the building is.
/// Each point averages several GPS readings and reports its accuracy.
/// </summary>
public sealed class ScannerCaptureViewModel : ViewModelBase, IQueryAttributable
{
    private static readonly TimeSpan SampleTime = TimeSpan.FromSeconds(8);
    private const double GoodAccuracyMeters = 6;
    private const double PoorAccuracyMeters = 15;

    private readonly IScannerAuthService auth;
    private readonly IBuildingRepository repository;
    private readonly ScannerDraftStore drafts;
    private readonly IArSensorService sensors;
    private readonly ILocationService stops;
    private readonly LocationSampler sampler = new();

    private BuildingAnchor? original;
    private string requestedId = string.Empty;
    private bool sensorsRunning;
    private bool isOutlineMode = true;
    private string name = string.Empty;
    private string summary = string.Empty;
    private string description = string.Empty;
    private bool isActive = true;
    private double radiusMeters = 20;
    private StopOption? selectedStop;
    private IReadOnlyList<StopOption> stopOptions = new[] { new StopOption("", "No linked tour stop") };
    private CapturedPointItem? centre;
    private bool isSampling;
    private double sampleProgress;
    private string gpsText = "Waiting for GPS...";
    private string shapeText = string.Empty;
    private string shapeWarning = string.Empty;

    public ScannerCaptureViewModel(
        IScannerAuthService auth,
        IBuildingRepository repository,
        ScannerDraftStore drafts,
        IArSensorService sensors,
        ILocationService stops,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.auth = auth;
        this.repository = repository;
        this.drafts = drafts;
        this.sensors = sensors;
        this.stops = stops;

        UseOutlineCommand = CreateCommand(() => SetMode(outline: true));
        UseRadiusCommand = CreateCommand(() => SetMode(outline: false));
        CapturePointCommand = CreateCommand(CapturePointAsync);
        UndoPointCommand = CreateCommand(() =>
        {
            if (Points.Count > 0)
            {
                Points.RemoveAt(Points.Count - 1);
                UpdateShape();
            }

            return Task.CompletedTask;
        });
        FixOrderCommand = CreateCommand(FixOrder);
        SaveCommand = CreateCommand(SaveAsync);
        DeleteCommand = CreateCommand(DeleteAsync);
        TestCommand = CreateCommand(() => Navigation.GoToAsync(ArRoutes.Viewer));
    }

    public ObservableCollection<CapturedPointItem> Points { get; } = new();

    public string PageTitle => original is null ? "Scan a new building" : $"Edit {original.Name}";

    public bool IsEditing => original is not null;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value ?? string.Empty);
    }

    public string Summary
    {
        get => summary;
        set => SetProperty(ref summary, value ?? string.Empty);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value ?? string.Empty);
    }

    public bool IsActive
    {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }

    public IReadOnlyList<StopOption> StopOptions
    {
        get => stopOptions;
        private set => SetProperty(ref stopOptions, value);
    }

    public StopOption? SelectedStop
    {
        get => selectedStop;
        set => SetProperty(ref selectedStop, value);
    }

    public bool IsOutlineMode
    {
        get => isOutlineMode;
        private set
        {
            if (SetProperty(ref isOutlineMode, value))
            {
                OnPropertiesChanged(nameof(IsRadiusMode), nameof(CaptureButtonText), nameof(ModeInstructions));
            }
        }
    }

    public bool IsRadiusMode => !isOutlineMode;

    public string ModeInstructions => isOutlineMode
        ? "Stand right against an OUTSIDE corner of the building and tap Capture. Wait for it to finish, walk along the wall to the next corner, and repeat for every corner in order (4 for a rectangle). Stay outside - GPS is much worse indoors."
        : "Stand in the middle of the building's footprint (or at its main entrance), tap Capture, then set the radius so the circle covers the building.";

    public string CaptureButtonText => isSampling ? "Capturing... keep still"
        : isOutlineMode ? $"Capture corner {Points.Count + 1}"
        : centre is null ? "Capture centre" : "Capture centre again";

    public double RadiusMeters
    {
        get => radiusMeters;
        set
        {
            if (SetProperty(ref radiusMeters, Math.Round(Math.Clamp(value, BuildingValidation.MinRadiusMeters, 150))))
            {
                OnPropertyChanged(nameof(RadiusText));
                UpdateShape();
            }
        }
    }

    public string RadiusText => $"Radius: {radiusMeters:0} m";

    public string CentreText => centre is null ? "No centre captured yet." : $"Centre captured {centre.Details}";

    public bool IsSampling
    {
        get => isSampling;
        private set
        {
            if (SetProperty(ref isSampling, value))
            {
                OnPropertyChanged(nameof(CaptureButtonText));
            }
        }
    }

    public double SampleProgress
    {
        get => sampleProgress;
        private set => SetProperty(ref sampleProgress, value);
    }

    /// <summary>Live GPS quality so staff can wait for a good fix before capturing.</summary>
    public string GpsText
    {
        get => gpsText;
        private set => SetProperty(ref gpsText, value);
    }

    public string ShapeText
    {
        get => shapeText;
        private set => SetProperty(ref shapeText, value);
    }

    public string ShapeWarning
    {
        get => shapeWarning;
        private set
        {
            if (SetProperty(ref shapeWarning, value))
            {
                OnPropertyChanged(nameof(HasShapeWarning));
            }
        }
    }

    public bool HasShapeWarning => shapeWarning.Length > 0;

    public AsyncCommand UseOutlineCommand { get; }

    public AsyncCommand UseRadiusCommand { get; }

    public AsyncCommand CapturePointCommand { get; }

    public AsyncCommand UndoPointCommand { get; }

    public AsyncCommand FixOrderCommand { get; }

    public AsyncCommand SaveCommand { get; }

    public AsyncCommand DeleteCommand { get; }

    public AsyncCommand TestCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        requestedId = query.TryGetValue(Routes.StopIdParameter, out var value) && value is string text ? text : string.Empty;

    public override async void OnAppearing()
    {
        try
        {
            if (!auth.IsUnlocked)
            {
                await Dialogs.AlertAsync("Scanner locked", "Your scanner session has ended. Sign in again.");
                await Navigation.GoBackAsync();
                return;
            }

            await LoadStopOptionsAsync();
            if (original is null && requestedId.Length > 0)
            {
                await LoadExistingAsync(requestedId);
            }

            var (_, location) = await ArPlatform.RequestPermissionsAsync(needCamera: false);
            if (!location)
            {
                GpsText = "Location permission is off - the scanner cannot work without it.";
                return;
            }

            if (!sensorsRunning)
            {
                sensorsRunning = true;
                sensors.PositionChanged += OnPositionChanged;
                await sensors.StartAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }

    public override void OnDisappearing()
    {
        if (sensorsRunning)
        {
            sensorsRunning = false;
            sensors.PositionChanged -= OnPositionChanged;
            sensors.Stop();
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (sensors.RawFix is not { } fix)
        {
            return;
        }

        if (isSampling)
        {
            sampler.Add(fix.Point, fix.AccuracyMeters);
        }

        var quality = fix.AccuracyMeters <= GoodAccuracyMeters ? "good"
            : fix.AccuracyMeters <= PoorAccuracyMeters ? "OK"
            : "poor - wait or move away from walls/trees";
        GpsText = $"GPS ±{fix.AccuracyMeters:0} m ({quality})";
    }

    private async Task LoadStopOptionsAsync()
    {
        if (StopOptions.Count > 1)
        {
            return;
        }

        var result = await stops.GetLocationsAsync();
        StopOptions = new[] { new StopOption("", "No linked tour stop") }
            .Concat(result.Locations.Select(l => new StopOption(l.Id, l.Name)))
            .ToList();
        SelectedStop ??= StopOptions[0];
    }

    private async Task LoadExistingAsync(string id)
    {
        BuildingAnchor? building;
        if (id.StartsWith("draft:", StringComparison.Ordinal))
        {
            var draftId = id["draft:".Length..];
            building = (await drafts.LoadAsync()).FirstOrDefault(b => b.Id == draftId);
        }
        else
        {
            building = (await repository.GetBuildingsAsync()).Buildings.FirstOrDefault(b => b.Id == id);
        }

        if (building is null)
        {
            return;
        }

        original = building;
        Name = building.Name;
        Summary = building.Summary;
        Description = building.Description;
        IsActive = building.IsActive;
        SelectedStop = StopOptions.FirstOrDefault(s => s.Id == building.StopId) ?? StopOptions[0];
        Points.Clear();
        if (building.HasFootprint)
        {
            IsOutlineMode = true;
            var n = 1;
            foreach (var p in building.Footprint)
            {
                Points.Add(new CapturedPointItem(n++, p, 0, 0));
            }
        }
        else
        {
            IsOutlineMode = false;
            centre = new CapturedPointItem(1, building.Center, 0, 0);
            RadiusMeters = building.RadiusMeters;
        }

        OnPropertiesChanged(nameof(PageTitle), nameof(IsEditing), nameof(CentreText), nameof(CaptureButtonText));
        UpdateShape();
    }

    private Task SetMode(bool outline)
    {
        IsOutlineMode = outline;
        UpdateShape();
        return Task.CompletedTask;
    }

    private async Task CapturePointAsync()
    {
        if (sensors.RawFix is null)
        {
            await Dialogs.AlertAsync("No GPS yet", "Wait until a GPS reading is shown above, preferably outdoors.");
            return;
        }

        sampler.Clear();
        IsSampling = true;
        try
        {
            var started = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - started < SampleTime)
            {
                SampleProgress = (DateTimeOffset.UtcNow - started) / SampleTime;
                if (sampler.Count >= 6 && sampler.BestAccuracy <= GoodAccuracyMeters / 1.5)
                {
                    break; // excellent fix already - no need to wait the full time
                }

                await Task.Delay(250);
            }

            if (sampler.Count == 0 && sensors.RawFix is { } fix)
            {
                sampler.Add(fix.Point, fix.AccuracyMeters);
            }
        }
        finally
        {
            IsSampling = false;
            SampleProgress = 0;
        }

        if (sampler.Result() is not { } result)
        {
            await Dialogs.AlertAsync("No GPS readings", "No position was received. Check that Location is on and try again.");
            return;
        }

        if (result.AccuracyMeters > PoorAccuracyMeters
            && !await Dialogs.ConfirmAsync("Poor GPS", $"This point is only accurate to about ±{result.AccuracyMeters:0} m. Use it anyway?", "Use it", "Retry"))
        {
            return;
        }

        var item = new CapturedPointItem(Points.Count + 1, result.Point, result.AccuracyMeters, sampler.Count);
        if (isOutlineMode)
        {
            Points.Add(item);
        }
        else
        {
            centre = item;
            OnPropertyChanged(nameof(CentreText));
        }

        OnPropertyChanged(nameof(CaptureButtonText));
        UpdateShape();
    }

    private Task FixOrder()
    {
        if (Points.Count < 3)
        {
            return Task.CompletedTask;
        }

        // Re-order the corners around their convex hull (fixes corners captured out of order).
        var origin = Points[0].Point;
        var hull = GeoMath.ConvexHull(Points.Select(p => GeoMath.ToLocal(origin, p.Point)));
        var reordered = hull
            .Select(local => Points.OrderBy(p => (GeoMath.ToLocal(origin, p.Point) - local).Length).First())
            .ToList();
        Points.Clear();
        var n = 1;
        foreach (var p in reordered)
        {
            Points.Add(p with { Number = n++ });
        }

        UpdateShape();
        return Task.CompletedTask;
    }

    private void UpdateShape()
    {
        ShapeWarning = string.Empty;
        if (isOutlineMode)
        {
            if (Points.Count < 3)
            {
                ShapeText = $"{Points.Count} of at least 3 corners captured.";
                return;
            }

            var origin = Points[0].Point;
            var local = Points.Select(p => GeoMath.ToLocal(origin, p.Point)).ToList();
            var area = Math.Abs(GeoMath.SignedArea(local));
            ShapeText = $"{Points.Count} corners · about {area:0} m² · outline ready";
            if (GeoMath.IsSelfIntersecting(local))
            {
                ShapeWarning = "The outline crosses itself - the corners were probably captured out of order. Tap \"Fix corner order\".";
            }
            else if (area < 20)
            {
                ShapeWarning = "The outline is very small. Were the corners captured at the building's corners?";
            }
        }
        else
        {
            ShapeText = centre is null ? "Capture the centre, then set the radius." : $"Circle of {radiusMeters:0} m radius ready";
        }
    }

    private BuildingAnchor? BuildAnchor(out string? problem)
    {
        problem = null;
        GeoPoint center;
        double radius;
        IReadOnlyList<GeoPoint> footprint;

        if (isOutlineMode)
        {
            if (Points.Count < 3)
            {
                problem = "Capture at least 3 corners first.";
                return null;
            }

            footprint = Points.Select(p => p.Point).ToList();
            var origin = footprint[0];
            var local = footprint.Select(p => GeoMath.ToLocal(origin, p)).ToList();
            center = GeoMath.FromLocal(origin, GeoMath.Centroid(local));
            radius = footprint.Max(p => GeoMath.DistanceMeters(center, p));
        }
        else
        {
            if (centre is null)
            {
                problem = "Capture the centre first.";
                return null;
            }

            footprint = Array.Empty<GeoPoint>();
            center = centre.Point;
            radius = radiusMeters;
        }

        var id = original?.Id ?? BuildingValidation.SlugFromName(Name);
        var anchor = new BuildingAnchor
        {
            Id = id,
            Name = Name.Trim(),
            Summary = Summary.Trim(),
            Description = Description.Trim(),
            StopId = SelectedStop?.Id ?? string.Empty,
            CenterLat = center.Lat,
            CenterLng = center.Lng,
            RadiusMeters = Math.Clamp(radius, BuildingValidation.MinRadiusMeters, BuildingValidation.MaxRadiusMeters),
            Footprint = footprint,
            IsActive = IsActive,
            UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedBy = auth.StaffUid ?? string.Empty,
        };

        problem = BuildingValidation.Problem(anchor);
        return problem is null ? anchor : null;
    }

    private async Task SaveAsync()
    {
        if (BuildAnchor(out var problem) is not { } anchor)
        {
            await Dialogs.AlertAsync("Cannot save yet", problem ?? "Check the details.");
            return;
        }

        if (original is null)
        {
            var existing = await repository.GetBuildingsAsync();
            if (existing.Buildings.Any(b => b.Id == anchor.Id)
                && !await Dialogs.ConfirmAsync("Replace building?", $"A building with id '{anchor.Id}' already exists. Replace it with this scan?", "Replace", "Cancel"))
            {
                return;
            }
        }

        // Save on the phone first so nothing is lost if the upload fails.
        await drafts.UpsertAsync(anchor);
        try
        {
            if (!auth.IsUnlocked)
            {
                throw new InvalidOperationException("Scanner session expired.");
            }

            await repository.SaveAsync(anchor);
            await drafts.RemoveAsync(anchor.Id);
            await Dialogs.AlertAsync("Saved", $"{anchor.Name} is live. Visitors will see it the next time their AR guide loads buildings.");
            await Navigation.GoBackAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await Dialogs.AlertAsync(
                "Saved on this phone only",
                "The upload failed (no connection, expired sign-in, or the account is not allowed to scan). " +
                "The scan is kept as a draft - upload it from the scanner home screen.");
        }
    }

    private async Task DeleteAsync()
    {
        if (original is null
            || !await Dialogs.ConfirmAsync("Delete building?", $"Remove {original.Name} for all visitors?", "Delete", "Keep"))
        {
            return;
        }

        await drafts.RemoveAsync(original.Id);
        try
        {
            await repository.DeleteAsync(original.Id);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await Dialogs.AlertAsync("Delete failed", "Check the connection and try again.");
            return;
        }

        await Navigation.GoBackAsync();
    }
}
