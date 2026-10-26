using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.AR.Nav;
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
    private readonly IVisitorProgressService visitorProgress;
    private readonly INarrationService narration;
    private bool narrationInRange;
    private string dataMessage = "Loading buildings...";
    private string statusText = "Starting sensors...";
    private string hintText = string.Empty;
    private string insideText = string.Empty;

    // "Navigate to a building": the destination's centre is the last waypoint on the route. When the bundled
    // walking graph is available the route follows it; otherwise it stays the straight line, so guidance always works.
    private BuildingAnchor? destination;
    private IReadOnlyList<GeoPoint> route = Array.Empty<GeoPoint>();
    private IReadOnlyList<DestinationOption> destinations = Array.Empty<DestinationOption>();
    private int navIndex;
    private bool isNavigating;
    private bool isChoosingDestination;
    private string navInstruction = string.Empty;
    private string navDistanceText = string.Empty;
    private string navArrivedText = string.Empty;
    private double navTurnDegrees;
    private bool hasArrived;
    private GeoPoint? nextWaypoint;

    // The walking graph (Resources/Raw/nav-graph.json) is loaded at most once; a missing or invalid file just
    // leaves these null and guidance falls back to the straight line.
    private const string NavGraphAsset = "nav-graph.json";
    private Task? graphLoad;
    private NavGraph? navGraph;
    private NavRouter? router;

    public ArViewerViewModel(
        IBuildingRepository repository,
        IArSensorService sensors,
        IAnalyticsService analytics,
        IVisitorProgressService visitorProgress,
        INarrationService narration,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.repository = repository;
        this.sensors = sensors;
        this.analytics = analytics;
        this.visitorProgress = visitorProgress;
        this.narration = narration;

        ToggleDetailsCommand = CreateCommand(() =>
        {
            IsDetailsOpen = !IsDetailsOpen;
            return Task.CompletedTask;
        });
        OpenStopCommand = CreateCommand(OpenStopAsync);
        ListenCommand = CreateCommand(ToggleNarrationAsync);
        RefreshCommand = CreateCommand(() => LoadBuildingsAsync(forceRefresh: true));
        CloseCommand = CreateCommand(() => Navigation.GoBackAsync());
        OpenSettingsCommand = CreateCommand(() =>
        {
            AppInfo.Current.ShowSettingsUI();
            return Task.CompletedTask;
        });
        GoToCommand = CreateCommand(ToggleDestinationListAsync);
        StopCommand = CreateCommand(() =>
        {
            StopNavigating();
            return Task.CompletedTask;
        });
        ChooseDestinationCommand = CreateCommand<BuildingAnchor>(ChooseDestinationAsync);
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

    /// <summary>Every scanned building is a campus location, so its location page can always be opened.</summary>
    public bool TargetHasStop => target is not null;

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

    /// <summary>Reads the building's description aloud with the phone's voice (or stops it).</summary>
    public AsyncCommand ListenCommand { get; }

    /// <summary>
    /// The "Listen" button: the building has a description and the visitor is within 15 m of it
    /// (<see cref="NarrationRange"/>). It stays while the narration plays, so it can always be stopped.
    /// </summary>
    public bool CanListen => TargetHasDescription && (narrationInRange || narration.IsSpeaking);

    public string ListenButtonText => narration.IsSpeaking ? "\u25A0  Stop narration" : "\U0001F50A  Listen";

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand CloseCommand { get; }

    public AsyncCommand OpenSettingsCommand { get; }

    /// <summary>Opens (or closes) the inline list of buildings the visitor can walk to.</summary>
    public AsyncCommand GoToCommand { get; }

    /// <summary>Clears the destination and stops guidance.</summary>
    public AsyncCommand StopCommand { get; }

    /// <summary>Sets the tapped building as the destination (parameter is its <see cref="BuildingAnchor"/>).</summary>
    public AsyncCommand ChooseDestinationCommand { get; }

    /// <summary>True while a destination is set and guidance should be shown.</summary>
    public bool IsNavigating
    {
        get => isNavigating;
        private set
        {
            if (SetProperty(ref isNavigating, value))
            {
                OnPropertyChanged(nameof(IsGuidanceActive));
            }
        }
    }

    /// <summary>True while the inline destination list should be visible.</summary>
    public bool IsChoosingDestination
    {
        get => isChoosingDestination;
        private set => SetProperty(ref isChoosingDestination, value);
    }

    /// <summary>The active buildings offered by the "Go to..." list (name + distance).</summary>
    public IReadOnlyList<DestinationOption> Destinations
    {
        get => destinations;
        private set => SetProperty(ref destinations, value);
    }

    /// <summary>The current turn instruction, e.g. "Bear right" (see <see cref="NavGuidance.Describe"/>).</summary>
    public string NavInstruction
    {
        get => navInstruction;
        private set => SetProperty(ref navInstruction, value);
    }

    /// <summary>Distance to the destination, formatted like the AR labels.</summary>
    public string NavDistanceText
    {
        get => navDistanceText;
        private set => SetProperty(ref navDistanceText, value);
    }

    /// <summary>"You have arrived at X" once the visitor reaches the destination.</summary>
    public string NavArrivedText
    {
        get => navArrivedText;
        private set => SetProperty(ref navArrivedText, value);
    }

    /// <summary>Signed turn angle to the destination (negative = left, positive = right); rotates the arrow.</summary>
    public double NavTurnDegrees
    {
        get => navTurnDegrees;
        private set => SetProperty(ref navTurnDegrees, value);
    }

    /// <summary>True once the visitor is within the arrival radius of the destination.</summary>
    public bool HasArrived
    {
        get => hasArrived;
        private set
        {
            if (SetProperty(ref hasArrived, value))
            {
                OnPropertyChanged(nameof(IsGuidanceActive));
            }
        }
    }

    /// <summary>
    /// The waypoint the visitor is currently walking to: the next graph node on a routed path, or the
    /// destination centre when guidance is straight-line. The page draws the ground chevrons toward it.
    /// </summary>
    public GeoPoint? NextWaypoint
    {
        get => nextWaypoint;
        private set => SetProperty(ref nextWaypoint, value);
    }

    /// <summary>
    /// The full active route (the last waypoint is the destination), or an empty list when not navigating.
    /// The page walks this polyline to draw the ground chevrons around corners.
    /// </summary>
    public IReadOnlyList<GeoPoint> NavRoute => route;

    /// <summary>Index in <see cref="NavRoute"/> of the waypoint being walked to next.</summary>
    public int NavRouteIndex => navIndex;

    /// <summary>True while turn-by-turn guidance should be shown (navigating and not yet arrived).</summary>
    public bool IsGuidanceActive => isNavigating && !hasArrived;

    /// <summary>Id of the destination building, or null; used by the page to place the marker.</summary>
    public string? DestinationId => destination?.Id;

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
        _ = EnsureGraphAsync();

        if (!location)
        {
            StatusText = "Location permission is off";
            HintText = "The AR guide needs your location to know which building you are looking at. Allow it in Settings.";
            return camera;
        }

        if (!isRunning)
        {
            isRunning = true;
            narration.SpeakingChanged += OnSpeakingChanged;
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
        narration.SpeakingChanged -= OnSpeakingChanged;
        narration.Stop();
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
        UpdateNavigation();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecomputeSightings()
    {
        if (sensors.Position is not { } position)
        {
            sightings = Array.Empty<BuildingSighting>();
            UpdateStatus();
            UpdateNavigation();
            return;
        }

        sightings = BuildingTargeting.Sight(position, buildings);
        var inside = sightings.FirstOrDefault(s => s.IsInside);
        InsideText = inside is null ? string.Empty : $"You are at {inside.Building.Name}";
        UpdateStatus();
        UpdateTarget();
        UpdateNavigation();
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
            UpdateNarrationRange();
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
            narration.Stop(); // a new building: don't keep reading the previous one
            narrationInRange = false;
            if (sighting is not null)
            {
                analytics.LogHotspotTapped(sighting.Building.Id, sighting.Building.Name);

                // Seeing a building in the AR camera counts as visiting that location (journey progress on Home).
                visitorProgress.MarkVisited(sighting.Building.Id);
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
        UpdateNarrationRange();
    }

    private Task ToggleDestinationListAsync()
    {
        if (IsChoosingDestination)
        {
            IsChoosingDestination = false;
            return Task.CompletedTask;
        }

        Destinations = BuildDestinations();
        IsChoosingDestination = true;
        return Task.CompletedTask;
    }

    private IReadOnlyList<DestinationOption> BuildDestinations()
    {
        var position = sensors.Position;
        return buildings
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new DestinationOption(
                b,
                position is { } p ? FormatDistance(GeoMath.DistanceMeters(p, b.Center)) : string.Empty,
                ChooseDestinationCommand))
            .ToList();
    }

    private async Task ChooseDestinationAsync(BuildingAnchor building)
    {
        await EnsureGraphAsync();

        destination = building;
        route = BuildRoute(building);
        navIndex = 0;
        NextWaypoint = null;
        IsChoosingDestination = false;
        HasArrived = false;
        OnPropertyChanged(nameof(DestinationId));
        IsNavigating = true;
        UpdateNavigation();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The walking route to <paramref name="building"/>: nearest graph node to the visitor, Dijkstra to the node
    /// that belongs to the building (or the node nearest its centre), then the building centre as the last waypoint.
    /// Falls back to the single straight-line waypoint when there is no usable graph or the nodes are unreachable.
    /// </summary>
    private IReadOnlyList<GeoPoint> BuildRoute(BuildingAnchor building)
    {
        var straightLine = new[] { building.Center };
        if (navGraph is not { } graph || router is not { } activeRouter || sensors.Position is not { } position)
        {
            return straightLine;
        }

        if (activeRouter.NearestNode(position) is not { } start)
        {
            return straightLine;
        }

        var end = graph.Nodes.FirstOrDefault(node => node.BuildingId == building.Id)
            ?? activeRouter.NearestNode(building.Center);
        if (end is null)
        {
            return straightLine;
        }

        if (activeRouter.ShortestPath(start.Id, end.Id) is not { Count: > 0 } path)
        {
            return straightLine;
        }

        var routed = new List<GeoPoint>(path) { building.Center };
        return routed;
    }

    private void StopNavigating()
    {
        destination = null;
        route = Array.Empty<GeoPoint>();
        navIndex = 0;
        IsChoosingDestination = false;
        IsNavigating = false;
        NavInstruction = string.Empty;
        NavDistanceText = string.Empty;
        NavArrivedText = string.Empty;
        NavTurnDegrees = 0;
        NextWaypoint = null;
        HasArrived = false;
        OnPropertyChanged(nameof(DestinationId));
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Feeds the active route to <see cref="NavGuidance"/> and updates the banner and next waypoint.</summary>
    private void UpdateNavigation()
    {
        if (destination is not { } targetBuilding)
        {
            return;
        }

        if (sensors.Position is not { } position || sensors.Pose is not { } pose)
        {
            NavInstruction = "Finding your position...";
            NavDistanceText = string.Empty;
            NavArrivedText = string.Empty;
            NavTurnDegrees = 0;
            NextWaypoint = null;
            HasArrived = false;
            return;
        }

        if (NavGuidance.Next(position, pose.Heading, route, navIndex, sensors.AccuracyMeters) is not { } step)
        {
            return;
        }

        navIndex = step.NextIndex;
        NavTurnDegrees = step.TurnDegrees;
        NavInstruction = NavGuidance.Describe(step.TurnDegrees);
        NavDistanceText = step.Arrived ? string.Empty : FormatDistance(step.DistanceToNextMeters);
        NextWaypoint = step.Next;
        HasArrived = step.Arrived;
        NavArrivedText = step.Arrived ? $"You have arrived at {targetBuilding.Name}" : string.Empty;
    }

    /// <summary>Starts loading the bundled walking graph exactly once.</summary>
    private Task EnsureGraphAsync() => graphLoad ??= LoadGraphAsync();

    private async Task LoadGraphAsync()
    {
        try
        {
            if (!await FileSystem.Current.AppPackageFileExistsAsync(NavGraphAsset))
            {
                return;
            }

            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(NavGraphAsset);
            using var reader = new StreamReader(stream);
            var graph = NavGraph.Load(await reader.ReadToEndAsync());
            navGraph = graph;
            router = new NavRouter(graph);
        }
        catch (Exception exception)
        {
            // A missing or malformed graph is not an error: guidance just stays straight-line.
            System.Diagnostics.Debug.WriteLine(exception);
            navGraph = null;
            router = null;
        }
    }

    private void UpdateNarrationRange()
    {
        var inRange = target is not null && NarrationRange.IsInRange(target.DistanceMeters, narrationInRange);
        if (inRange != narrationInRange)
        {
            narrationInRange = inRange;
            OnPropertyChanged(nameof(CanListen));
        }
        else if (target is null)
        {
            OnPropertyChanged(nameof(CanListen));
        }
    }

    private async Task ToggleNarrationAsync()
    {
        if (narration.IsSpeaking)
        {
            narration.Stop();
            return;
        }

        if (!TargetHasDescription)
        {
            return;
        }

        try
        {
            await narration.SpeakAsync(TargetDescription);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await Dialogs.AlertAsync("Narration unavailable", "This phone has no text-to-speech voice installed.");
        }
    }

    private void OnSpeakingChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => OnPropertiesChanged(nameof(CanListen), nameof(ListenButtonText)));

    private Task OpenStopAsync() =>
        target is { } t
            ? Navigation.GoToStopAsync(Routes.LocationDetail, t.Building.Id)
            : Task.CompletedTask;

    public static string FormatDistance(double meters) =>
        meters < 1 ? "0 m" : meters < 1000 ? $"{Math.Round(meters / 5) * 5:0} m" : $"{meters / 1000:0.0} km";
}

/// <summary>One row in the inline "Go to..." list: a destination building and how far it is.</summary>
public sealed class DestinationOption
{
    public DestinationOption(BuildingAnchor building, string distanceText, AsyncCommand selectCommand)
    {
        Building = building;
        DistanceText = distanceText;
        SelectCommand = selectCommand;
    }

    public BuildingAnchor Building { get; }

    public string Name => Building.Name;

    public string DistanceText { get; }

    /// <summary>Name and distance on one line, e.g. "Library  ·  120 m".</summary>
    public string Label => DistanceText.Length > 0 ? $"{Name}  ·  {DistanceText}" : Name;

    public AsyncCommand SelectCommand { get; }
}
