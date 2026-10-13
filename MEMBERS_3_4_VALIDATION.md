# Members 3 and 4 - verification record

Prepared: 13 October 2026. Target archive: `BCOpenDay_Maps_Reconfigured.zip`.

## Executed successfully

| Check | Result |
| --- | --- |
| Built-in Node data / seed-tooling tests | **26 passed, 0 failed** in this project tree |
| Seed versus bundled JSON validation | Identical five records; valid known IDs, image filenames, fields and coordinate states |
| Seed tool dry run | Successful; **zero Firestore writes**; no credentials or Admin SDK needed |
| JavaScript syntax | All 5 `.mjs` files passed `node --check` |
| XML / XAML / project / entitlement syntax | 12 new or changed files parsed successfully |
| C# lexical delimiter check | 30 changed files checked; NOT a compiler or type check |
| Page integration static checks | All seven data-backed XAML pages match their C# base/class and DI registrations |
| Resource checks | All five location images and all three map images exist; map dimensions match the coordinate layout metadata |
| Legacy catalogue checks | No remaining `CampusStop`, `CampusCatalog.Stops` or `CampusCatalog.Find` usages in the app source |
| Test-source references | The C# test project links to the actual new service/model sources and bundled JSON |
| Unchanged original files | 174 files outside the update list verified byte-for-byte against the supplied archive |

The same 26 data/tooling tests were run in each project tree; this is one 26-test suite, not 52 distinct tests.
The command output is included in `Tests/Validation/data-tooling-output.txt`.

## Included but NOT executed

- **22 C# repository/geometry tests.** No .NET SDK/compiler is installed in this container. They have
  not been compiled or run; execute `dotnet run --project Tests/LocationService.Tests/LocationService.Tests.csproj`.
- **12 Firestore rules tests.** The Firebase testing packages/CLI and emulator were not installed.
  No emulator execution or deployed-rule evaluation has occurred. Follow `firebase/README.md`.
- **Full MAUI compilation, native Firebase startup and device UI checks.** The required .NET MAUI,
  Android/iOS SDKs, signing and device environment were not available. Static checks are not a substitute
  for these builds or on-device acceptance checks.

## Cloud and map limitations

Nothing was deployed to a real Firebase project and the five documents were not uploaded. The delivered
seed data and guarded seed script prepare that step. Supply your own genuine Firebase app configuration,
enable anonymous authentication, review/deploy the rules and run the seeder explicitly.

Only the Library's labelled position can be referenced from the supplied Main Campus image. Its pin is
approximate image-based information, not surveyed/GPS data. The four other stops are connected to the
service, directory, tour and detail pages, but their map coordinates remain unassigned rather than invented.

## Applying this package

This is a source overlay. Merge its `BCOpenDay` folder contents into the matching original project and
replace included files. Keep all other files, assets, solution files and Git history. Do not delete your
original project and do not create a nested duplicate project folder. The file manifest lists every delivered
path. No font files, credentials, generated build output or dependency folders are included.
