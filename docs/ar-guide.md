# AR building guide + staff scanner — setup and how it works

Visitors open **Tour → Open AR camera** and point the phone at a campus building. The name, a
summary and "More info" for that building appear over the live camera image. Staff use the
**password-protected scanner** in the same app (**Tour → New AR scan**) to record buildings.
The scans are stored in **Firebase Firestore**, so every visitor gets them automatically. There is no sign-in
or setup for visitors.

Everything is built and run from **Visual Studio**. The only other tool is the Firebase website console,
which is used to switch features on, create the staff account and publish the rules. No Unity, Android Studio,
ARCore SDK, Google Cloud project setup or Node.js is needed. Node.js is only needed for the *optional* rules tests.

---

## 1. How it works (and why not ARCore)

| | Old plan (Unity + ARCore Cloud Anchors) | This version (geo-anchors) |
|---|---|---|
| What a "scan" records | Visual feature points of a surface | The building's **outline on the map** (GPS corners) |
| Works from | Near the spot where the anchor was made | **Any side, any distance up to 400 m** |
| Needs the scanner to "see" surfaces | Yes (the problem you kept hitting) | No |
| Anchor lifetime | 1–365 days, then they expire | Forever (it's just data) |
| Tooling | Unity + ARCore SDK + Google Cloud API keys | Visual Studio + NuGet only |
| .NET support | `Xamarin.Google.ARCore` was last updated in **March 2022** and is marked unsupported/unmaintained | `CommunityToolkit.Maui.Camera` (maintained by the .NET Foundation) plus MAUI's built-in sensors |

**Recognition:**

1. GPS gives the visitor's position. A small Kalman-style filter removes GPS jitter.
2. The phone's fused **rotation-vector sensor** (gyroscope + accelerometer + magnetometer) gives the
   direction the *back camera* is pointing. The heading is corrected from magnetic north to true north using
   Android's built-in world magnetic model. Around Pretoria that correction is about 18°, and without it
   every label would be off by 18°.
3. A ray is cast from the visitor along the camera direction. The **nearest** building outline it crosses is
   the one being looked at, so a closer building hides the ones behind it, like in reality.
4. If the ray just misses, a building still counts when it is within the sensor error (about 4° of compass
   error plus the GPS error as seen from that distance, max 15°).
5. A building must stay under the reticle for 0.45 s before the card switches, so the card doesn't flicker
   between neighbouring buildings.

This is the same approach most outdoor campus or city AR guides use. It is reliable for building-sized
targets. Be honest with your lecturers about its limits:

- It works **outdoors**, within 400 m. It is not for picking out individual rooms indoors.
- GPS on phones is accurate to about 3–10 m. Steel structures, cars and laptops near the phone disturb the
  compass, which is why the guide tells visitors to calibrate with a figure-8 motion.
- Floating labels are placed with the camera's real focal length. They follow the buildings closely but not to
  the pixel. The **selection** (the card at the bottom) comes from the outline maths, not from the drawing.

---

## 2. Where the files are

The AR guide and scanner are part of the full project (see [`SETUP.md`](../SETUP.md) for getting it into GitHub).
For reference, this is what they consist of and which existing files they touch:

**New folders and files:**

```
src/BCOpendayApp/AR/                       visitor AR guide
  ArFeature.cs                             registration: builder.UseBuildingAr()
  Models/BuildingAnchor.cs                 a scanned building (outline / centre + radius, texts)
  Geo/GeoMath.cs, BuildingTargeting.cs     "which building am I pointing at" maths (unit-tested)
  Geo/SensorMath.cs                        camera heading from the orientation sensor, GPS filters
  Data/                                    Firestore + on-phone cache + bundled file, validation
  Sensors/                                 sensors service, permissions, camera optics, portrait lock
  ViewModels/ArViewerViewModel.cs, Views/ArViewerPage.xaml(.cs)
src/BCOpendayApp/Scanner/                  staff-only building scanner
  ScannerFeature.cs                        registration: builder.UseBuildingScanner()
  Services/ScannerAuthService.cs           the password gate (Firebase email + password)
  Services/ScannerDraftStore.cs            scans saved on the phone until uploaded
  ViewModels/, Views/                      Login → Home (list/export) → Capture
src/BCOpendayApp/Resources/Raw/buildings.json      bundled starter list (empty: [] until you export)
Tests/LocationService.Tests/ArTests.cs              25 tests for the AR maths and data loading
docs/ar-guide.md                                    this file
```

**Existing files that were changed for the AR feature:**

| File | Change |
|---|---|
| `src/BCOpendayApp/BCOpendayApp.csproj` | `CommunityToolkit.Maui.Camera` **4.0.0**; `Microsoft.Maui.Controls` pinned to **9.0.120** |
| `src/BCOpendayApp/MauiProgram.cs` | `using BCOpendayApp.AR; using BCOpendayApp.Scanner; using CommunityToolkit.Maui;`, `.UseMauiCommunityToolkitCamera()` straight after `.UseMauiApp<App>()`, and `builder.UseBuildingAr().UseBuildingScanner();` |
| `Platforms/Android/AndroidManifest.xml` | camera, fine/coarse location and vibrate permissions; optional hardware features |
| `Platforms/iOS/Info.plist` | camera and location usage texts |
| `Properties/FirebaseLinker.xml` | keeps the two new Firestore document classes from being trimmed |
| `Services/IDialogService.cs`, `DialogService.cs` | new `ConfirmAsync` (yes/no dialog) |
| `Views/StartTourPage.xaml`, `ViewModels/StartTourViewModel.cs` | the placeholder is replaced by **Open AR camera** |
| `Views/AboutPage.xaml`, `ViewModels/AboutViewModel.cs` | small **Staff: building scanner** button |
| `firebase/firestore.rules`, `firebase/tests/firestore.rules.test.mjs` | rules and tests for `buildings` + `scanners` |
| `Tests/LocationService.Tests/*.csproj`, `Program.cs` | compile and run the AR tests |
| `Resources/Images/qr.png` | no longer used; moved to `design-assets/unused/` (run `git rm src/BCOpendayApp/Resources/Images/qr.png`) |

---

## 3. Build in Visual Studio

1. Open `BCOpendayApp.sln`. Visual Studio restores the new NuGet packages (the CameraX bindings come in through the camera package).
2. Build the Android target. If anyone gets **"path too long"** errors, the fixes in the main README (section 1) apply unchanged.
3. Run it on a **real phone** over USB. The emulator has no real camera, GPS or compass, so you can only check that the pages open.
4. Optional: right-click `LocationService.Tests` → *Set as Startup Project* → run it (or `dotnet run --project Tests/LocationService.Tests`). It should print `47/47 tests passed` (22 old + 25 new).

**If restore fails with NU1605 (package downgrade):** the camera package needs `Microsoft.Maui.Controls` ≥ 9.0.120. That's why the csproj pins exactly that version, so check the pin is still there.
**If the build fails with MCTC001:** `.UseMauiCommunityToolkitCamera()` must stay in `MauiProgram.CreateMauiApp()`, chained after `.UseMauiApp<App>()`.
**Don't upgrade `CommunityToolkit.Maui.Camera` past 4.0.0** while the project is on .NET 9; 5.0 and later need .NET 10.

---

## 4. Firebase setup (one time, in the console at console.firebase.google.com → project `bcopenday`)

Your app already uses anonymous sign-in and Firestore. These are the additions.

### 4.1 Turn on email/password sign-in (for the scanner)
*Build → Authentication → Sign-in method → Add new provider → **Email/Password** → Enable → Save.*
Leave **Anonymous** enabled; visitors use it.

### 4.2 Create the staff scanner account(s)
1. *Authentication → Users → **Add user***, then enter an email and a strong password. This is the "scanner password".
   Use one account per person if you want to see who scanned what (`updatedBy` stores the account id).
2. Copy the account's **User UID** from the users list.
3. *Firestore Database → Data → **Start collection*** → Collection ID `scanners` → Document ID = **the UID you copied** →
   add a field `name` (string) = e.g. `Paul` → Save.

Only accounts with a document in `scanners` can unlock the scanner and change buildings. Firebase checks the
password itself, and the Firestore rules enforce the allow-list on Google's servers. Someone who decompiles
the app still cannot write buildings. To revoke access, delete that person's `scanners` document (or the user).

The app signs in with `createsUserAutomatically: false`. Plugin.Firebase's default is `true`, which would create a
new account for any email someone typed into the scanner login.

### 4.3 Publish the security rules
*Firestore Database → **Rules*** → replace the text with the contents of `firebase/firestore.rules` → **Publish**.

- Publishing **replaces** the current rules. If teammates added rules for other collections in the console, merge them in first.
- The rules still contain the old `member1_verification` block, which lets *any* signed-in visitor write to that
  collection. Remove it when it's no longer needed.

### 4.4 Check
Build the app with `Platforms/Android/google-services.json` in place, open *Tour → New AR scan* and sign in.
"This account is not allowed to use the scanner" means the `scanners/<uid>` document is missing or has the wrong ID.

---

## 5. Scanning buildings on campus (staff)

Do this **outside**, ideally on a clear day. GPS is worst right against tall walls and under trees, so expect ±5–10 m there.

1. *Tour → New AR scan →* sign in → **+ Scan building**.
2. Fill in the **name**, a one-line **summary** (shown on the AR card), optional **More info** text, and optionally
   **link a tour stop** (the AR card then gets an *Open tour stop* button).
3. Choose a method:
   - **Outline (best):** stand at an outside **corner**, watch the GPS line until it says *good* or *OK*, then tap
     **Capture corner**. Each capture averages up to 8 seconds of GPS readings. Walk along the wall to the next
     corner and repeat until every corner is captured **in order** (4 for a rectangular building; add the inner
     corners of an L-shape). If you went out of order, tap **Fix corner order**.
   - **Centre + radius (quick):** stand in the middle (or at the entrance), tap **Capture centre**, then move the
     slider until the circle covers the building. Use this for small or round buildings, or ones you can't walk around.
4. **Save and upload.** The scan is first saved on the phone, then uploaded. If there is no signal, it stays under
   *Not uploaded yet* on the scanner home; tap **Upload drafts** later.
5. Tap **Test in AR**, walk away 30–100 m, and point the phone at the building. The card should show it.
6. Repeat for each building. Tap a building in the list to edit it, rescan it, hide it (*Visible to visitors* off) or delete it.
7. **Sign out and lock** when done. The scanner also locks automatically after 45 minutes.

### Shipping the scans inside the app (offline on first launch)
Visitors normally get buildings from Firestore and keep a copy on the phone, so a phone that has been online
once also works offline. To make even a phone that has **never** been online work (for example, a visitor who
installs the APK and opens it straight away without data):

1. On the scanner home, tap **Export buildings.json** and send the file to your PC.
2. Replace `src/BCOpendayApp/Resources/Raw/buildings.json` with it and commit.
3. Rebuild the APK.

---

## 6. Visitors

Install → open the app → **Tour → Open AR camera** → allow **camera** and **location** → point at a building.

- The top bar shows GPS accuracy, the heading and how many buildings are nearby.
- If labels seem consistently off, move the phone in a **figure-8** a few times to calibrate the compass.
- With the camera permission refused, the guide still works; the visitor just doesn't see the camera image.
- Data order: Firestore → copy saved on this phone → bundled `buildings.json`. The status line says which one is in use.

---

## 7. Firestore data shape

`buildings/{id}` (id = lowercase letters, digits and dashes, e.g. `main-building`):

| Field | Type | Notes |
|---|---|---|
| `name` | string | 1–80 characters |
| `summary` | string | ≤ 200 characters, shown on the AR card |
| `description` | string | ≤ 2000 characters, shown under "More info" |
| `stopId` | string | optional tour stop id (`library`, `robotics` …) or empty |
| `centerLat`, `centerLng` | number | WGS-84 degrees |
| `radiusMeters` | number | 3–500; used for circle anchors and as the building's size |
| `footprint` | string | `"lat,lng;lat,lng;…"` corner list, or empty for centre + radius |
| `isActive` | bool | hidden from visitors when false |
| `updatedAtUnixMs`, `updatedBy` | number, string | set by the app; `updatedBy` must be the writer's uid |

You can fix a typo directly in the Firestore console, but leave `updatedBy` alone. Console edits bypass the
rules, while edits made in the app are checked against them.

---

## 8. What was and was not verified

- **Checked here:**
  - The targeting maths (ray casting, occlusion, wrap-around at north, tolerance, inside detection) was checked against an independent re-implementation.
  - The quaternion → camera-heading formula matches the examples in the official MAUI docs (upright phone facing north = heading 0, facing west = 270).
  - All XAML bindings and DI registrations were checked statically.
  - An independent code review was done for compile errors; it found 2 problems, now fixed.
  - The package versions come from nuget.org (Camera 4.0.0 is the last version for .NET 9, and Controls 9.0.120 is the last 9.0 release).
- **Not run yet:** compiling the app, the 25 new C# tests, the Firestore rules tests (they need Node + Java, `cd firebase && npm install && npm run test:rules`), and anything on a real phone on campus.
  **Do the campus walk-test before the open day.** Scan one building, then test it from several sides and distances.

## 9. Possible upgrades later

- **ARCore Geospatial API (VPS)** can sharpen position and heading to about 1 m / 1° using Google Street View
  imagery. It has no maintained .NET binding, so it would need an Android *binding library* project. That's doable
  in Visual Studio but more work, and it only helps where Street View coverage exists. The building data and
  targeting code here would be reused unchanged; only the sensor service would change.
- A per-visitor "heading offset" calibration (aim at a known building, tap to align) for phones with poor compasses.
- QR codes on building signs that open the tour stop directly (the code-entry path already exists).

## Sources
- [CommunityToolkit.Maui.Camera on NuGet](https://www.nuget.org/packages/CommunityToolkit.Maui.Camera) (4.0.0 targets net9.0; 5.x targets net10.0)
- [CameraView docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/views/camera-view)
- [Xamarin.Google.ARCore: unmaintained, last release 2022](https://libraries.io/nuget/Xamarin.Google.ARCore)
- [.NET MAUI sensors (orientation sensor frames, compass)](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/sensors)
- [Plugin.Firebase.Auth](https://www.nuget.org/packages/plugin.firebase.auth)
