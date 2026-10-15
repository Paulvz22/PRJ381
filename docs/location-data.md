# Location data, Firebase and the map

How the tour stops get into the app, how to change them, and what has been verified.
(Condensed from the Members 3 & 4 hand-over notes; paths updated for the `src/` layout.)

## Data flow

```
Page -> ViewModel (CatalogViewModelBase) -> ILocationService -> LocationService
          LocationService -> FirestoreLocationSource -> IAuthService (anonymous sign-in) + IFirestoreService
          LocationService -> Resources/Raw/locations.json   (bundled offline copy)
```

- A successful Firestore read is reused for **5 minutes**; the *Refresh locations* button skips that wait.
- A failed read is retried after **30 seconds**, on Refresh, or straight away when connectivity changes.
- The whole remote read is capped at **6 seconds**; leaving a page cancels its load.
- A missing, invalid or duplicate Firestore document never removes a stop: the bundled record fills the gap.
- The "last good" cache is **memory only**. A fresh launch with no internet uses the bundled JSON - no prior
  sign-in needed. Every data page shows where its data came from (`CatalogStatusView`).
- Visited / favourite flags are local `Preferences` (`visited_<id>`, `saved_<id>`), not Firestore.

Files: `src/BCOpendayApp/Services/Locations/*`, `Services/Firebase/*`, `Models/Hotspot.cs`,
`Models/FirestoreHotspotDocument.cs`, `Resources/Raw/locations.json`.

## The five stops

IDs are permanent (they are Firestore document IDs, Preferences keys and the `BC-xxx` codes):
`robotics`, `manufacturing`, `lecture`, `library`, `lounge`. Stable IDs, images and map metadata are in
`Services/Locations/CampusCatalog.cs`; all text lives in the JSON / Firestore.

## Map pins - accuracy

Pins use **normalised image coordinates** (`x`, `y` between 0 and 1 of that map image), not GPS.
The map image is laid out at its original aspect ratio (`CampusCatalog.Maps` holds the pixel sizes), so pins
stay attached when zooming.

Pins (approximate reference points read off the supplied map images – **not** surveyed or GPS positions):

| Stop | Map | x, y |
|---|---|---|
| Library | Main | 0.551, 0.126 (labelled on the image) |
| Student Lounge | Main | 0.693, 0.735 (the best-known lounge; there are others) |
| Robotics Lab | North | 0.765, 0.726 (The Hangar) |
| Manufacturing Lab | North | 0.827, 0.726 (also The Hangar – drawn beside Robotics) |
| Lecture Hall | – | no pin: lecture rooms are spread over the Main and North buildings |

**To add a pin:** edit the stop in BOTH `firebase/locations.seed.json` and
`src/BCOpendayApp/Resources/Raw/locations.json` (they must match - `node firebase/scripts/validate.mjs`
checks this): set `mapSection` (`main` / `north` / `west`), `x`, `y` and `positionNote`. Rebuild the app and
re-seed with `--apply --overwrite`. Editing Firestore alone does not update the offline copy in the app.

## Security model

- Visitors sign in anonymously and may only **read** `locations`. The app never writes catalogue data.
- Only trusted admin tooling (`firebase/scripts/seed-locations.mjs` with Admin SDK credentials kept OUTSIDE
  the repo) writes. The seeder is a dry run unless `--apply`; it never deletes.
- **Deploying `firebase/firestore.rules` replaces the project's rules.** Merge other members' collections first.
- The rules currently also allow any signed-in user (which includes every anonymous visitor) to read **and
  write** `member1_verification`. That was an early test collection; remove that block once it is no longer
  needed. It is not covered by the rules tests.

## Verification

| Check | How | Status |
|---|---|---|
| Data / seed tooling (26 tests) | `node firebase/scripts/validate.mjs` and `node --test firebase/tests/data.test.mjs` | Pass (re-run after the restructure) |
| C# service + map-geometry tests (22) | `dotnet run --project Tests/LocationService.Tests` | Passed on a teammate's machine before the restructure (`docs/evidence/csharp-tests.txt`); re-run them |
| Firestore rules tests | `cd firebase && npm install && npm run test:rules` (needs a JDK for the emulator) | The original 12 passed on a teammate's PC (`docs/evidence/rules-tests.txt`); the 10 new building/scanner tests have not been run yet |
| App build + device checks | see the main README | Not run yet |

`docs/evidence/` holds the teammates' original test output (from the old `MAUIProject/` layout).

## On-device acceptance checks

- No Firebase config file: all five stops work from the bundled JSON.
- Airplane mode on first launch: the same five stops load from JSON.
- Firebase configured, anonymous auth on, rules deployed, data seeded: status says *Locations loaded from Firestore.*
- Delete one Firestore document: that stop still appears (bundled fallback).
- Turn internet off after a successful load: the page shows the fallback status and stays usable.
- Map: switching Main/North/West and zooming keeps every pin on its building; tapping one opens its info.
- Lecture Hall (no position) is explained on the Map page, not shown at a wrong coordinate.

## References

- MAUI bundled files: https://learn.microsoft.com/dotnet/maui/platform-integration/storage/file-system-helpers
- Plugin.Firebase.Firestore: https://www.nuget.org/packages/Plugin.Firebase.Firestore/4.0.0
- Firestore rules conditions: https://firebase.google.com/docs/firestore/security/rules-conditions
- Rules unit tests: https://firebase.google.com/docs/rules/unit-tests
- Admin SDK setup: https://firebase.google.com/docs/admin/setup
