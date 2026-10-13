# Members 3 and 4 - location and offline integration

## Apply this update without moving your folders

This is a **source update / overlay**, not a standalone replacement for your original archive.
Copy the contents of the matching project folder over your existing extracted project, allowing
Windows to merge folders and replace the included source files. **Do not delete your existing
project first.** Keep its existing images, fonts, platform resources, solution files, Unity project,
and Git metadata. Those unchanged assets are not redistributed in this update.

Use the PRJ381-Backend update for the backend repository. Use the BCOpenDay update for the
maps/Unity archive. Both contain the same location integration; apply only the one matching
the project you are working in. No existing directory has been renamed or relocated.

Open `MAUIProject/BCOpendayApp/BCOpendayApp.sln` in your MAUI development environment.
The project's .NET 9 target is retained. Build and run the Android target to test native Firebase.
Without your own Firebase configuration file, the app selects bundled offline data automatically.

## What is implemented

| Responsibility | Files / behavior |
| --- | --- |
| Member 3: model | `Models/Hotspot.cs`, plus a separate attributed `FirestoreHotspotDocument` DTO |
| Member 3: interface | `Services/ILocationService.cs`: catalogue reads and one-location lookup |
| Member 3: Firestore | Authenticated, server-only read of the `locations` collection using your existing `IAuthService` and `IFirestoreService` contracts |
| Member 3: five records | Robotics Lab, Manufacturing Lab, Lecture Hall, Library, Student Lounge; original text, images and stable IDs retained |
| Member 3: page connections | Map, Locations, Tour, TourView, HotspotInfoOverlay, Home progress and manual-code entry use the same injected service |
| Member 4: offline fallback | `Resources/Raw/locations.json`, opened with MAUI `FileSystem.Current.OpenAppPackageFileAsync` |
| Member 4: rules | `firebase/firestore.rules`: signed-in reads, no client writes, all other paths denied |
| Supporting work | Real-config detection, safe native initialization, dependency injection, bounded requests, data validation, seed tooling and tests |

The old `CampusCatalog` is now only stable ID / map metadata. It no longer holds a second copy
of the location descriptions. The unused original `Models/Location.cs` is left in place for compatibility.
Visitor favourites and visited flags remain local Preferences, with their existing keys unchanged.

## Data flow

`Page -> ILocationService -> LocationService -> FirestoreLocationSource -> existing auth / Firestore services`

`LocationService -> bundled locations.json` when cloud data cannot be used.

A successful request is reused for five minutes; the Refresh locations button bypasses that wait.
Failures are retried after 30 seconds, by explicit refresh, or immediately when the connection/configuration
state changes. The total remote wait is bounded to six seconds. Navigation cancels the waiting page.
The native SDK may finish a request after that wait; the adapter reuses outstanding operations and observes
late errors instead of launching unlimited requests.

A missing, invalid or duplicate remote location does not remove a required stop: the bundled record fills
that gap. If a successful catalogue has already loaded in the current app session, it can be reused during
an outage. That application-level cache is **memory only**. On a fresh launch without cloud access, the
bundled JSON is available without prior login or a previous server visit. Source/fallback status is visible.

## Map accuracy - important

The existing Main, North and West map images and section buttons are retained. Pins use normalized
image coordinates (0 to 1), and the image is laid out at its original aspect ratio before pins are positioned.
This prevents zooming and resizing from shifting pins away from their reference points.

The supplied Main Campus image explicitly labels the Library. Its bundled pin is an approximate
image-reference point (`x: 0.551`, `y: 0.126`), **not a surveyed position or GPS coordinate**.
The supplied files do not establish where the other four named tour stops should be pinned.
Their `mapSection` is empty and their `x` / `y` values are `null`. They remain fully usable in the directory,
tour and information pages. The UI explains their missing map positions rather than inventing them.

To add a confirmed pin, edit the location in BOTH JSON files (and seed with explicit overwrite), or edit
the Firestore document for the live catalogue. Set `mapSection` to `main`, `north` or `west`, set `x` and `y`
to fractions of that image's width and height, and update `positionNote`. Rebuild after changing the bundled
file. Editing Firestore alone does not update the app's shipped offline bundle.

## Turn on live Firebase

No Firebase configuration file or account credentials were present in the supplied archives. Nothing has
been created or deployed in your cloud project by this update.

1. In your Firebase project, register the Android app with the project's existing application ID,
   `com.companyname.bcopendayapp` (or your deliberately changed ID). Put the downloaded app config at:
   `MAUIProject/BCOpendayApp/Platforms/Android/google-services.json`.
   Do not use a service-account key as this file.
2. Enable **Authentication -> Anonymous** and create the default Cloud Firestore database.
3. Review and deploy the rules, then seed the records using `firebase/README.md` below.
4. Rebuild the app after adding the platform file. Open Locations or Map and tap Refresh locations.
   A fully successful live load displays `Locations loaded from Firestore.`

The project file includes the native Firebase packages and initialization only when the target's real
configuration file exists at build time. Missing configuration is not a build-stopping placeholder.
The original backend Firebase package versions are retained. An unused appsettings configuration loader
was removed; no project ID, API key or service-account secret has been fabricated.

For iOS, supply `Platforms/iOS/GoogleService-Info.plist`, configure provisioning/keychain entitlements
and build on an appropriate Apple toolchain. Native initialization has been added for iOS, but it has
not been device-tested here. Windows and Mac Catalyst use the bundled-data path in this integration;
the supplied native Firebase wrapper is not used on those targets. In the maps archive,
adding Android Firebase configuration raises the Android minimum from API 21 to API 23 for
that configured build; its offline-only build retains API 21. The backend archive retains API 26.

## Security scope

Anonymous sign-in gives visitors permission to read location documents, not permission to change them.
The app has no location-editing or automatic-seeding feature. Only your trusted Admin SDK / IAM tooling
writes catalogue data. The Admin seeder defaults to a dry run and, when explicitly applied, creates only
missing documents. `--overwrite` is a separate explicit choice; it never deletes documents.

**Deploying this rules file replaces the current Firestore rules.** The uploaded source did not define
other collections. If your actual Firebase project already supports other team members' collections,
merge their reviewed rules before deployment rather than unintentionally denying their features.
Never replace the rules with a broad `allow read, write: if true` workaround.

## Verification commands

From the project root:

```powershell
# Actual C# repository/geometry tests; requires the .NET 9 SDK, not a MAUI workload.
dotnet run --project Tests/LocationService.Tests/LocationService.Tests.csproj

# These data/tooling tests use built-in Node modules and need no npm install.
node firebase/scripts/validate.mjs
node --test firebase/tests/data.test.mjs
node firebase/scripts/seed-locations.mjs --dry-run

# Android app build: use your installed .NET MAUI / Android toolchain.
dotnet build MAUIProject/BCOpendayApp/BCOpendayApp.csproj -f net9.0-android
```

The C# test project compiles the real service/model files, not copies or a JavaScript reimplementation.
Its tests cover first-launch offline mode, configuration failure, partial/invalid/duplicate data, authentication
errors, timeouts, cancellation, cache behavior, reconnection, concurrency, lookup and pin geometry.

The Firestore rules suite is separate and must be run against the emulator as described in `firebase/README.md`.
Read `MEMBERS_3_4_VALIDATION.md` for exactly what was and was not run when this update was prepared.
Older Review reports in the original maps archive describe that earlier archive, not this integration.

## On-device acceptance checks

- Launch without a Firebase config file: all five location cards and descriptions must work.
- Launch in airplane mode before any successful sign-in: the same five records must load from JSON.
- Configure Firebase, enable anonymous auth, deploy rules and seed: all five records must load live.
- Temporarily remove or invalidate one test-project record: that stop must fall back instead of disappearing.
- Disable the connection after a successful load: the page must show its fallback status and remain usable.
- Open the Library from the map pin, then navigate through next-location, tour, audio and favourites controls.
- Switch Main/North/West and zoom: the Library reference point must remain attached to the Main map.
- Confirm that four unassigned stop positions are clearly explained rather than shown at false coordinates.

## Official implementation references

- MAUI bundled-file access: https://learn.microsoft.com/dotnet/maui/platform-integration/storage/file-system-helpers
- Native Firestore model attributes and source behavior: https://www.nuget.org/packages/Plugin.Firebase.Firestore/4.0.0
- Firebase rules conditions / Admin SDK distinction: https://firebase.google.com/docs/firestore/security/rules-conditions
- Rules test environment: https://firebase.google.com/docs/rules/unit-tests
- Firebase Admin SDK setup: https://firebase.google.com/docs/admin/setup
