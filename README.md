# PRJ381 — Belgium Campus Open Day App

An Android app that guides visitors around Belgium Campus during Open Day, and gives students a way to
explore campus even if they can't attend in person.

The app has three main parts:

- **A self-guided campus tour** — campus stops with photos, descriptions and audio narration, a campus map
  with pins, and a progress tracker, all driven by data in Firebase Firestore with a bundled offline copy
  so the app always works even with no signal.
- **An AR building guide** — point the phone's camera at a building and its name and information appear on
  screen, worked out from the phone's GPS and compass rather than a printed marker or a 3D scan. Behind it
  is a password-protected **staff scanner** that staff use to capture each building's location once, ahead
  of time.
- **A 360° virtual tour** — look around each spot by moving the phone, and follow Street-View-style arrows to
  the next view. Staff capture new views with the phone camera inside the app (same staff sign-in); arrows
  between views are generated from each view's GPS position, and new views go online for every visitor. A VR mode works with cheap phone-in-headset viewers (look at an
  arrow to walk there).

## Why it's built this way

The original plan used Google ARCore (via Unity) and QR-code markers to trigger AR content. That approach
was abandoned during development — the ARCore .NET binding is unmaintained, and surface detection was
unreliable in testing. The app now uses **.NET MAUI** end to end, with AR done through GPS position +
compass heading + the phone's live camera feed, matched against building outlines captured by staff with
the in-app scanner. No printed markers, no separate game engine, and no 3D scanning hardware required —
everything is built from inside Visual Studio.

- **Platform:** .NET MAUI, target **.NET 9 / Android** (iOS builds on a Mac; Windows is opt-in)
- **Backend:** Firebase (Firestore + Authentication), entirely optional — the app works fully offline from
  a bundled copy of the data, and Firebase switches on automatically once its config file is present
- **No ARCore, no Unity, no QR codes, no 360° camera or VR headset** — see [`docs/ar-guide.md`](docs/ar-guide.md)
  for the full reasoning and how the AR targeting actually works

## Project layout

```
src/BCOpendayApp/     the app itself (MVVM: Views, ViewModels, Services, Models)
  AR/                 the AR building guide (camera + GPS + orientation)
  Scanner/             the staff-only building scanner
firebase/              Firestore rules, seed data and tests
Tests/                 C# unit tests (geometry, AR targeting, data validation)
docs/                  ar-guide.md, location-data.md and other technical notes
```

## Getting it running (first steps)

1. Install **Visual Studio 2022 (17.14+)** with the **.NET Multi-platform App UI development** workload.
2. Clone the repo into a short path, e.g. `C:\dev\PRJ381`.
3. Open `BCOpendayApp.sln`, pick an Android emulator or a USB-connected phone, and press Run.

That's enough to build and run the app on the bundled offline tour data — Firebase and the AR/scanner
Firebase sign-in are optional extras on top of this.

**For everything else** — Firebase setup, troubleshooting restores, moving the team onto this structure,
scanning buildings, and the full pre-Open-Day checklist — see **[`docs/setup/SETUP.md`](docs/setup/SETUP.md)**.

## Documentation

- [`docs/setup/SETUP.md`](docs/setup/SETUP.md) — full setup, build and deployment guide
- [`docs/ar-guide.md`](docs/ar-guide.md) — how the AR guide and staff scanner work, and why
- [`docs/panoramas.md`](docs/panoramas.md) — the 360° tour: capturing views in the app, automatic arrows, look-around by moving the phone
- [`docs/location-data.md`](docs/location-data.md) — tour/location data, map pins, and the Firestore data flow
- [`firebase/README.md`](firebase/README.md) — Firestore rules and seed data
