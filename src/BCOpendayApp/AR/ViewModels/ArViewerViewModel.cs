using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.AR.Sensors;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Services;
using BCOpendayApp.ViewModels;

namespace BCOpendayApp.AR.ViewModels;

/// <summary>
/// The visitor AR guide: live camera + the name/info of the building the camera points at.
/// Works straight after install - no sign-in, no scanning; building data comes from Firestore, the copy saved
/// on the phone, or the bundled file (see <see cref="IBuildingRepository"/>).
/// </summary>
public sealed class ArViewerViewModel : ViewModelBase
{
    // A new target must be seen this long before it replaces the current one (stops flicker between neighbours)...
    private static readonly TimeSpan SwitchDelay = TimeSpan.FromMilliseconds(450);

    // ...and the current one is kept this long after the camera moves off it.
    private static readonly TimeSpan LoseDelay = TimeSpan.FromMilliseconds(900);

    private readonly IBuildingRepository repository;
    private readonly IArSensorService sensors;
    private readonly IAnalyticsService analytics;

    private IReadOnlyList<BuildingAnchor> buildings = Array.Empty<BuildingAnchor>();
    private IReadOnlyList<BuildingSighting> sightings = Array.Empty<BuildingSighting>();
    private BuildingSighting? target;
    private BuildingAnchor? pendingTarget;
    private DateTimeOffset pendingSince;
    private DateTimeOffset targetLastSeen;
    private DateTimeOffset lastStatusUpdate;
    private bool isRunning;
    private bool isDetailsOpen;
    private bool cameraAllowed;
    private bool locationAllowed = true;
    private string dataMessage = "Loading buildings...";
    private string statusText = "Starting sensors...";
    private string hintText = string.Empty;
    private string insideText = string.Empty;

    public ArViewerViewModel(
        IBuildingRepository repository,
        IArSensorService sensors,
        IAnalyticsService analytics,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.repository = repository;
        this.sensors = sensors;
        this.analytics = analytics;

        ToggleDetailsCommand = CreateCommand(() =>
        {
            IsDetailsOpen = !IsDetailsOpen;
            return Task.CompletedTask;
        });
        OpenStopCommand = CreateCommand(OpenStopAsync);
        RefreshCommand = CreateCommand(() => LoadBuildingsAsync(forceRefresh: true));
        CloseCommand = CreateCommand(() => Navigation.GoBackAsync());
        OpenSettingsCommand = CreateCommand(() =>
        {
            AppInfo.Current.ShowSettingsUI();
            return Task.CompletedTask;
        });
    }

    /// <summary>Raised whenever the overlay should be redrawn (pose, position or data changed).</summary>
    public event EventHandler? FrameChanged;

    public IReadOnlyList<BuildingSighting> Sightings => sightings;

    public CameraPose? Pose => sensors.Pose;

    public string? TargetId => target?.Building.Id;

    public bool CameraAllowed
    {
        get => cameraAllowed;
        private set
        {
            if (SetProperty(ref cameraAllowed, value))
            {
                OnPropertyChanged(nameof(ShowNoCamera));
            }
        }
    }

    public bool ShowNoCamera => !cameraAllowed;

    public bool ShowNoLocation => !locationAllowed;

    public string DataMessage
    {
        get => dataMessage;
        private set => SetProperty(ref dataMessage, value);
    }

    /// <summary>Top status chip: GPS quality, heading, number of buildings nearby.</summary>
    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>What the visitor should do next ("Point at a building", "GPS is weak - step outside" ...).</summary>
    public string HintText
    {
        get => hintText;
        private set
        {
            if (SetProperty(ref hintText, value))
            {
                OnPropertyChanged(nameof(HasHint));
            }
        }
    }

    public bool HasHint => hintText.Length > 0 && !HasTarget;

    /// <summary>"You are inside X" when the visitor stands within a scanned outline.</summary>
    public string InsideText
    {
        get => insideText;
        private set
        {
            if (SetProperty(ref insideText, value))
            {
                OnPropertyChanged(nameof(IsInside));
            }
        }
    }

    public bool IsInside => insideText.Length > 0;

    public bool HasTarget => target is not null;

    public string TargetName => target?.Building.Name ?? string.Empty;

    public string TargetSummary => target?.Building.Summary ?? string.Empty;

    public string TargetDescription => target?.Building.Description ?? string.Empty;

    public bool TargetHasDescription => TargetDescription.Length > 0;

    public string TargetDistance => target is null ? string.Empty : $"About {FormatDistance(target.DistanceMeters)} away";

    public bool TargetHasStop => target is { Building.StopId.Length: > 0 };

    public bool IsDetailsOpen
    {
        get => isDetailsOpen;
        private set
        {
            if (SetProperty(ref isDetailsOpen, value))
            {
                OnPropertiesChanged(nameof(DetailsButtonText), nameof(ShowDescription));
            }
        }
    }

    public bool ShowDescription => isDetailsOpen && TargetHasDescription;

    public string DetailsButtonText => isDetailsOpen ? "Less info" : "More info";

    public AsyncCommand ToggleDetailsCommand { get; }

    public AsyncCommand OpenStopCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand CloseCommand { get; }

    public AsyncCommand OpenSettingsCommand { get; }

    /// <summary>
    /// Called by the page when it appears. Returns true when the camera may be started.
    /// Asks for permissions, loads the building list and starts GPS + orientation.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        analytics.LogScreenView("ArViewerPage");

        var (camera, location) = await ArPlatform.RequestPermissionsAsync(needCamera: true);
        CameraAllowed = camera;
        locationAllowed = location;
        OnPropertyChanged(nameof(ShowNoLocation));

        _ = LoadBuildingsAsync(forceRefresh: false);

        if (!location)
        {
            StatusText = "Location permission is off";
            HintText = "The AR guide needs your location to know which building you are looking at. Allow it in Settings.";
            return camera;
        }

        if (!isRunning)
        {
            isRunning = true;
            sensors.PoseChanged += OnPoseChanged;
            sensors.PositionChanged += OnPositionChanged;
            if (!await sensors.StartAsync())
            {
                HintText = "Could not start GPS. Check that Location is switched on.";
            }

            if (!sensors.HasOrientationSensor)
            {
                HintText = "This phone has no orientation sensor - directions may be less accurate.";
            }
        }

        return camera;
    }

    public void Stop()
    {
        if (!isRunning)
        {
            return;
        }

        isRunning = false;
        sensors.PoseChanged -= OnPoseChanged;
        sensors.PositionChanged -= OnPositionChanged;
        sensors.Stop();
    }

    private async Task LoadBuildingsAsync(bool forceRefresh)
    {
        try
        {
            var result = await repository.GetBuildingsAsync(forceRefresh);
            buildings = result.Buildings;
            DataMessage = result.Message;
            RecomputeSightings();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            DataMessage = "Building data could not be loaded.";
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e) => RecomputeSightings();

    private void OnPoseChanged(object? sender, EventArgs e)
    {
        if (DateTimeOffset.UtcNow - lastStatusUpdate > TimeSpan.FromMilliseconds(500))
        {
            UpdateStatus();
        }

        UpdateTarget();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecomputeSightings()
    {
        if (sensors.Position is not { } position)
        {
            sightings = Array.Empty<BuildingSighting>();
            UpdateStatus();
            return;
        }

        sightings = BuildingTargeting.Sight(position, buildings);
        var inside = sightings.FirstOrDefault(s => s.IsInside);
        InsideText = inside is null ? string.Empty : $"You are at {inside.Building.Name}";
        UpdateStatus();
        UpdateTarget();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStatus()
    {
        lastStatusUpdate = DateTimeOffset.UtcNow;
        var gps = sensors.Position is null
            ? "Finding GPS..."
            : $"GPS ±{Math.Round(sensors.AccuracyMeters)} m";
        var heading = sensors.Pose is { } pose ? $"{Math.Round(pose.Heading):000}°" : "--°";
        var north = sensors.IsTrueNorth ? string.Empty : " (magnetic)";
        var nearby = sightings.Count == 1 ? "1 building nearby" : $"{sightings.Count} buildings nearby";
        StatusText = $"{gps} · {heading}{north} · {nearby}";

        HintText = sensors.Position is null ? "Waiting for GPS - this is fastest outdoors."
            : buildings.Count == 0 ? "No buildings have been scanned yet. Ask campus staff, or tap Refresh."
            : sightings.Count == 0 ? "No scanned buildings within 400 m."
            : sensors.AccuracyMeters > 25 ? "GPS is weak here - step into the open for better results."
            : "Point your camera at a building. If labels seem off, move your phone in a figure-8 to calibrate the compass.";
    }

    private void UpdateTarget()
    {
        if (sensors.Position is not { } position || sensors.Pose is not { } pose)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var candidate = BuildingTargeting.Target(position, pose.Heading, sensors.AccuracyMeters, sightings);

        if (candidate is not null && candidate.Building.Id == target?.Building.Id)
        {
            // Same building - refresh its distance etc.
            target = candidate;
            targetLastSeen = now;
            pendingTarget = null;
            OnPropertyChanged(nameof(TargetDistance));
            return;
        }

        if (candidate is not null)
        {
            if (pendingTarget?.Id != candidate.Building.Id)
            {
                pendingTarget = candidate.Building;
                pendingSince = now;
            }

            if (target is null || now - pendingSince >= SwitchDelay)
            {
                SetTarget(candidate, now);
            }

            return;
        }

        pendingTarget = null;
        if (target is not null && now - targetLastSeen >= LoseDelay)
        {
            SetTarget(null, now);
        }
    }

    private void SetTarget(BuildingSighting? sighting, DateTimeOffset now)
    {
        var changed = sighting?.Building.Id != target?.Building.Id;
        target = sighting;
        targetLastSeen = now;
        pendingTarget = null;
        if (changed)
        {
            IsDetailsOpen = false;
            if (sighting is not null)
            {
                analytics.LogHotspotTapped(sighting.Building.Id, sighting.Building.Name);
                try
                {
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                }
                catch (Exception)
                {
                    // Haptics are optional.
                }
            }
        }

        OnPropertiesChanged(
            nameof(HasTarget),
            nameof(HasHint),
            nameof(TargetName),
            nameof(TargetSummary),
            nameof(TargetDescription),
            nameof(TargetHasDescription),
            nameof(TargetDistance),
            nameof(TargetHasStop),
            nameof(ShowDescription));
    }

    private Task OpenStopAsync() =>
        target is { Building.StopId.Length: > 0 } t
            ? Navigation.GoToStopAsync(Routes.StopInfo, t.Building.StopId)
            : Task.CompletedTask;

    public static string FormatDistance(double meters) =>
        meters < 1 ? "0 m" : meters < 1000 ? $"{Math.Round(meters / 5) * 5:0} m" : $"{meters / 1000:0.0} km";
}
