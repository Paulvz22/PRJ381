using System.Text.Json;
using System.Text.Json.Nodes;
using BCOpendayApp.AR.Data;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

/// <summary>
/// Panoramas live on the phone that captured them. To ship one to every visitor it is exported as two files -
/// the image and a ready-made tour.json scene - which a developer drops into Resources/Raw/pano (see
/// docs/panoramas.md). The scene keeps the GPS position and north direction, so bundled scenes get the same
/// automatic arrows. Pure logic - unit-tested.
/// </summary>
public static class PanoramaExport
{
    /// <summary>A safe scene id / file name from the panorama's name, e.g. "library-entrance".</summary>
    public static string SceneId(PanoramaDraft draft) => BuildingValidation.SlugFromName(draft.DisplayName) is var slug
        && slug != "building" ? slug : "view-" + draft.Id[..8];

    /// <summary>{ "scene-id": { ...Pannellum scene for tour.json... } }</summary>
    public static string SceneJson(PanoramaDraft draft)
    {
        var id = SceneId(draft);
        var scene = new JsonObject
        {
            ["type"] = "equirectangular",
            ["title"] = draft.DisplayName,
            ["panorama"] = $"scenes/{id}{(draft.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg")}",
            ["haov"] = 360,
            ["vaov"] = Math.Round(draft.ViewerVerticalFov, 3),
            ["vOffset"] = 0,
            ["hfov"] = draft.IsFullSphere ? PanoramaTour.DefaultViewHfov : PanoramaTour.DefaultCapturedViewHfov,
            ["yaw"] = 0,
            ["pitch"] = 0,
        };

        if (!draft.IsFullSphere)
        {
            scene["minHfov"] = 20; // same as the in-app viewer: see PanoramaTour.BuildViewerConfig
        }

        if (draft.HasLocation)
        {
            scene["lat"] = Math.Round(draft.Latitude, 7);
            scene["lng"] = Math.Round(draft.Longitude, 7);
        }

        if (draft.HasNorthOffset)
        {
            scene["northOffset"] = Math.Round(draft.NorthOffsetDegrees, 2);
        }

        scene["hotSpots"] = new JsonArray();
        var root = new JsonObject { [id] = scene };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
