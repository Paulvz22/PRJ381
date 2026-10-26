using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;

/// <summary>
/// Tests for online 360° views: splitting images into Firestore-sized pieces, the manifest checks, the phone-side
/// cache/offline behaviour (with a fake Firestore) and online views in the combined tour.
/// </summary>
static class PanoramaOnlineTests
{
    public static void Register(Action<string, Func<Task>> add)
    {
        add("PANO ONLINE: images split into pieces and join back exactly", () =>
        {
            foreach (var size in new[] { 1, 2, 3, PanoramaChunks.ChunkBytes - 1, PanoramaChunks.ChunkBytes, PanoramaChunks.ChunkBytes + 1, 1_700_000 })
            {
                var image = Bytes(size, seed: size);
                var pieces = PanoramaChunks.Split(image);
                Check(pieces.Count == PanoramaChunks.ChunkCountFor(size));
                Check(pieces.All(p => p.Length <= PanoramaChunks.ChunkChars));
                var manifest = Manifest(image);
                Check(PanoramaChunks.Join(pieces, manifest).SequenceEqual(image));
            }

            Check(PanoramaChunks.ChunkCountFor(1_700_000) == 4);
            Check(PanoramaChunks.ChunkChars == 700_000 && PanoramaChunks.ChunkBytes == 525_000);
            return Task.CompletedTask;
        });

        add("PANO ONLINE: damaged, missing or oversized pieces are rejected", () =>
        {
            var image = Bytes(1_200_000, seed: 7);
            var pieces = PanoramaChunks.Split(image).ToList();
            var manifest = Manifest(image);

            Throws(() => PanoramaChunks.Join(pieces.Take(2).ToList(), manifest));             // a piece missing
            var damaged = pieces.ToList();
            damaged[1] = Convert.ToBase64String(Bytes(PanoramaChunks.ChunkBytes, seed: 99));
            Throws(() => PanoramaChunks.Join(damaged, manifest));                              // wrong content
            Throws(() => PanoramaChunks.Split(Bytes((int)PanoramaChunks.MaxImageBytes + 1, seed: 1)));
            Throws(() => PanoramaChunks.Split(Array.Empty<byte>()));
            return Task.CompletedTask;
        });

        add("PANO ONLINE: manifest round-trips a captured view and is validated", () =>
        {
            var image = Bytes(900_000, seed: 3);
            var draft = Draft("ab");
            var manifest = PanoramaManifest.FromDraft(draft, image, new string('c', 32), "staff-uid", 1_800_000_000_000);
            Check(PanoramaChunks.IsValid(manifest));
            Check(manifest.ChunkCount == 2 && manifest.ByteLength == 900_000 && manifest.Sha256 == PanoramaChunks.Sha256Hex(image));
            Check(manifest.VerticalFovDegrees == 62.5 && manifest.MimeType == "image/jpeg" && manifest.UpdatedBy == "staff-uid");

            var back = manifest.ToDraft();
            Check(back.Id == draft.Id && back.DisplayName == draft.DisplayName && back.ViewerVerticalFov == 62.5);
            Check(back.HasLocation && back.Latitude == draft.Latitude && back.HasNorthOffset && back.NorthOffsetDegrees == 0);

            Check(!PanoramaChunks.IsValid(manifest with { Id = "nope" }));
            Check(!PanoramaChunks.IsValid(manifest with { ChunkCount = 3 }));            // does not match the size
            Check(!PanoramaChunks.IsValid(manifest with { Sha256 = "short" }));
            Check(!PanoramaChunks.IsValid(manifest with { MimeType = "text/html" }));
            Check(!PanoramaChunks.IsValid(manifest with { Latitude = 200 }));
            Check(!PanoramaChunks.IsValid(manifest with { Name = "" }));
            Check(!PanoramaChunks.IsValid(null));
            return Task.CompletedTask;
        });

        add("PANO ONLINE: the list is saved on the phone and used when offline", async () =>
        {
            using var folder = new TempFolder();
            var remote = new FakeRemote();
            var a = remote.Add(Draft("a1"), Bytes(1000, 1));
            var library = new PanoramaOnlineLibrary(remote, folder.Path);

            var fresh = await library.GetListAsync();
            Check(fresh.IsFresh && fresh.Manifests.Single().Id == a.Id);

            remote.Fail = true;
            var offline = await library.GetListAsync();
            Check(!offline.IsFresh && offline.Manifests.Single().Id == a.Id && offline.Message.Contains("Offline", StringComparison.Ordinal));

            var unconfigured = new PanoramaOnlineLibrary(new FakeRemote { IsConfigured = false }, folder.Path);
            Check((await unconfigured.GetListAsync()).Manifests.Single().Id == a.Id);
        });

        add("PANO ONLINE: an image is downloaded once and replaced when staff re-upload", async () =>
        {
            using var folder = new TempFolder();
            var remote = new FakeRemote();
            var first = remote.Add(Draft("b1"), Bytes(2000, 2));
            var library = new PanoramaOnlineLibrary(remote, folder.Path);

            var path = await library.GetImagePathAsync(first);
            Check(File.ReadAllBytes(path).SequenceEqual(remote.Images[first.Id]));
            await library.GetImagePathAsync(first);
            Check(remote.Downloads == 1);                                   // the saved copy is used

            var second = remote.Add(Draft("b1"), Bytes(3000, 3));           // same view, new image
            var newPath = await library.GetImagePathAsync(second);
            Check(remote.Downloads == 2 && newPath != path && !File.Exists(path) && File.Exists(newPath));

            remote.Fail = true;
            Check((await library.GetImagePathAsync(second)) == newPath);   // offline: still works from the copy
        });

        add("PANO ONLINE: upload keeps a local copy; delete removes it everywhere", async () =>
        {
            using var folder = new TempFolder();
            var remote = new FakeRemote();
            var library = new PanoramaOnlineLibrary(remote, folder.Path);
            var image = Bytes(600_000, 4);

            var manifest = await library.UploadAsync(Draft("c1"), image, "staff");
            Check(remote.Manifests.ContainsKey(manifest.Id) && manifest.ChunkCount == 2);
            Check((await library.GetImagePathAsync(manifest)) is { } path && File.ReadAllBytes(path).SequenceEqual(image));
            Check(remote.Downloads == 0);                                   // the uploader never downloads its own view

            remote.Fail = true;
            Check((await library.GetListAsync()).Manifests.Single().Id == manifest.Id);
            remote.Fail = false;

            await library.DeleteAsync(manifest);
            Check(!remote.Manifests.ContainsKey(manifest.Id));
            remote.Fail = true;
            Check((await library.GetListAsync()).Manifests.Count == 0);
            Check(!Directory.EnumerateFiles(Path.Combine(folder.Path, "panorama-online"), manifest.Id + ".*").Any());

            remote.Fail = false;
            await ThrowsAsync(() => library.UploadAsync(Draft("c2"), Bytes((int)PanoramaChunks.MaxImageBytes + 1, 5), "staff"));
        });

        add("PANO ONLINE: online views join the tour; the phone's own copy is used when present", () =>
        {
            var origin = new GeoPoint(-25.7545, 28.1879);
            var onlineOnly = PanoramaManifest.FromDraft(Draft("d1", origin), Bytes(10, 1), new string('c', 32), "staff", 1);
            var alsoLocal = PanoramaManifest.FromDraft(Draft("d2", At(origin, 90, 30)), Bytes(10, 2), new string('c', 32), "staff", 1);
            var localOnly = Draft("d3", At(origin, 0, 30));

            var tour = PanoramaTour.Create(null, new[] { Draft("d2", At(origin, 90, 30)), localOnly }, new[] { onlineOnly, alsoLocal });
            Check(tour.Scenes.Select(s => s.Id).SequenceEqual(new[] { Id("d1"), Id("d2"), Id("d3") }));

            var d1 = tour.Find(Id("d1"))!;
            Check(!d1.IsOnPhone && d1.Online is not null && d1.Draft is not null);
            var d2 = tour.Find(Id("d2"))!;
            Check(d2.IsOnPhone && d2.Online is not null);
            Check(tour.Find(Id("d3"))!.IsOnPhone && tour.Find(Id("d3"))!.Online is null);

            // Arrows connect online and phone-only views alike.
            var targets = tour.Hotspots(d1).Select(h => h["bcTarget"]!.GetValue<string>()).ToHashSet();
            Check(targets.SetEquals(new[] { Id("d2"), Id("d3") }));
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- helpers

    private static string Id(string seed) => (seed + new string('0', 32))[..32];

    private static GeoPoint At(GeoPoint origin, double bearing, double meters) =>
        GeoMath.FromLocal(origin, GeoMath.Direction(bearing) * meters);

    private static PanoramaDraft Draft(string seed, GeoPoint? at = null)
    {
        var id = Id(seed);
        var where = at ?? new GeoPoint(-25.7545, 28.1879);
        return new PanoramaDraft
        {
            Id = id,
            DisplayName = "View " + seed,
            FileName = id + ".jpg",
            Width = 4096,
            Height = 711,
            Projection = PanoramaDraft.EquirectangularProjection,
            IsFullSphere = false,
            CaptureMethod = PanoramaDraft.InAppCaptureMethod,
            FileSizeBytes = 1000,
            CreatedAtUnixMs = 1_800_000_000_000,
            VerticalFovDegrees = 62.5,
            PhotoCount = 10,
            HasLocation = true,
            Latitude = where.Lat,
            Longitude = where.Lng,
            LocationAccuracyMeters = 4,
            HasNorthOffset = true,
            NorthOffsetDegrees = 0,
        };
    }

    private static PanoramaManifest Manifest(byte[] image) =>
        PanoramaManifest.FromDraft(Draft("e1"), image, new string('c', 32), "staff", 1);

    private static byte[] Bytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class FakeRemote : IPanoramaRemoteStore
    {
        public Dictionary<string, PanoramaManifest> Manifests { get; } = new();

        public Dictionary<string, byte[]> Images { get; } = new();

        public bool IsConfigured { get; init; } = true;

        public bool Fail { get; set; }

        public int Downloads { get; private set; }

        public PanoramaManifest Add(PanoramaDraft draft, byte[] image)
        {
            var manifest = PanoramaManifest.FromDraft(draft, image, Guid.NewGuid().ToString("N"), "staff", 1);
            Manifests[manifest.Id] = manifest;
            Images[manifest.Id] = image;
            return manifest;
        }

        public Task<IReadOnlyList<PanoramaManifest>> ListAsync(CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            return Task.FromResult<IReadOnlyList<PanoramaManifest>>(Manifests.Values.ToList());
        }

        public Task<byte[]> DownloadAsync(PanoramaManifest manifest, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Downloads++;
            var pieces = PanoramaChunks.Split(Images[manifest.Id]);
            return Task.FromResult(PanoramaChunks.Join(pieces, manifest));
        }

        public Task<PanoramaManifest> UploadAsync(PanoramaDraft draft, byte[] image, string uid, IProgress<double>? progress)
        {
            ThrowIfFailing();
            PanoramaChunks.Split(image);
            return Task.FromResult(Add(draft, image));
        }

        public Task DeleteAsync(PanoramaManifest manifest)
        {
            ThrowIfFailing();
            Manifests.Remove(manifest.Id);
            Images.Remove(manifest.Id);
            return Task.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (Fail)
            {
                throw new IOException("offline");
            }
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prj381-pano-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Throws(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            return;
        }

        throw new InvalidOperationException("Expected an exception");
    }

    private static async Task ThrowsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            return;
        }

        throw new InvalidOperationException("Expected an exception");
    }

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Check failed");
        }
    }
}
