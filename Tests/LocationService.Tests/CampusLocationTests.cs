using System.Security.Cryptography;
using System.Text;
using BCOpendayApp.AR.Models;
using BCOpendayApp.Panorama.Models;
using BCOpendayApp.Panorama.Services;
using BCOpendayApp.Services;
using BCOpendayApp.Services.Campus;

/// <summary>Tests for the automatic campus location list (AR building scans + online 360° views).</summary>
static class CampusLocationTests
{
    public static void Register(Action<string, Func<Task>> add)
    {
        add("LOCATIONS: buildings and 360° views become locations automatically", () =>
        {
            var library = Building("library", "Library", "Books and study rooms.");
            var hidden = Building("old-shed", "Old shed", "") with { IsActive = false };
            var libraryView = View("a", "Library inside", locationId: "library", time: 2);
            var libraryView2 = View("b", "Library entrance", locationId: "library", time: 1);
            var courtyard = View("c", "Courtyard", locationId: "courtyard", description: "Lunch spot.", time: 3);
            var legacy = View("d", "Old view", locationId: "", time: 4);

            var locations = CampusLocationCatalog.Build(new[] { library, hidden }, new[] { libraryView, libraryView2, courtyard, legacy });

            Check(locations.Count == 3);                                   // library, courtyard, legacy view
            Check(locations.All(location => location.Id != "old-shed"));    // inactive buildings are not locations
            var lib = locations.Single(location => location.Id == "library");
            Check(lib.HasAr && lib.HasPanorama && lib.Badges == "AR · 360°");
            Check(lib.Views.Count == 2 && lib.FirstViewId == libraryView2.Id); // oldest view opens first
            Check(lib.Description == "Books and study rooms." && lib.Code == "BC-LIBRARY");

            var yard = locations.Single(location => location.Id == "courtyard");
            Check(!yard.HasAr && yard.Badges == "360°" && yard.Name == "Courtyard" && yard.Description == "Lunch spot.");

            var old = locations.Single(location => location.Id == legacy.Id); // no LocationId: the view is its own location
            Check(old.Name == "Old view" && old.FirstViewId == legacy.Id);

            Check(locations.Select(location => location.Name).SequenceEqual(new[] { "Courtyard", "Library", "Old view" }));
            return Task.CompletedTask;
        });

        add("LOCATIONS: an AR scan alone is a location; invalid views are ignored", () =>
        {
            var locations = CampusLocationCatalog.Build(
                new[] { Building("robotics", "Robotics lab", "") with { Summary = "Robots." } },
                new[] { View("e", "Bad", locationId: "Bad Id!", time: 1) });
            Check(locations.Count == 1 && locations[0].Badges == "AR" && locations[0].FirstViewId is null);
            Check(locations[0].Description == "Robots.");
            return Task.CompletedTask;
        });

        add("LOCATIONS: codes are found with or without the BC- prefix", () =>
        {
            var locations = CampusLocationCatalog.Build(
                new[] { Building("library", "Library", "") with { StopId = "lib-stop" } },
                Array.Empty<PanoramaManifest>());
            Check(CampusLocationCatalog.FindByCode(locations, "BC-LIBRARY")?.Id == "library");
            Check(CampusLocationCatalog.FindByCode(locations, "  bc-library ")?.Id == "library");
            Check(CampusLocationCatalog.FindByCode(locations, "library")?.Id == "library");
            Check(CampusLocationCatalog.FindByCode(locations, "BC-LIB-STOP")?.Id == "library"); // old stop codes still work
            Check(CampusLocationCatalog.FindByCode(locations, "BC-NOPE") is null);
            Check(CampusLocationCatalog.FindByCode(locations, "") is null);
            return Task.CompletedTask;
        });

        add("LOCATIONS: new location ids are clean, short and unique", () =>
        {
            Check(CampusLocationCatalog.NewLocationId("Main Library!", Array.Empty<string>()) == "main-library");
            Check(CampusLocationCatalog.NewLocationId("Main Library", new[] { "main-library" }) == "main-library-2");
            Check(CampusLocationCatalog.NewLocationId("  ***  ", Array.Empty<string>()) == "location");
            var longId = CampusLocationCatalog.NewLocationId(new string('x', 80), Array.Empty<string>());
            Check(longId.Length <= CampusLocationCatalog.MaxIdLength && PanoramaChunks.IsValidLocationId(longId));
            Check(PanoramaChunks.IsValidLocationId(CampusLocationCatalog.NewLocationId("Café Área 51", Array.Empty<string>())));
            return Task.CompletedTask;
        });

        add("LOCATIONS: location id and description survive the online round trip", () =>
        {
            var view = View("f", "Hall", locationId: "great-hall", description: "Graduations.", time: 5);
            var draft = view.ToDraft();
            Check(draft.LocationId == "great-hall" && draft.Description == "Graduations.");
            Check(CampusLocationCatalog.LocationIdFor(draft) == "great-hall");
            Check(!PanoramaChunks.IsValid(view with { Description = new string('d', 301) }));
            Check(!PanoramaChunks.IsValid(view with { LocationId = "UPPER" }));
            return Task.CompletedTask;
        });

        add("NARRATION: AR Listen button shows within 15 m and only hides past 20 m", () =>
        {
            Check(NarrationRange.IsInRange(0, wasInRange: false));            // inside / touching the building
            Check(NarrationRange.IsInRange(15, wasInRange: false));
            Check(!NarrationRange.IsInRange(16, wasInRange: false));          // not shown yet
            Check(NarrationRange.IsInRange(18, wasInRange: true));            // already shown: GPS wobble keeps it
            Check(NarrationRange.IsInRange(20, wasInRange: true));
            Check(!NarrationRange.IsInRange(21, wasInRange: true));           // walked away
            Check(!NarrationRange.IsInRange(double.NaN, wasInRange: true));
            return Task.CompletedTask;
        });

        add("LOCATIONS: photos are checked before they are shown", () =>
        {
            var jpeg = new byte[1000];
            new Random(3).NextBytes(jpeg);
            var photo = LocationPhotoRules.Create(jpeg, "staff", 1);
            Check(LocationPhotoRules.Decode(photo)!.SequenceEqual(jpeg));
            Check(LocationPhotoRules.Decode(photo with { Sha256 = new string('0', 64) }) is null);
            Check(LocationPhotoRules.Decode(photo with { Data = "not base64!" }) is null);
            Check(LocationPhotoRules.Decode(null) is null);
            Check(LocationPhotoRules.MaxBase64Chars == 800_000);
            Throws(() => LocationPhotoRules.Create(new byte[LocationPhotoRules.MaxBytes + 1], "staff", 1));
            return Task.CompletedTask;
        });
    }

    private static BuildingAnchor Building(string id, string name, string description) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        CenterLat = -25.7545,
        CenterLng = 28.1879,
        RadiusMeters = 20,
    };

    private static PanoramaManifest View(string seed, string name, string locationId, long time, string description = "")
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..32];
        var draft = new PanoramaDraft
        {
            Id = id,
            DisplayName = name,
            FileName = id + ".jpg",
            Width = 4096,
            Height = 711,
            Projection = PanoramaDraft.EquirectangularProjection,
            CaptureMethod = PanoramaDraft.InAppCaptureMethod,
            FileSizeBytes = 1000,
            CreatedAtUnixMs = 1_800_000_000_000,
            VerticalFovDegrees = 62.5,
            PhotoCount = 10,
            LocationId = locationId,
            Description = description,
        };
        return PanoramaManifest.FromDraft(draft, new byte[1000], new string('c', 32), "staff", time);
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

    private static void Check(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Check failed");
        }
    }
}
