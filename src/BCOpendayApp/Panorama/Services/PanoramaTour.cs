using System.Globalization;
using System.Text.Json.Nodes;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// One scene of the combined tour: from the bundled tour.json, an online view (Firestore), or a panorama saved on
/// this phone. <see cref="Draft"/> carries the size/position metadata for online and phone scenes.
/// </summary>
public sealed record PanoramaScene(
    string Id,
    string Title,
    PanoramaNode Node,
    JsonObject BundledConfig,
    string? BundledImagePath,
    PanoramaDraft? Draft,
    bool IsOnPhone = false,
    PanoramaManifest? Online = null)
{
    /// <summary>The image file is in this phone's panorama folder (captured or imported here).</summary>
    public bool IsLocal => IsOnPhone;
}

/// <summary>
/// Combines the bundled tour (Resources/Raw/pano/tour.json) with the panoramas on this phone into one set of
/// scenes, and builds the Pannellum configuration for ONE scene at a time (images are inlined as base64, so
/// loading every scene at once would not fit in the WebView's memory).
///
/// Arrows:
///  - hand-made hotspots in tour.json that point at another scene ("sceneId") are kept;
///  - every scene with a GPS position and a known north direction also gets automatic arrows to its
///    neighbours (<see cref="PanoramaLinks"/>), so a new panorama is linked as soon as it is captured.
/// Arrows carry "bcTarget"/"bcTargetYaw"; the page's script turns them into a click that loads the target.
/// Bundled scenes join the automatic links when tour.json gives them "lat", "lng" and "northOffset".
/// Pure logic (System.Text.Json only) - unit-tested in the console test project.
/// </summary>
public sealed class PanoramaTour
{
    public const double DefaultViewHfov = 90;
    public const double DefaultCapturedViewHfov = 75;

    private readonly Dictionary<string, PanoramaScene> byId;

    private PanoramaTour(IReadOnlyList<PanoramaScene> scenes, string? firstSceneId)
    {
        Scenes = scenes;
        byId = scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
        FirstSceneId = firstSceneId is not null && byId.ContainsKey(firstSceneId) ? firstSceneId : scenes.FirstOrDefault()?.Id;
    }

    public IReadOnlyList<PanoramaScene> Scenes { get; }

    public string? FirstSceneId { get; }

    public static PanoramaTour Create(
        JsonObject? bundledTour,
        IEnumerable<PanoramaDraft> drafts,
        IEnumerable<PanoramaManifest>? online = null)
    {
        var scenes = new List<PanoramaScene>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? first = null;

        if (bundledTour?["scenes"] is JsonObject bundledScenes)
        {
            first = (bundledTour["default"] as JsonObject)?["firstScene"] is JsonValue firstValue
                && firstValue.TryGetValue<string>(out var firstId) ? firstId : null;

            foreach (var (id, node) in bundledScenes)
            {
                if (node is not JsonObject config || string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    continue;
                }

                var title = Text(config["title"]) is { Length: > 0 } t ? t : id;
                var location = Number(config["lat"]) is { } lat && Number(config["lng"]) is { } lng
                    ? new GeoPoint(lat, lng)
                    : (GeoPoint?)null;
                var north = Number(config["northOffset"]);
                var image = Text(config["panorama"]);
                scenes.Add(new PanoramaScene(id, title, new PanoramaNode(id, title, location, north), config, image, null));
            }
        }

        // Online views (uploaded by staff, visible to every visitor).
        foreach (var manifest in online ?? Enumerable.Empty<PanoramaManifest>())
        {
            if (ids.Add(manifest.Id))
            {
                scenes.Add(FromDraft(manifest.ToDraft(), isOnPhone: false, manifest));
            }
        }

        // Views on this phone. One that is also online uses the phone's own file (no download needed).
        foreach (var draft in drafts)
        {
            if (ids.Add(draft.Id))
            {
                scenes.Add(FromDraft(draft, isOnPhone: true, null));
                continue;
            }

            var index = scenes.FindIndex(scene => scene.Id == draft.Id && scene.Online is not null);
            if (index >= 0)
            {
                scenes[index] = FromDraft(draft, isOnPhone: true, scenes[index].Online);
            }
        }

        return new PanoramaTour(scenes, first);
    }

    private static PanoramaScene FromDraft(PanoramaDraft draft, bool isOnPhone, PanoramaManifest? online)
    {
        var location = draft.HasLocation ? new GeoPoint(draft.Latitude, draft.Longitude) : (GeoPoint?)null;
        double? north = draft.HasNorthOffset ? draft.NorthOffsetDegrees : null;
        return new PanoramaScene(
            draft.Id,
            draft.DisplayName,
            new PanoramaNode(draft.Id, draft.DisplayName, location, north),
            new JsonObject(),
            null,
            draft,
            isOnPhone,
            online);
    }

    /// <summary>The scene with this id, or the tour's first scene for an empty id. Null if unknown.</summary>
    public PanoramaScene? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? FirstSceneId is { } first ? byId[first] : null
            : byId.GetValueOrDefault(id);

    /// <summary>All arrows leaving a scene: the hand-made ones, then automatic ones to scenes not already linked.</summary>
    public IReadOnlyList<JsonObject> Hotspots(PanoramaScene scene)
    {
        var hotspots = new List<JsonObject>();
        var linked = new HashSet<string>(StringComparer.Ordinal);

        if (scene.BundledConfig["hotSpots"] is JsonArray manual)
        {
            foreach (var spot in manual.OfType<JsonObject>())
            {
                var copy = spot.DeepClone().AsObject();
                if (Text(copy["sceneId"]) is { Length: > 0 } target)
                {
                    copy.Remove("sceneId");
                    if (!byId.ContainsKey(target))
                    {
                        continue; // points at a scene that no longer exists
                    }

                    copy["bcTarget"] = target;
                    if (Number(copy["targetYaw"]) is { } targetYaw)
                    {
                        copy["bcTargetYaw"] = targetYaw;
                    }

                    linked.Add(target);
                }

                hotspots.Add(copy);
            }
        }

        var arrowPitch = Math.Max(PanoramaLinks.ArrowPitchDegrees, -VerticalFov(scene) / 2 + 4);
        foreach (var link in PanoramaLinks.For(scene.Node, Scenes.Select(s => s.Node)))
        {
            if (!linked.Add(link.TargetId))
            {
                continue;
            }

            var spot = new JsonObject
            {
                ["pitch"] = Math.Round(arrowPitch, 2),
                ["yaw"] = Math.Round(link.Yaw, 2),
                ["type"] = "scene",
                ["text"] = PanoramaLinks.Label(link),
                ["cssClass"] = "pnlm-hotspot pnlm-sprite pnlm-scene bc-auto-link",
                ["bcTarget"] = link.TargetId,
            };

            // Arrive facing the direction you walked, like Street View.
            if (byId[link.TargetId].Node.NorthOffsetDegrees is { } targetNorth)
            {
                spot["bcTargetYaw"] = Math.Round(PanoramaLinks.YawFor(link.BearingDegrees, targetNorth), 2);
            }

            hotspots.Add(spot);
        }

        return hotspots;
    }

    /// <summary>Pannellum configuration for one scene, with its image already inlined as a data URI.</summary>
    public string BuildViewerConfig(PanoramaScene scene, string panoramaDataUri, double? initialYaw)
    {
        JsonObject config;
        if (scene.Draft is { } draft)
        {
            config = new JsonObject
            {
                ["type"] = "equirectangular",
                ["title"] = scene.Title,
                ["haov"] = 360,
                ["vaov"] = Math.Round(draft.ViewerVerticalFov, 3),
                ["vOffset"] = 0,
                ["hfov"] = draft.IsFullSphere ? DefaultViewHfov : DefaultCapturedViewHfov,
                ["yaw"] = 0,
                ["pitch"] = 0,
            };

            if (!draft.IsFullSphere)
            {
                // A captured strip is only ~50-70° tall, but a portrait phone (and each eye in VR) shows more than
                // that vertically. Pannellum's avoidShowingBackground cannot fit such a strip and ends up zoomed into
                // a few pixels, flickering as the phone moves, so the space above and below is simply left dark.
                // minHfov lets visitors still zoom in far enough to fill the screen if they want.
                config["minHfov"] = 20;
            }
        }
        else
        {
            config = scene.BundledConfig.DeepClone().AsObject();
            config.Remove("lat");
            config.Remove("lng");
        }

        config["panorama"] = panoramaDataUri;
        if (scene.Node.NorthOffsetDegrees is { } north)
        {
            config["northOffset"] = Math.Round(north, 2);
            config["compass"] = true;
        }

        if (initialYaw is { } yaw && double.IsFinite(yaw))
        {
            config["yaw"] = Math.Round(GeoMath.AngleDifference(yaw, 0), 2);
        }

        var spots = new JsonArray();
        foreach (var spot in Hotspots(scene))
        {
            spots.Add(spot);
        }

        config["hotSpots"] = spots;

        var root = new JsonObject
        {
            ["default"] = new JsonObject
            {
                ["firstScene"] = scene.Id,
                ["autoLoad"] = true,
                ["sceneFadeDuration"] = 0,
                ["showFullscreenCtrl"] = false,
                ["hotSpotDebug"] = false,
            },
            ["scenes"] = new JsonObject { [scene.Id] = config },
        };

        return PanoramaViewerConfig.ToSafeScriptJson(root);
    }

    private static double VerticalFov(PanoramaScene scene) =>
        scene.Draft?.ViewerVerticalFov ?? Number(scene.BundledConfig["vaov"]) ?? 180;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number) && double.IsFinite(number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && double.IsFinite(number)
                ? number
                : null;
    }
}
