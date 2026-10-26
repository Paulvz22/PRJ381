using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.AR.Sensors;
using BCOpendayApp.Infrastructure;
using BCOpendayApp.Panorama.Capture;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Scanner.Services;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;
using BCOpendayApp.ViewModels;
using Microsoft.Maui.Storage;

namespace BCOpendayApp.Panorama.ViewModels;

/// <summary>
/// Staff-only: capture a new 360° view with the phone camera. The person stands still on one spot and turns
/// on the spot; the screen tells them which way to turn and takes each photo automatically when the phone
/// points at the next target and is held steady. Meanwhile GPS readings are averaged for the panorama's map
/// position. When the ring is complete the photos are stitched on the phone (<see cref="PanoramaStitcher"/>)
/// and saved as a local panorama, which immediately gets arrows to nearby panoramas in the viewer.
/// Requires the same staff sign-in as the building scanner.
/// </summary>
public sealed class PanoramaCaptureViewModel : ViewModelBase
{
    private static readonly TimeSpan SteadyWindow = TimeSpan.FromMilliseconds(450);
    private const double SteadySpreadDegrees = 1.2;
    private const double MinCoverageToSave = 0.97;

    private readonly IScannerAuthService auth;
    private readonly IArSensorService sensors;
    private readonly PanoramaDraftStore store;
    private readonly PanoramaOnlineLibrary online;
    private readonly ICampusLocationService campus;
    private readonly LocationSampler sampler = new();
    private readonly SteadinessTracker steadiness = new(SteadyWindow, SteadySpreadDegrees);
    private readonly List<(int Index, StitchFrame Frame)> frames = new();

    private (double FocalOverShort, double FocalOverLong) optics;
    private int sensorOrientation = 90;
    private PanoramaCapturePlan? plan;
    private double horizontalFov;
    private bool sensorsRunning;
    private bool startedTrueNorth;
    private bool isCapturingPhoto;
    private int session;
    private string name = string.Empty;
    private string description = string.Empty;
    private IReadOnlyList<CampusLocation> existingLocations = Array.Empty<CampusLocation>();
    private IReadOnlyList<string> locationChoices = new[] { NewLocationChoice };
    private int selectedLocationIndex;
    private string gpsText = "Waiting for GPS...";
    private string guidanceText = string.Empty;
    private string turnText = string.Empty;
    private string progressText = string.Empty;
    private double progress;
    private bool isCapturing;
    private bool isStitching;
    private string stitchText = string.Empty;

    public PanoramaCaptureViewModel(
        IScannerAuthService auth,
        IArSensorService sensors,
        PanoramaDraftStore store,
        PanoramaOnlineLibrary online,
        ICampusLocationService campus,
        INavigationService navigation,
        IDialogService dialogs)
        : base(navigation, dialogs)
    {
        this.auth = auth;
        this.sensors = sensors;
        this.store = store;
        this.online = online;
        this.campus = campus;

        StartCommand = CreateCommand(StartAsync);
        CaptureNowCommand = CreateCommand(() => TryCaptureAsync(force: true));
        CancelCommand = CreateCommand(CancelAsync);
    }

    /// <summary>Set by the page: takes one photo with the camera and returns the JPEG bytes.</summary>
    public Func<CancellationToken, Task<byte[]>>? CapturePhoto { get; set; }

    /// <summary>Set by the page once the camera preview is running.</summary>
    public bool IsCameraReady { get; set; }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value ?? string.Empty);
    }

    public const string NewLocationChoice = "New location (named after this view)";

    /// <summary>Short description of the place, shown on its location page.</summary>
    public string Description
    {
        get => description;
        set => SetProperty(ref description, value ?? string.Empty);
    }

    /// <summary>"New location" first, then every existing location (AR buildings and earlier 360° views).</summary>
    public IReadOnlyList<string> LocationChoices
    {
        get => locationChoices;
        private set => SetProperty(ref locationChoices, value);
    }

    public int SelectedLocationIndex
    {
        get => selectedLocationIndex;
        set => SetProperty(ref selectedLocationIndex, Math.Max(0, value));
    }

    public string GpsText
    {
        get => gpsText;
        private set => SetProperty(ref gpsText, value);
    }

    public string GuidanceText
    {
        get => guidanceText;
        private set => SetProperty(ref guidanceText, value);
    }

    /// <summary>A big arrow showing which way to turn.</summary>
    public string TurnText
    {
        get => turnText;
        private set => SetProperty(ref turnText, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public double Progress
    {
        get => progress;
        private set => SetProperty(ref progress, value);
    }

    public bool IsCapturing
    {
        get => isCapturing;
        private set
        {
            if (SetProperty(ref isCapturing, value))
            {
                OnPropertiesChanged(nameof(IsSetup));
            }
        }
    }

    public bool IsStitching
    {
        get => isStitching;
        private set
        {
            if (SetProperty(ref isStitching, value))
            {
                OnPropertiesChanged(nameof(IsSetup));
            }
        }
    }

    public bool IsSetup => !isCapturing && !isStitching;

    public string StitchText
    {
        get => stitchText;
        private set => SetProperty(ref stitchText, value);
    }

    public AsyncCommand StartCommand { get; }

    public AsyncCommand CaptureNowCommand { get; }

    public AsyncCommand CancelCommand { get; }

    /// <summary>Called by the page after the permission prompts. Returns false when the page must close.</summary>
    public async Task<bool> PrepareAsync(bool locationAllowed)
    {
        if (!auth.IsUnlocked)
        {
            await Dialogs.AlertAsync("Staff only", "Sign in on the staff screen first. Your session may have ended.");
            await Navigation.GoBackAsync();
            return false;
        }

        if (!PanoramaFrameCodec.IsSupported)
        {
            await Dialogs.AlertAsync("Not available", "Capturing 360° views is only available on Android phones.");
            await Navigation.GoBackAsync();
            return false;
        }

        _ = LoadLocationChoicesAsync();
        optics = ArPlatform.BackCameraOptics();
        sensorOrientation = ArPlatform.BackCameraSensorOrientation();
        ResetCapture();

        if (!sensorsRunning)
        {
            sensors.PoseChanged += OnPoseChanged;
            sensors.PositionChanged += OnPositionChanged;
            sensorsRunning = true;

            // Always started: the compass is needed even when GPS is unavailable.
            var gpsStarted = await sensors.StartAsync();
            if (!locationAllowed || !gpsStarted)
            {
                GpsText = locationAllowed
                    ? "GPS is off - the panorama will get no map position or arrows."
                    : "Location permission is off - the panorama will get no map position or arrows.";
            }
        }

        if (sensors.RawFix is { } fix)
        {
            OnFix(fix);
        }

        return true;
    }

    private async Task LoadLocationChoicesAsync()
    {
        try
        {
            var list = await campus.GetAllAsync();
            existingLocations = list.Locations;
            LocationChoices = new[] { NewLocationChoice }.Concat(existingLocations.Select(location => location.Name)).ToList();
            SelectedLocationIndex = 0;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Locations not loaded for the picker: {exception.Message}");
        }
    }

    /// <summary>The chosen existing location, or a new id made from the view's name.</summary>
    private string ChosenLocationId() =>
        selectedLocationIndex >= 1 && selectedLocationIndex <= existingLocations.Count
            ? existingLocations[selectedLocationIndex - 1].Id
            : CampusLocationCatalog.NewLocationId(Name, existingLocations.Select(location => location.Id));

    public override void OnDisappearing()
    {
        session++;
        if (sensorsRunning)
        {
            sensorsRunning = false;
            sensors.PoseChanged -= OnPoseChanged;
            sensors.PositionChanged -= OnPositionChanged;
            sensors.Stop();
        }

        if (!isStitching)
        {
            ResetCapture();
        }
    }

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await Dialogs.AlertAsync("Name needed", "Give the view a name first, e.g. \"Library entrance\".");
            return;
        }

        if (!IsCameraReady || CapturePhoto is null)
        {
            await Dialogs.AlertAsync("Camera not ready", "Wait for the camera picture to appear, then tap Start again.");
            return;
        }

        if (sensors.Pose is not { } pose)
        {
            await Dialogs.AlertAsync("No compass yet", "Hold the phone upright for a moment so the compass can settle.");
            return;
        }

        if (sensors.RawFix is null
            && !await Dialogs.ConfirmAsync(
                "No GPS yet",
                "Without GPS this view gets no map position and no arrows to other views. Wait outdoors for GPS, or start anyway?",
                "Start anyway",
                "Wait"))
        {
            return;
        }

        // Photos are portrait, so their width is the sensor's short side.
        horizontalFov = PanoramaStitcher.HorizontalFovDegrees(optics.FocalOverShort, 1);
        plan = new PanoramaCapturePlan(horizontalFov, pose.Heading);
        frames.Clear();

        // Average GPS only over the time spent standing on this spot.
        sampler.Clear();
        if (sensors.RawFix is { } fix)
        {
            OnFix(fix);
        }

        startedTrueNorth = sensors.IsTrueNorth;
        steadiness.Reset();
        IsCapturing = true;
        UpdateProgress();
        GuidanceText = "Hold still - taking the first photo";
    }

    private async Task CancelAsync()
    {
        if (IsCapturing && frames.Count > 0
            && !await Dialogs.ConfirmAsync("Stop capturing?", "The photos taken so far will be discarded.", "Discard", "Keep going"))
        {
            return;
        }

        ResetCapture();
    }

    private void ResetCapture()
    {
        plan = null;
        frames.Clear();
        isCapturingPhoto = false;
        IsCapturing = false;
        GuidanceText = "Stand on the spot for the view, hold the phone upright at eye level and tap Start. Then turn slowly on the spot - photos are taken automatically.";
        TurnText = string.Empty;
        ProgressText = string.Empty;
        Progress = 0;
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (sensors.RawFix is { } fix)
        {
            OnFix(fix);
        }
    }

    private void OnFix((GeoPoint Point, double AccuracyMeters) fix)
    {
        // The person stays on one spot for the whole capture, so every fix helps the average.
        sampler.Add(fix.Point, fix.AccuracyMeters);
        var averaged = sampler.Result();
        GpsText = averaged is { } result
            ? $"GPS ±{result.AccuracyMeters:0} m from {sampler.Count} readings{(result.AccuracyMeters > 15 ? " - move away from walls/trees if you can" : string.Empty)}"
            : $"GPS ±{fix.AccuracyMeters:0} m";
    }

    private void OnPoseChanged(object? sender, EventArgs e)
    {
        if (!IsCapturing || plan is null || sensors.Pose is not { } pose)
        {
            return;
        }

        if (sensors.IsTrueNorth != startedTrueNorth)
        {
            // The first GPS fix just arrived and switched the compass from magnetic to true north. Photos taken
            // before and after would not line up, so start the ring again.
            plan = new PanoramaCapturePlan(horizontalFov, pose.Heading);
            frames.Clear();
            startedTrueNorth = sensors.IsTrueNorth;
            UpdateProgress();
            GuidanceText = "GPS found - starting again so the photos line up. Hold still.";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        steadiness.Add(now, pose.Heading, pose.Pitch);
        if (plan.Guide(pose.Heading, pose.Pitch) is not { } guide)
        {
            return;
        }

        if (!guide.IsAligned)
        {
            if (Math.Abs(guide.TurnDegrees) > PanoramaCapturePlan.HeadingToleranceDegrees)
            {
                TurnText = guide.TurnDegrees > 0 ? "➜" : "⬅";
                GuidanceText = $"Turn {(guide.TurnDegrees > 0 ? "right" : "left")} {Math.Abs(guide.TurnDegrees):0}°";
            }
            else
            {
                TurnText = guide.TiltDegrees > 0 ? "⬆" : "⬇";
                GuidanceText = guide.TiltDegrees > 0 ? "Tilt the phone up a little - aim at the horizon" : "Tilt the phone down a little - aim at the horizon";
            }

            return;
        }

        TurnText = "●";
        if (!steadiness.IsSteady(now))
        {
            GuidanceText = "Hold still...";
            return;
        }

        _ = TryCaptureAsync(force: false);
    }

    /// <summary>Takes the next photo. <paramref name="force"/>: the "Take photo now" button (skips the steadiness check).</summary>
    private async Task TryCaptureAsync(bool force)
    {
        if (isCapturingPhoto || plan is null || !IsCapturing || CapturePhoto is null || sensors.Pose is not { } before)
        {
            return;
        }

        var guide = plan.Guide(before.Heading, before.Pitch);
        if (guide is null || (!force && !guide.Value.IsAligned))
        {
            return;
        }

        if (force && Math.Abs(guide.Value.TurnDegrees) > plan.StepDegrees / 2)
        {
            await Dialogs.AlertAsync("Not there yet", $"Turn {(guide.Value.TurnDegrees > 0 ? "right" : "left")} {Math.Abs(guide.Value.TurnDegrees):0}° first.");
            return;
        }

        isCapturingPhoto = true;
        var mySession = session;
        var currentPlan = plan;
        GuidanceText = "Taking photo - hold still";
        try
        {
            var jpeg = await CapturePhoto(CancellationToken.None);
            var after = sensors.Pose ?? before;
            if (mySession != session || !ReferenceEquals(currentPlan, plan) || !IsCapturing)
            {
                return; // cancelled or restarted while the camera was busy
            }

            // The photo was taken between the two readings: use their average direction.
            var heading = GeoMath.NormalizeDegrees(before.Heading + GeoMath.AngleDifference(after.Heading, before.Heading) / 2);
            var pitch = (before.Pitch + after.Pitch) / 2;
            var orientation = sensorOrientation;
            var decoded = await Task.Run(() => PanoramaFrameCodec.DecodePortrait(jpeg, orientation));
            if (mySession != session || !ReferenceEquals(currentPlan, plan))
            {
                return;
            }

            frames.RemoveAll(f => f.Index == guide.Value.TargetIndex);
            frames.Add((guide.Value.TargetIndex, new StitchFrame(decoded.Pixels, decoded.Width, decoded.Height, heading, pitch)));
            plan.MarkTaken(guide.Value.TargetIndex);
            steadiness.Reset();
            TryVibrate();
            UpdateProgress();

            if (plan.IsComplete)
            {
                await StitchAndSaveAsync();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            GuidanceText = "The photo failed - hold still, it will be retried.";
        }
        finally
        {
            isCapturingPhoto = false;
        }
    }

    private void UpdateProgress()
    {
        if (plan is null)
        {
            return;
        }

        ProgressText = $"Photo {plan.TakenCount} of {plan.Count}";
        Progress = plan.TakenCount / (double)plan.Count;
    }

    private async Task StitchAndSaveAsync()
    {
        var mySession = session;
        IsCapturing = false;
        IsStitching = true;
        StitchText = "Joining the photos into a 360° view... (this takes a few seconds)";
        try
        {
            var shots = frames.OrderBy(f => f.Index).Select(f => f.Frame).ToList();
            var focal = optics.FocalOverShort * shots[0].Width;
            var tempPath = Path.Combine(FileSystem.Current.CacheDirectory, "panorama-" + Guid.NewGuid().ToString("N") + ".jpg");

            var result = await Task.Run(() =>
            {
                var stitched = PanoramaStitcher.Stitch(shots, new StitchOptions(focal));
                PanoramaFrameCodec.SaveJpeg(stitched.Pixels, stitched.Width, stitched.Height, tempPath);
                return stitched;
            });

            if (result.CoverageFraction < MinCoverageToSave
                && !await Dialogs.ConfirmAsync(
                    "Gaps in the view",
                    $"Only {result.CoverageFraction:P0} of the circle is covered (a photo was probably taken while turning). Save it anyway?",
                    "Save",
                    "Discard"))
            {
                File.Delete(tempPath);
                ResetCapture();
                return;
            }

            var location = sampler.Result();
            var template = new PanoramaDraft
            {
                DisplayName = Name.Trim(),
                LocationId = ChosenLocationId(),
                Description = Description.Trim(),
                Width = result.Width,
                Height = result.Height,
                VerticalFovDegrees = result.VerticalFovDegrees,
                PhotoCount = shots.Count,
                HasLocation = location is not null,
                Latitude = location?.Point.Lat ?? 0,
                Longitude = location?.Point.Lng ?? 0,
                LocationAccuracyMeters = location?.AccuracyMeters ?? 0,

                // The image is laid out by compass heading (centre column = north), so yaw 0 IS north - when
                // the compass was corrected to true north with the GPS position.
                HasNorthOffset = startedTrueNorth,
                NorthOffsetDegrees = 0,
            };

            var draft = await store.SaveCapturedAsync(tempPath, template);

            // Put it online straight away so every visitor gets it; if that fails it stays on this phone and can
            // be uploaded later from the 360° views list.
            var uploaded = false;
            if (online.IsConfigured && auth.IsUnlocked && auth.StaffUid is { Length: > 0 } uid)
            {
                StitchText = "Uploading the view for visitors...";
                try
                {
                    var image = await File.ReadAllBytesAsync(store.GetImagePath(draft));
                    await online.UploadAsync(draft, image, uid, new Progress<double>(p => StitchText = $"Uploading the view for visitors... {p:P0}"));
                    uploaded = true;
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine($"Panorama upload failed: {exception}");
                }
            }

            if (mySession != session)
            {
                return; // the page was left; the panorama is saved and appears in the list
            }

            ResetCapture();
            Name = string.Empty;
            Description = string.Empty;
            sampler.Clear();
            var aligned = result.RefinedPairs > 0 ? $" {result.RefinedPairs} of {shots.Count} joins were aligned from the photos themselves." : string.Empty;
            var where = uploaded
                ? "It is online - every visitor sees it the next time they open the 360° tour."
                : "It is saved on this phone only (no connection or Firebase not set up) - use Upload in the 360° views list later.";
            await Dialogs.AlertAsync(
                "360° view saved",
                where + " "
                + (draft.HasLocation
                    ? "It has a GPS position, so arrows to nearby views appear automatically."
                    : "It has no GPS position, so it gets no arrows to other views.")
                + aligned);
            await Navigation.GoToStopAsync(Routes.Pano, draft.Id);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await Dialogs.AlertAsync("Could not create the panorama", "Something went wrong while joining the photos. Please capture it again.\n\n" + exception.Message);
            ResetCapture();
        }
        finally
        {
            IsStitching = false;
            frames.Clear();
        }
    }

    private static void TryVibrate()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }
}
