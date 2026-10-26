using System.Text.Json.Nodes;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Panorama.Capture;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;

/// <summary>
/// Tests for in-app 360° capture: automatic arrows between panoramas, the combined tour/viewer config, the
/// export format, capture guidance and the stitcher (on a synthetic textured world with known answers).
/// </summary>
static class PanoramaCaptureTests
{
    private static readonly GeoPoint Origin = new(-25.7545, 28.1879);

    public static void Register(Action<string, Func<Task>> add)
    {
        // ------------------------------------------------------------ automatic arrows

        add("PANO LINKS: neighbours get arrows at their real compass direction", () =>
        {
            var north = At(Origin, 0, 40);
            var east = At(Origin, 90, 25);
            var nodes = new[] { Node("a", Origin, 0), Node("n", north, 0), Node("e", east, 0) };
            var links = PanoramaLinks.For(nodes[0], nodes);
            Check(links.Count == 2);
            Check(links[0].TargetId == "e" && Near(links[0].Yaw, 90, 0.5) && Near(links[0].DistanceMeters, 25, 0.5));
            Check(links[1].TargetId == "n" && Near(links[1].Yaw, 0, 0.5));
            return Task.CompletedTask;
        });

        add("PANO LINKS: the panorama's north offset turns the arrow yaw", () =>
        {
            var nodes = new[] { Node("a", Origin, 30), Node("e", At(Origin, 90, 25), 0) };
            Check(Near(PanoramaLinks.For(nodes[0], nodes).Single().Yaw, 60, 0.5));
            Check(Near(PanoramaLinks.YawFor(10, 350), 20, 1e-9));
            Check(Near(PanoramaLinks.YawFor(350, 10), -20, 1e-9));
            return Task.CompletedTask;
        });

        add("PANO LINKS: a farther view in the same direction is reached through the nearer one", () =>
        {
            var nodes = new[]
            {
                Node("a", Origin, 0),
                Node("near", At(Origin, 0, 20), 0),
                Node("far", At(Origin, 10, 45), 0),     // 10° away from "near": hidden behind it
                Node("side", At(Origin, 60, 45), 0),    // a different direction: kept
            };
            var ids = PanoramaLinks.For(nodes[0], nodes).Select(l => l.TargetId).ToList();
            Check(ids.SequenceEqual(new[] { "near", "side" }));
            return Task.CompletedTask;
        });

        add("PANO LINKS: too far, same spot, unknown direction and no GPS give no arrows", () =>
        {
            var nodes = new[]
            {
                Node("a", Origin, 0),
                Node("far", At(Origin, 0, 151), 0),
                Node("same", At(Origin, 0, 1), 0),
                Node("nogps", null, 0),
            };
            Check(PanoramaLinks.For(nodes[0], nodes).Count == 0);

            var unknownNorth = new PanoramaNode("u", "U", Origin, null);
            Check(PanoramaLinks.For(unknownNorth, new[] { unknownNorth, Node("b", At(Origin, 0, 20), 0) }).Count == 0);

            // ...but a scene with unknown direction can still be linked TO.
            var toUnknown = PanoramaLinks.For(Node("b", At(Origin, 0, 20), 0), new[] { unknownNorth, Node("b", At(Origin, 0, 20), 0) });
            Check(toUnknown.Single().TargetId == "u");
            return Task.CompletedTask;
        });

        add("PANO LINKS: at most 6 arrows, labels use metres", () =>
        {
            var nodes = new List<PanoramaNode> { Node("a", Origin, 0) };
            for (var i = 0; i < 10; i++)
            {
                nodes.Add(Node("n" + i, At(Origin, i * 36, 20 + i), 0));
            }

            var links = PanoramaLinks.For(nodes[0], nodes);
            Check(links.Count == PanoramaLinks.MaxLinksPerScene);
            Check(PanoramaLinks.Label(links[0]) == "N0 · 20 m");
            Check(PanoramaLinks.Label(links[0] with { DistanceMeters = 7.25 }) == "N0 · 7.3 m"
                || PanoramaLinks.Label(links[0] with { DistanceMeters = 7.25 }) == "N0 · 7.2 m");
            return Task.CompletedTask;
        });

        // ------------------------------------------------------------ combined tour + viewer config

        add("PANO TOUR: bundled scenes and phone panoramas form one tour with arrows", () =>
        {
            var tour = PanoramaTour.Create(BundledTour(), new[] { CapturedDraft("c1", At(Origin, 0, 30)) });
            Check(tour.Scenes.Select(s => s.Id).SequenceEqual(new[] { "spot-1", "spot-2", "gone-link", Id("c1") }));
            Check(tour.FirstSceneId == "spot-1");
            Check(tour.Find(null)!.Id == "spot-1");
            Check(tour.Find(Id("c1"))!.IsLocal);
            Check(tour.Find("nope") is null);

            // spot-1 has GPS + north in tour.json: hand-made arrow to spot-2 kept, automatic arrow to the capture added.
            var spots = tour.Hotspots(tour.Find("spot-1")!);
            Check(spots.Count == 3);
            Check(Text(spots[0]["bcTarget"]) == "spot-2" && spots[0]["sceneId"] is null);
            Check(Num(spots[0]["bcTargetYaw"]) == 180);
            Check(Text(spots[1]["type"]) == "info");                  // info hotspot untouched
            Check(Text(spots[2]["bcTarget"]) == Id("c1"));
            Check(Near(Num(spots[2]["yaw"]), 0 - 20, 0.5));             // bearing 0, spot-1 north offset 20
            Check(Near(Num(spots[2]["bcTargetYaw"]), 0, 0.5));          // arrive facing the walking direction
            Check(Text(spots[2]["text"])!.StartsWith("Library door · 30", StringComparison.Ordinal));

            // spot-2 has no GPS: only its hand-made arrow. The arrow to a removed scene is dropped.
            Check(tour.Hotspots(tour.Find("spot-2")!).Count == 1);
            Check(tour.Hotspots(tour.Find("gone-link")!).Count == 0);

            // The capture links back to spot-1 (bearing 180 from it).
            var back = tour.Hotspots(tour.Find(Id("c1"))!).Single();
            Check(Text(back["bcTarget"]) == "spot-1" && (Near(Num(back["yaw"]), 180, 0.5) || Near(Num(back["yaw"]), -180, 0.5)));
            return Task.CompletedTask;
        });

        add("PANO TOUR: hand-made arrow wins over an automatic one to the same scene", () =>
        {
            var json = JsonNode.Parse("""
                {"default":{"firstScene":"a"},"scenes":{
                  "a":{"title":"A","panorama":"scenes/a.jpg","lat":-25.7545,"lng":28.1879,"northOffset":0,
                       "hotSpots":[{"pitch":0,"yaw":5,"type":"scene","text":"to B","sceneId":"b"}]},
                  "b":{"title":"B","panorama":"scenes/b.jpg","lat":-25.7542,"lng":28.1879,"northOffset":0}}}
                """)!.AsObject();
            var tour = PanoramaTour.Create(json, Array.Empty<PanoramaDraft>());
            var spots = tour.Hotspots(tour.Find("a")!);
            Check(spots.Count == 1 && Num(spots[0]["yaw"]) == 5);
            return Task.CompletedTask;
        });

        add("PANO TOUR: viewer config for a captured strip", () =>
        {
            var tour = PanoramaTour.Create(BundledTour(), new[] { CapturedDraft("c1", At(Origin, 0, 30)) });
            var scene = tour.Find(Id("c1"))!;
            var root = JsonNode.Parse(tour.BuildViewerConfig(scene, "data:image/jpeg;base64,AAAA", initialYaw: 200))!.AsObject();
            Check(Text(root["default"]!["firstScene"]) == Id("c1"));
            Check((bool)root["default"]!["hotSpotDebug"]! == false);
            var config = root["scenes"]![Id("c1")]!.AsObject();
            Check(Text(config["panorama"]) == "data:image/jpeg;base64,AAAA");
            Check(Num(config["haov"]) == 360 && Near(Num(config["vaov"]), 62.5, 1e-6));
            Check(config["avoidShowingBackground"] is null && Num(config["minHfov"]) == 20); // strips can't fill a portrait screen
            Check((bool)config["compass"]! && Num(config["northOffset"]) == 0);
            Check(Near(Num(config["yaw"]), -160, 1e-6));               // 200 wrapped into (-180, 180]
            Check(config["hotSpots"]!.AsArray().Count == 1);
            return Task.CompletedTask;
        });

        add("PANO TOUR: bundled scene config keeps its settings but drops lat/lng", () =>
        {
            var tour = PanoramaTour.Create(BundledTour(), Array.Empty<PanoramaDraft>());
            var root = JsonNode.Parse(tour.BuildViewerConfig(tour.Find("spot-1")!, "data:x", null))!.AsObject();
            var config = root["scenes"]!["spot-1"]!.AsObject();
            Check(Num(config["vaov"]) == 44.7 && config["lat"] is null && config["lng"] is null);
            Check(Num(config["northOffset"]) == 20);
            Check(Text(config["panorama"]) == "data:x");
            Check(!tour.BuildViewerConfig(tour.Find("spot-1")!, "</script>", null).Contains("</script>", StringComparison.Ordinal));
            return Task.CompletedTask;
        });

        // ------------------------------------------------------------ captured drafts + export

        add("PANO DRAFT: captured strip metadata validates", () =>
        {
            var draft = CapturedDraft("c1", Origin);
            Check(PanoramaDraftValidation.IsValid(draft));
            Check(draft.IsCaptured && draft.ViewerVerticalFov == 62.5);
            Check(!PanoramaDraftValidation.IsValid(draft with { Height = 1000 }));          // ratio no longer 360/vFOV
            Check(!PanoramaDraftValidation.IsValid(draft with { FileName = draft.Id + ".png" }));
            Check(!PanoramaDraftValidation.IsValid(draft with { IsFullSphere = true }));
            Check(!PanoramaDraftValidation.IsValid(draft with { VerticalFovDegrees = 5, Height = 57 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { Latitude = 95 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { Latitude = 0, Longitude = 0 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { NorthOffsetDegrees = 360 }));
            Check(PanoramaDraftValidation.IsValid(draft with { HasLocation = false, Latitude = 0, Longitude = 0 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { PhotoCount = 0 }));
            return Task.CompletedTask;
        });

        add("PANO EXPORT: a ready-to-paste tour.json scene with position and north", () =>
        {
            var draft = CapturedDraft("c1", Origin) with { DisplayName = "Library door (main)" };
            Check(PanoramaExport.SceneId(draft) == "library-door-main");
            var root = JsonNode.Parse(PanoramaExport.SceneJson(draft))!.AsObject();
            var scene = root["library-door-main"]!.AsObject();
            Check(Text(scene["panorama"]) == "scenes/library-door-main.jpg");
            Check(Near(Num(scene["lat"]), Origin.Lat, 1e-6) && Near(Num(scene["lng"]), Origin.Lng, 1e-6));
            Check(Num(scene["northOffset"]) == 0 && Num(scene["vaov"]) == 62.5);

            // ...and a tour built from that export links it like the original.
            var tour = PanoramaTour.Create(new JsonObject { ["scenes"] = root.DeepClone() }, Array.Empty<PanoramaDraft>());
            Check(tour.Find("library-door-main")!.Node.Location is not null);
            Check(PanoramaExport.SceneId(draft with { DisplayName = "!!!" }) == "view-" + Id("c1")[..8]);
            return Task.CompletedTask;
        });

        // ------------------------------------------------------------ capture guidance

        add("PANO PLAN: photo count gives ~40% overlap, targets start where the phone points", () =>
        {
            var plan = new PanoramaCapturePlan(60, 350);
            Check(plan.Count == 10 && Near(plan.StepDegrees, 36, 1e-9));
            Check(Near(plan.TargetHeading(0), 350, 1e-9) && Near(plan.TargetHeading(1), 26, 1e-9));
            Check(new PanoramaCapturePlan(120, 0).Count == 8);   // minimum
            Check(new PanoramaCapturePlan(10, 0).Count == 24);   // maximum
            return Task.CompletedTask;
        });

        add("PANO PLAN: guidance turns right to the next photo and checks the tilt", () =>
        {
            var plan = new PanoramaCapturePlan(60, 0);
            var first = plan.Guide(1, 0)!.Value;
            Check(first.TargetIndex == 0 && first.IsAligned);
            Check(!plan.Guide(1, 9)!.Value.IsAligned && plan.Guide(1, 9)!.Value.TiltDegrees < 0);

            plan.MarkTaken(0);
            var next = plan.Guide(2, 0)!.Value;
            Check(next.TargetIndex == 1 && Near(next.TurnDegrees, 34, 1e-9) && !next.IsAligned);

            // Already pointing at another remaining target: take that one.
            Check(plan.Guide(108, 0)!.Value.TargetIndex == 3);

            for (var i = 1; i < plan.Count; i++)
            {
                plan.MarkTaken(i);
            }

            Check(plan.IsComplete && plan.Guide(0, 0) is null);
            return Task.CompletedTask;
        });

        add("PANO PLAN: steadiness needs the phone held still for the whole window", () =>
        {
            var tracker = new SteadinessTracker(TimeSpan.FromMilliseconds(400), 1.2);
            var t = DateTimeOffset.UnixEpoch;
            tracker.Add(t, 359.5, 0);
            Check(!tracker.IsSteady(t));
            for (var ms = 50; ms <= 500; ms += 50)
            {
                tracker.Add(t.AddMilliseconds(ms), ms % 100 == 0 ? 0.3 : 359.7, 0.2); // jitter across north
            }

            Check(tracker.IsSteady(t.AddMilliseconds(500)));
            tracker.Add(t.AddMilliseconds(550), 5, 0);
            Check(!tracker.IsSteady(t.AddMilliseconds(550)));
            return Task.CompletedTask;
        });

        // ------------------------------------------------------------ stitcher

        add("PANO STITCH: photos placed by an exact compass reproduce the world", () =>
        {
            var (frames, focal) = RenderRing(Enumerable.Repeat(0.0, 10).ToArray());
            var result = PanoramaStitcher.Stitch(frames, new StitchOptions(focal, RefineWithImages: false));
            Check(result.Width == 652 && result.CoverageFraction == 1);
            Check(result.Height > 100 && Near(result.VerticalFovDegrees, result.Height * 360.0 / result.Width, 1e-9));
            Check(MeanError(result) < 6);
            return Task.CompletedTask;
        });

        add("PANO STITCH: compass errors of a few degrees are corrected from the overlaps", () =>
        {
            var errors = new[] { 3.0, -2.5, 4.0, -3.5, 2.0, 0.5, -4.0, 3.0, -1.5, 1.0 };
            var (frames, focal) = RenderRing(errors);
            var raw = PanoramaStitcher.Stitch(frames, new StitchOptions(focal, RefineWithImages: false));
            var refined = PanoramaStitcher.Stitch(frames, new StitchOptions(focal));
            Check(refined.RefinedPairs == 10);

            // Relative positions are recovered (the absolute direction stays the compass average).
            var mean = errors.Average();
            for (var i = 0; i < frames.Count; i++)
            {
                var truth = GeoMath.NormalizeDegrees(i * 36.0 + mean);
                Check(Math.Abs(GeoMath.AngleDifference(refined.FinalHeadings[i], truth)) < 0.5);
            }

            Check(MeanError(refined, mean) < MeanError(raw, mean) * 0.6);
            return Task.CompletedTask;
        });

        add("PANO STITCH: a missing photo leaves a gap that is reported", () =>
        {
            var (frames, focal) = RenderRing(Enumerable.Repeat(0.0, 10).ToArray());
            frames.RemoveAt(5);
            frames.RemoveAt(4);
            var result = PanoramaStitcher.Stitch(frames, new StitchOptions(focal));
            Check(result.CoverageFraction < 0.97 && result.CoverageFraction > 0.8);
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- helpers

    private static PanoramaNode Node(string id, GeoPoint? at, double? north) =>
        new(id, id.ToUpperInvariant(), at, north);

    private static GeoPoint At(GeoPoint origin, double bearing, double meters) =>
        GeoMath.FromLocal(origin, GeoMath.Direction(bearing) * meters);

    /// <summary>A valid 32-hex-character panorama id starting with the (hex) seed.</summary>
    private static string Id(string seed) => (seed + new string('0', 32))[..32];

    private static PanoramaDraft CapturedDraft(string seed, GeoPoint at)
    {
        var id = Id(seed);
        return new PanoramaDraft
        {
            Id = id,
            DisplayName = "Library door",
            FileName = id + ".jpg",
            Width = 4096,
            Height = 711,                 // 4096 × 62.5 / 360
            Projection = PanoramaDraft.EquirectangularProjection,
            IsFullSphere = false,
            CaptureMethod = PanoramaDraft.InAppCaptureMethod,
            FileSizeBytes = 2_000_000,
            CreatedAtUnixMs = 1_800_000_000_000,
            VerticalFovDegrees = 62.5,
            PhotoCount = 10,
            HasLocation = true,
            Latitude = at.Lat,
            Longitude = at.Lng,
            LocationAccuracyMeters = 4,
            HasNorthOffset = true,
            NorthOffsetDegrees = 0,
        };
    }

    private static JsonObject BundledTour() => JsonNode.Parse("""
        {
          "default": { "firstScene": "spot-1", "hotSpotDebug": true },
          "scenes": {
            "spot-1": { "type": "equirectangular", "title": "Spot 1", "panorama": "scenes/spot-1.jpg",
                        "haov": 360, "vaov": 44.7, "lat": -25.7545, "lng": 28.1879, "northOffset": 20,
                        "hotSpots": [
                          { "pitch": 0, "yaw": 0, "type": "scene", "text": "Next: Spot 2", "sceneId": "spot-2", "targetYaw": 180 },
                          { "pitch": 5, "yaw": 90, "type": "info", "text": "Notice board" } ] },
            "spot-2": { "type": "equirectangular", "title": "Spot 2", "panorama": "scenes/spot-2.jpg", "vaov": 36.8,
                        "hotSpots": [ { "pitch": 0, "yaw": 180, "type": "scene", "text": "Back", "sceneId": "spot-1" } ] },
            "gone-link": { "panorama": "scenes/x.jpg",
                        "hotSpots": [ { "pitch": 0, "yaw": 0, "type": "scene", "text": "Old", "sceneId": "deleted" } ] }
          }
        }
        """)!.AsObject();

    /// <summary>A textured "world": smooth value noise over heading/pitch with 3° cells (colour depends on both).</summary>
    private static int World(double heading, double pitch)
    {
        double Noise(double u, double v, int salt)
        {
            var iu = (int)Math.Floor(u);
            var iv = (int)Math.Floor(v);
            double fu = u - iu, fv = v - iv;
            fu = fu * fu * (3 - 2 * fu);
            fv = fv * fv * (3 - 2 * fv);
            double H(int a, int b)
            {
                a = ((a % 120) + 120) % 120; // heading wraps (360° / 3°)
                var h = unchecked((uint)(a * 374761393 + b * 668265263 + salt * 2147483647));
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (double)uint.MaxValue;
            }

            var top = H(iu, iv) * (1 - fu) + H(iu + 1, iv) * fu;
            var bottom = H(iu, iv + 1) * (1 - fu) + H(iu + 1, iv + 1) * fu;
            return top * (1 - fv) + bottom * fv;
        }

        var u = GeoMath.NormalizeDegrees(heading) / 3.0;
        var v = (pitch + 90) / 3.0;
        var r = (int)(40 + 200 * Noise(u, v, 1));
        var g = (int)(40 + 200 * Noise(u, v, 2));
        var b = (int)(40 + 200 * Noise(u, v, 3));
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// Ten 60°-wide pinhole photos (120 × 160) every 36°, each tilted a little; the compass reports
    /// heading + error[i]. Exactly the inverse of the stitcher's projection.
    /// </summary>
    private static (List<StitchFrame> Frames, double Focal) RenderRing(double[] compassErrors)
    {
        const int width = 120, height = 160;
        var focal = width / 2.0 / Math.Tan(GeoMath.ToRadians(30));
        var frames = new List<StitchFrame>();
        for (var i = 0; i < compassErrors.Length; i++)
        {
            var heading = i * 36.0;
            var pitch = (i % 3 - 1) * 2.0;
            var (sinP, cosP) = (Math.Sin(GeoMath.ToRadians(pitch)), Math.Cos(GeoMath.ToRadians(pitch)));
            var pixels = new int[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var cx = (x - (width - 1) / 2.0) / focal;
                    var cy = -(y - (height - 1) / 2.0) / focal;
                    const double cz = 1;
                    var dy = cy * cosP + cz * sinP;
                    var dz = -cy * sinP + cz * cosP;
                    var alpha = GeoMath.ToDegrees(Math.Atan2(cx, dz));
                    var phi = GeoMath.ToDegrees(Math.Atan2(dy, Math.Sqrt(cx * cx + dz * dz)));
                    pixels[y * width + x] = World(heading + alpha, phi);
                }
            }

            frames.Add(new StitchFrame(pixels, width, height, GeoMath.NormalizeDegrees(heading + compassErrors[i]), pitch));
        }

        return (frames, focal);
    }

    /// <summary>Mean colour error (0..255 per channel) of the panorama against the world, sampled on a grid.</summary>
    private static double MeanError(StitchResult result, double headingShift = 0)
    {
        double total = 0;
        var count = 0;
        for (var r = 2; r < result.Height - 2; r += 7)
        {
            var pitch = result.VerticalFovDegrees / 2 - (r + 0.5) * result.VerticalFovDegrees / result.Height;
            for (var u = 0; u < result.Width; u += 5)
            {
                var actual = result.Pixels[r * result.Width + u];
                var expected = World(PanoramaStitcher.ColumnHeading(u, result.Width) - headingShift, pitch);
                total += (Math.Abs(((actual >> 16) & 255) - ((expected >> 16) & 255))
                    + Math.Abs(((actual >> 8) & 255) - ((expected >> 8) & 255))
                    + Math.Abs((actual & 255) - (expected & 255))) / 3.0;
                count++;
            }
        }

        return total / count;
    }

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    private static double Num(JsonNode? node) => node!.GetValue<double>();

    private static bool Near(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Check failed");
        }
    }
}
