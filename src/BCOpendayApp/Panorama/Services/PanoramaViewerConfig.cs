using System.Text.Json.Nodes;

namespace BCOpendayApp.Panorama.Services;

/// <summary>Builds the Pannellum configuration for one validated local full-sphere scene.</summary>
public static class PanoramaViewerConfig
{
    public const double FullSphereHorizontalFov = 360;
    public const double FullSphereVerticalFov = 180;
    public const double DefaultViewHorizontalFov = 90;

    public static string BuildFullSphereTourJson(string title, string panoramaDataUri)
    {
        var root = new JsonObject
        {
            ["default"] = new JsonObject
            {
                ["firstScene"] = "local",
                ["autoLoad"] = true,
                ["sceneFadeDuration"] = 0,
                ["showFullscreenCtrl"] = false,
                ["hotSpotDebug"] = false,
            },
            ["scenes"] = new JsonObject
            {
                ["local"] = new JsonObject
                {
                    ["type"] = "equirectangular",
                    ["title"] = string.IsNullOrWhiteSpace(title) ? "Local panorama" : title,
                    ["panorama"] = panoramaDataUri,
                    ["haov"] = FullSphereHorizontalFov,
                    ["vaov"] = FullSphereVerticalFov,
                    ["vOffset"] = 0,
                    ["hfov"] = DefaultViewHorizontalFov,
                    ["yaw"] = 0,
                    ["pitch"] = 0,
                    ["hotSpots"] = new JsonArray(),
                },
            },
        };

        return ToSafeScriptJson(root);
    }

    /// <summary>Turns off Pannellum's debug hotspot markers for user-facing tours.</summary>
    public static void DisableDebugOverlays(JsonObject root)
    {
        if (root["default"] is JsonObject defaults)
        {
            defaults["hotSpotDebug"] = false;
        }
    }

    public static string ToSafeScriptJson(JsonObject root) =>
        root.ToJsonString().Replace("</", "<\\/", StringComparison.Ordinal);
}
