using System.Text.Json.Nodes;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;

/// <summary>Tests for the local panorama import rules, draft metadata, safe names and viewer configuration.</summary>
static class PanoramaTests
{
    public static void Register(Action<string, Func<Task>> add)
    {
        add("PANORAMA: 2:1 PNG is accepted as a full-sphere image", () =>
        {
            WithTemporaryFile(".png", Png(4096, 2048), path =>
            {
                var result = PanoramaValidation.ValidateFile(path, "campus.png");
                Check(result.IsValid);
                Check(result.Image is { Width: 4096, Height: 2048, Format: PanoramaImageFormat.Png });
            });
            return Task.CompletedTask;
        });

        add("PANORAMA: 2:1 JPEG is accepted as a full-sphere image", () =>
        {
            WithTemporaryFile(".jpg", Jpeg(4000, 2000), path =>
            {
                var result = PanoramaValidation.ValidateFile(path, "campus.jpg");
                Check(result.IsValid);
                Check(result.Image is { Width: 4000, Height: 2000, Format: PanoramaImageFormat.Jpeg });
            });
            return Task.CompletedTask;
        });

        add("PANORAMA: the documented 2% ratio tolerance accepts 1.96 and 2.04", () =>
        {
            Check(PanoramaImageRules.IsFullSphereAspectRatio(1960, 1000));
            Check(PanoramaImageRules.IsFullSphereAspectRatio(2040, 1000));
            Check(!PanoramaImageRules.IsFullSphereAspectRatio(1959, 1000));
            Check(!PanoramaImageRules.IsFullSphereAspectRatio(2041, 1000));
            return Task.CompletedTask;
        });

        add("PANORAMA: a normal 16:9 photo is rejected", () =>
        {
            WithTemporaryFile(".jpg", Jpeg(4096, 2304), path =>
            {
                var result = PanoramaValidation.ValidateFile(path, "photo.jpg");
                Check(!result.IsValid);
                Check(result.Image is null);
                Check(result.Message.Contains("must be about 2:1", StringComparison.Ordinal));
            });
            return Task.CompletedTask;
        });

        add("PANORAMA: too-small and too-large dimensions are rejected", () =>
        {
            WithTemporaryFile(".jpg", Jpeg(2047, 1024), path =>
                Check(!PanoramaValidation.ValidateFile(path, "small.jpg").IsValid));
            WithTemporaryFile(".jpg", Jpeg(8194, 4097), path =>
                Check(!PanoramaValidation.ValidateFile(path, "large.jpg").IsValid));
            return Task.CompletedTask;
        });

        add("PANORAMA: the minimum and maximum accepted dimensions are inclusive", () =>
        {
            WithTemporaryFile(".jpg", Jpeg(2048, 1024), path =>
                Check(PanoramaValidation.ValidateFile(path, "min.jpg").IsValid));
            WithTemporaryFile(".jpg", Jpeg(8192, 4096), path =>
                Check(PanoramaValidation.ValidateFile(path, "max.jpg").IsValid));
            return Task.CompletedTask;
        });

        add("PANORAMA: unsupported extensions and extension/content mismatches are rejected", () =>
        {
            WithTemporaryFile(".gif", Png(4096, 2048), path =>
            {
                var result = PanoramaValidation.ValidateFile(path, "image.gif");
                Check(!result.IsValid);
                Check(result.Message.Contains("JPEG and PNG", StringComparison.Ordinal));
            });
            WithTemporaryFile(".png", Jpeg(4096, 2048), path =>
                Check(!PanoramaValidation.ValidateFile(path, "image.png").IsValid));
            return Task.CompletedTask;
        });

        add("PANORAMA: corrupt or truncated image headers are rejected", () =>
        {
            WithTemporaryFile(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x20 }, path =>
            {
                var result = PanoramaValidation.ValidateFile(path, "broken.jpg");
                Check(!result.IsValid);
                Check(result.Message.Contains("not a readable JPEG or PNG", StringComparison.Ordinal));
            });
            WithTemporaryFile(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, path =>
                Check(!PanoramaValidation.ValidateFile(path, "broken.png").IsValid));
            return Task.CompletedTask;
        });

        add("PANORAMA: empty and oversized files are rejected before decoding", () =>
        {
            WithTemporaryFile(".jpg", Array.Empty<byte>(), path =>
                Check(!PanoramaValidation.ValidateFile(path, "empty.jpg").IsValid));

            WithTemporaryFile(".jpg", Array.Empty<byte>(), path =>
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    stream.SetLength(PanoramaImageRules.MaxFileSizeBytes + 1);
                }

                var result = PanoramaValidation.ValidateFile(path, "huge.jpg");
                Check(!result.IsValid);
                Check(result.Message.Contains("limit", StringComparison.Ordinal));
            });
            return Task.CompletedTask;
        });

        add("PANORAMA: a missing file is rejected", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "prj381-missing-" + Guid.NewGuid().ToString("N") + ".jpg");
            Check(!PanoramaValidation.ValidateFile(path, "missing.jpg").IsValid);
            return Task.CompletedTask;
        });

        add("PANORAMA: valid draft metadata round-trips all required fields", () =>
        {
            var draft = ValidDraft();
            Check(PanoramaDraftValidation.IsValid(draft));
            Check(draft.Projection == PanoramaDraft.EquirectangularProjection);
            Check(draft.CaptureMethod == PanoramaDraft.GalleryImportMethod);
            return Task.CompletedTask;
        });

        add("PANORAMA: draft metadata rejects path traversal and mismatched names", () =>
        {
            var draft = ValidDraft();
            Check(!PanoramaDraftValidation.IsValid(draft with { FileName = "../" + draft.Id + ".jpg" }));

            var otherId = new string('b', 32);
            Check(!PanoramaDraftValidation.IsValid(draft with { FileName = otherId + ".jpg" }));
            Check(!PanoramaDraftValidation.IsValid(draft with { Id = "not-a-draft-id" }));
            Check(!PanoramaDraftValidation.IsValid(draft with { Width = 4096, Height = 2304 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { Projection = "partial" }));
            Check(!PanoramaDraftValidation.IsValid(draft with { IsFullSphere = false }));
            Check(!PanoramaDraftValidation.IsValid(draft with { CaptureMethod = "camera" }));
            Check(!PanoramaDraftValidation.IsValid(draft with { CreatedAtUnixMs = 0 }));
            Check(!PanoramaDraftValidation.IsValid(draft with { CreatedAtUnixMs = long.MaxValue }));
            return Task.CompletedTask;
        });

        add("PANORAMA: draft file names are unique, safe and extension-normalised", () =>
        {
            var id = new string('a', 32);
            Check(PanoramaLocalFiles.CreateDraftFileName(id, PanoramaImageFormat.Jpeg) == id + ".jpg");
            Check(PanoramaLocalFiles.CreateDraftFileName(id, PanoramaImageFormat.Png) == id + ".png");
            Check(PanoramaLocalFiles.IsSafeDraftFileName(id + ".jpg"));
            Check(PanoramaLocalFiles.IsSafeDraftFileName(id + ".png"));
            Check(!PanoramaLocalFiles.IsSafeDraftFileName(id + ".gif"));
            Check(!PanoramaLocalFiles.IsSafeDraftFileName("../" + id + ".jpg"));
            Check(!PanoramaLocalFiles.IsSafeDraftFileName(id.ToUpperInvariant() + ".jpg"));
            Check(!PanoramaLocalFiles.IsSafeDraftId("short"));

            var tempJpeg = PanoramaLocalFiles.CreateTempFileName(".jpeg");
            var tempPng = PanoramaLocalFiles.CreateTempFileName(".PNG");
            Check(tempJpeg.EndsWith(".jpg", StringComparison.Ordinal));
            Check(tempPng.EndsWith(".png", StringComparison.Ordinal));
            Check(tempJpeg.StartsWith("import-", StringComparison.Ordinal));
            Check(tempJpeg != PanoramaLocalFiles.CreateTempFileName(".jpg"));
            return Task.CompletedTask;
        });

        add("PANORAMA: full-sphere Pannellum config uses 360/180 FOV and no debug hotspots", () =>
        {
            var json = PanoramaViewerConfig.BuildFullSphereTourJson(
                "Test panorama",
                "data:image/jpeg;base64,AAAA");
            var root = JsonNode.Parse(json)!.AsObject();

            Check((bool)root["default"]!["hotSpotDebug"]! == false);
            Check((bool)root["default"]!["autoLoad"]!);

            var scene = root["scenes"]!["local"]!.AsObject();
            Check((double)scene["haov"]! == 360);
            Check((double)scene["vaov"]! == 180);
            Check((double)scene["vOffset"]! == 0);
            Check((double)scene["hfov"]! == 90);
            Check(scene["hotSpots"]!.AsArray().Count == 0);
            Check(scene["panorama"]!.GetValue<string>() == "data:image/jpeg;base64,AAAA");
            return Task.CompletedTask;
        });

        add("PANORAMA: debug overlays can be disabled in the bundled tour config", () =>
        {
            var root = JsonNode.Parse("""{"default":{"hotSpotDebug":true}}""")!.AsObject();
            PanoramaViewerConfig.DisableDebugOverlays(root);
            Check((bool)root["default"]!["hotSpotDebug"]! == false);
            return Task.CompletedTask;
        });
    }

    private static PanoramaDraft ValidDraft() => new()
    {
        Id = new string('a', 32),
        DisplayName = "Library",
        FileName = new string('a', 32) + ".jpg",
        Width = 4096,
        Height = 2048,
        Projection = PanoramaDraft.EquirectangularProjection,
        IsFullSphere = true,
        CaptureMethod = PanoramaDraft.GalleryImportMethod,
        FileSizeBytes = 4 * 1024 * 1024,
        CreatedAtUnixMs = 1_700_000_000_000,
    };

    private static void WithTemporaryFile(string extension, byte[] bytes, Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "prj381-pano-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            File.WriteAllBytes(path, bytes);
            test(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        AddUInt32BigEndian(bytes, 13);
        bytes.AddRange("IHDR"u8.ToArray());
        AddUInt32BigEndian(bytes, (uint)width);
        AddUInt32BigEndian(bytes, (uint)height);
        return bytes.ToArray();
    }

    private static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
        bytes.AddRange(new byte[14]);
        bytes.AddRange(new byte[]
        {
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03,
            0x01, 0x11, 0x00,
            0x02, 0x11, 0x00,
            0x03, 0x11, 0x00,
        });
        return bytes.ToArray();
    }

    private static void AddUInt32BigEndian(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed.");
        }
    }
}
