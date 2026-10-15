# PRJ381 – Belgium Campus Open Day App

A .NET MAUI mobile app that guides visitors around campus on open days: a self-guided tour of five
campus stops (photos, descriptions, audio narration), campus maps with reference pins, a progress tracker
and a "location code" entry screen. Stop data comes from **Firebase Firestore**, with a bundled offline copy
so the app always works. An **AR building guide** (point the camera at a building to see its name and info) and a
password-protected **staff scanner** that records buildings are included – see [`docs/ar-guide.md`](docs/ar-guide.md).

- Target: **.NET 9, Android** (iOS builds automatically on a Mac; Windows is opt-in, see below)
- Firebase (anonymous Auth, Firestore, Analytics, Crashlytics) switches on **only when the config file is present**

**New here? Follow [`SETUP.md`](SETUP.md).** More detail: [`docs/location-data.md`](docs/location-data.md) (data flow, map pins, security, tests) and
[`firebase/README.md`](firebase/README.md) (rules + seeding).

---

## 1. First-time setup

1. Install **Visual Studio 2022 17.14+** with the *.NET Multi-platform App UI development* workload
   (or the .NET 9 SDK + `dotnet workload install maui-android`).
2. Clone the repo into a **short folder**, e.g. `C:\dev\PRJ381` (not Desktop/OneDrive/Documents).
3. **Firebase config (optional, but needed for live data):** get `google-services.json` from a teammate
   (privately – it is gitignored) and save it as exactly
   `src/BCOpendayApp/Platforms/Android/google-services.json`.
   - Browsers often save a second download as `google-services(2).json` / `google-services_2_.json`.
     **Rename it** – with any other name Firebase is silently off.
   - Its `package_name` must equal `<ApplicationId>` in the csproj (`com.companyname.bcopendayapp`).
4. Open `BCOpendayApp.sln`, pick an Android emulator or a USB-connected phone, and press Run.
   Command line: `dotnet build src/BCOpendayApp -f net9.0-android`

Without the config file the app builds and runs normally on the bundled offline locations; the status line on
each page says *"Using bundled offline locations."* With it, after the one-time Firebase setup in section 4,
it says *"Locations loaded from Firestore."*

`nuget.config` pins restore to nuget.org only, so a stale or private feed on someone's machine
can no longer break restore.

### "Path too long" / restore failures on Windows

The Firebase packages are the heavy ones (their Android/iOS bindings have very deep file trees), and they are
**only restored on machines that have the config file**. So a teammate who doesn't work on Firebase can simply
not add the file. Anyone who does add it and gets path errors should do these in order:

1. **Enable long paths** (once per machine, then reboot), in an admin PowerShell:
   ```powershell
   New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name LongPathsEnabled -Value 1 -PropertyType DWORD -Force
   git config --global core.longpaths true
   ```
2. **Move the NuGet cache to a short folder** – set a user environment variable
   `NUGET_PACKAGES=C:\nuget`, restart Visual Studio. (This matters most: the default cache sits
   under `C:\Users\<your-name>\.nuget\packages`, so a long Windows username eats the path budget
   *regardless* of where the project is.)
3. Install the Android SDK/JDK to short paths too (e.g. `C:\Android\sdk`) in VS → *Tools → Android → Android SDK Manager*.
4. Clear stale state: close VS, delete every `bin` and `obj` folder, run `dotnet nuget locals all --clear`, rebuild.

### Windows desktop target (opt-in)

The team's previous instructions used "Windows Machine". It is now **off by default** because it restores the
large Windows App SDK on every Windows machine. To use it for quick UI checks, uncomment the
`net9.0-windows10.0.19041.0` line in `src/BCOpendayApp/BCOpendayApp.csproj` (don't commit that change unless
the team agrees). Firebase never runs on Windows – it always uses the offline data.

## 2. Project layout

```
BCOpendayApp.sln            the app + the C# test project
nuget.config                restore from nuget.org only
.gitignore / .gitattributes / .editorconfig
docs/                       location-data.md (data flow, pins, security, verification) + evidence/ (test logs)
firebase/                   Firestore rules, seed data, seeder + tests (Node 22) – see firebase/README.md
Tests/LocationService.Tests C# tests that compile the app's real location/geometry source files
design-assets/unused/       artwork nothing uses yet – NOT built into the app
src/BCOpendayApp/
  BCOpendayApp.csproj       Firebase packages are added only if the config file exists (FIREBASE_CONFIGURED)
  MauiProgram.cs            startup: Firebase init + dependency injection (register every new page/VM/service)
  App.xaml(.cs)             global resources, forces dark theme
  AppShell.xaml(.cs)        navigation: splash → tab bar (Home / Tour / Map); pushed pages registered in .cs
  Models/                   Hotspot (a tour stop), FirestoreHotspotDocument (Firestore shape)
  Services/                 Routes, navigation, dialogs, narration (text-to-speech), visitor progress
    Locations/              ILocationService: Firestore → in-memory cache → bundled JSON; CampusCatalog (IDs + maps);
                            MapGeometry (pin placement); HotspotValidation
    Firebase/               auth, Firestore, analytics wrappers – all safe no-ops when Firebase is off
  ViewModels/               one per page; CatalogViewModelBase = shared loading/status/refresh for stop pages
  Views/                    XAML pages + thin code-behind; Controls/ = PageHeader, MenuTile, InfoCard, CatalogStatusView
  AR/                       AR building guide (camera + GPS + orientation) – see docs/ar-guide.md
  Scanner/                  staff-only building scanner (Firebase email/password + "scanners" allow-list)
  Infrastructure/           AsyncCommand (stops double-taps, reports errors instead of crashing)
  Resources/                Styles, Images, Fonts, AppIcon, Splash, Raw/locations.json (offline stop data)
  Properties/               FirebaseLinker.xml (keeps the Firestore model from being trimmed away)
  Platforms/Android|iOS     platform entry points + manifests (+ Windows, used only if opted in)
```

**Adding a page:** `Views/XPage.xaml(.cs)` + `ViewModels/XViewModel.cs` → register both in
`MauiProgram.cs` → add a constant in `Services/Routes.cs` → `Routing.RegisterRoute` in `AppShell.xaml.cs`.
A page that shows stops should derive its view model from `CatalogViewModelBase`, override
`OnCatalogueLoaded()`, and put `<controls:CatalogStatusView />` near the top.

**Changing stop content:** edit `Resources/Raw/locations.json` **and** `firebase/locations.seed.json` (they must
match – `node firebase/scripts/validate.mjs`), then re-seed Firestore. Photos go in `Resources/Images`
(lowercase letters/digits/underscores, starting with a letter). Never rename a stop **id**.

**Firebase code** must sit inside `#if FIREBASE_CONFIGURED` (see `Services/Firebase/FirebaseAnalyticsService.cs`),
or every clone without the config file stops building.

## 3. What was changed and why

### First restructure

| Problem in the old repo | What was done |
|---|---|
| **NuGet path-length/name failures** | Firebase packages (the deep native bindings) now restore only when the config file exists; mismatched `Microsoft.Extensions.Configuration.Json` (v10 on net9) removed; MacCatalyst/Tizen dropped and Windows made opt-in (each restores a whole extra platform SDK). iOS only builds on a Mac. |
| **Solution pointed at a deleted Unity project** | `UnityAndroidBinding` removed from the `.sln`; the Unity/jar rules removed from `.gitignore`. |
| **Messy structure** | Single `src/` project, MVVM layout, one folder per concern. Deleted template `MainPage`, `temp.cs` placeholders, empty `Controllers/`, `GlobalXmlns.cs`, unused `Models/Location.cs`, stray `belgium_logo.png.png`. |
| **Pages built in C# inside empty XAML files** | Real XAML pages with data binding. Renamed/merged: `TourViewPage`→`StopViewerPage`, `HotspotInfoOverlayPage`→`StopInfoPage`, `QrScanPage`→`StartTourPage`, `NotificationsPage`→`VisitorInfoPage`, `SplashPage1`→`SplashPage`. Hand-made bottom bar replaced by the native Shell tab bar. |
| **~25 unused images** | Moved to `design-assets/unused/`. |
| **Default .NET icon/splash** | Belgium Campus logo. |
| **`.gitignore`** | Rewritten: ignores generated files and secrets only. Check a rule with `git check-ignore -v <path>`. |

### Merging the Firebase / location update (Members 3 & 4)

All of the team's location work was kept – `LocationService` (Firestore + offline fallback, caching, timeouts),
the Firestore model, validation, `MapGeometry` pins, Analytics/Crashlytics, the `firebase/` rules + seeder +
tests and the C# test project – and fitted into the structure above:

- The page logic from the C#-built `LocationPage` pages now lives in view models (`CatalogViewModelBase` and
  friends) with XAML pages; the "Refresh locations" status block is the reusable `CatalogStatusView`.
- Map pins are drawn in `MapPage.xaml.cs` from `MapViewModel.PinnedStops`; tapping a pin opens that stop's info.
- **Fixed:** `FirebaseAnalyticsService` called Firebase APIs without `#if FIREBASE_CONFIGURED`, so any clone
  *without* `google-services.json` failed to build. It is now guarded and a no-op when Firebase is off.
- **`ApplicationId` reverted to `com.companyname.bcopendayapp`** (the first restructure had changed it) because
  that is the package name registered in Firebase. If you ever change it, register the new ID in Firebase and
  download a new `google-services.json`. It cannot change once published to the Play Store.
- Paths in `firebase/` scripts/tests and the test project were updated from `MAUIProject/...` to `src/...`;
  the test project is now in the solution; the hand-over notes were condensed into `docs/location-data.md`.

## 4. Turning on live Firebase (one-time, per Firebase project)

Skip whatever your team has already done in the `bcopenday` Firebase project.

1. Firebase console → *Authentication* → enable **Anonymous**. *Firestore Database* → create the default database.
2. Review `firebase/firestore.rules` – **deploying replaces the project's current rules**, so merge any rules
   other members rely on first. Note it still lets any signed-in (anonymous) user read **and write**
   `member1_verification`; delete that block when the test collection is no longer needed.
3. Deploy the rules and seed the five stops – exact commands in [`firebase/README.md`](firebase/README.md)
   (dry run first; the Admin service-account key stays outside the repo).
4. Put `google-services.json` in place (section 1), rebuild, open the Tour tab and tap *Refresh locations*.

## 5. Moving the team over to the new structure

Step-by-step (git commands, Firebase console, scanning, final checks): **[`SETUP.md`](SETUP.md)**.
Check the repo history for leaked secrets once:
`git log --all --oneline -- "*google-services*" "*appsettings*" "*.keystore" "*service-account*"` – if a real key was
ever committed, restrict/rotate it (deleting the file doesn't remove it from history).

## 6. Next steps

### AR guide and building scanner
Implemented – **read [`docs/ar-guide.md`](docs/ar-guide.md)** for how it works, the one-time Firebase console
steps (Email/Password sign-in, staff account, `scanners` allow-list, rules) and how to scan buildings.
Code: `src/BCOpendayApp/AR/` (visitor guide) and `src/BCOpendayApp/Scanner/` (staff-only scanner).
- Don't upgrade `CommunityToolkit.Maui.Camera` past 4.0.0 or un-pin `Microsoft.Maui.Controls` 9.0.120 until the move to .NET 10.
- A QR scanner for stop codes is still open: put the scanned text into `StartTourViewModel.Code` and run `OpenCodeCommand`.

### Smaller to-dos
- Lecture Hall has no map pin (lecture rooms are spread over several buildings) – see `docs/location-data.md`.
- Real stop photos: the Library, Lecture Hall and Student Lounge images appear to show lab spaces.
- Favourites are saved but nothing lists them yet (e.g. a section on the Tour tab).
- Analytics: only the Home screen view is logged; `IAnalyticsService` has methods for stop/pin taps if wanted.
- .NET 9 reaches end of support in November 2026; plan a move to `net10.0` after the open day.

## 7. Status: not yet built or run

This restructure was done without a .NET/MAUI toolchain, so **the app and the C# tests have not been compiled
or run** after the merge. Checked here: XAML/XML well-formed, every binding exists on its view model, every
page/service is registered, every route and image resolves, the `.gitignore` hides the config file but nothing
the build needs, and the Node data tests pass (26/26). Expect to fix a small compile error or two. Smoke test:

- [ ] `dotnet run --project Tests/LocationService.Tests` → 47/47 pass
- [ ] Builds and launches **without** `google-services.json`; status says bundled offline locations
- [ ] Builds and launches **with** it; after section 4, status says *Locations loaded from Firestore*
- [ ] Home: all six tiles open the right page; progress bar starts at 0 / 5
- [ ] Tour tab: *Continue* opens the first stop; *Mark as visited* → progress updates on Home and Tour
- [ ] Stop viewer: Info Points, Audio / Stop audio, Gallery (robotics shows two photos), Floor Plan → Map tab
- [ ] Map: Library and Student Lounge pins on Main, Robotics + Manufacturing pins side by side on North (The Hangar);
      pins stay in place when zooming; tapping one opens that stop's info;
      Floor Plan from the Library selects the Main map and outlines its pin in yellow
- [ ] Start AR Tour: `BC-ROBOTICS` (also `robotics`) opens the stop; `xyz` shows the error listing valid codes
- [ ] Airplane mode: everything still works from the bundled data; *Refresh locations* doesn't hang
- [ ] Close and reopen the app: visited stops and favourites are still there
- [ ] A teammate who previously hit path-length errors can restore and build (without the config file first)
