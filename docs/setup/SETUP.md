# Getting everything working: checklist

Do these steps in order. Background detail is in [`README.md`](README.md), [`docs/ar-guide.md`](docs/ar-guide.md)
(AR + scanner) and [`docs/location-data.md`](docs/location-data.md) (tour data, map pins, Firebase).

This zip is the **complete project**. It already contains the clean structure, the Firebase/location work, the
latest map pins from `feature/map-pins` (PR #13), and the AR guide + staff scanner.
It does **not** contain `google-services.json`; that file stays private.

---

## A. Put it into GitHub (you, once)

Your teammates are still committing to the old `MAUIProject/` layout on `Development`. Every change made there
has to be ported by hand, as this one was. So the most important step is to get **everyone onto the new layout**.

1. Ask the team to **push everything** and not commit for a while.
2. In PowerShell, in your local clone, on **your branch** (the one with the restructure):
   ```powershell
   git fetch origin
   git merge origin/Development      # brings in their history; conflicts in old MAUIProject files are expected
   ```
   Don't resolve conflicts by hand. The next step replaces the files anyway.
3. **Back up** `src\BCOpendayApp\Platforms\Android\google-services.json` if you have it, then delete the old folders:
   ```powershell
   Remove-Item -Recurse -Force MAUIProject, Evidence -ErrorAction SilentlyContinue
   Remove-Item -Force MEMBERS_3_4_*.md, MEMBERS_3_4_FILES.txt -ErrorAction SilentlyContinue
   ```
4. Extract this zip's `PRJ381` folder **contents** into the repo root and choose *Replace* for everything.
   Keep the `.git` folder.
5. Stage and check:
   ```powershell
   git add -A
   git status
   ```
   There must be **no** `google-services`, `bin/`, `obj/`, `.vs/` or `node_modules/` entries in the list.
   If `git status` still shows *unmerged* paths, run `git add -A` again.
6. `git commit -m "Restructure + Firebase + map pins + AR guide and scanner"`, push, and open a PR into
   `Development` (then into `main`).
7. Tell everyone: after it's merged, **pull, close Visual Studio, delete the old local `MAUIProject` folder**, and from
   then on work only in `src/BCOpendayApp/`. Any open branch that changes `MAUIProject/...` must be rebased, with its
   changes re-applied in the new location.

## B. Each developer's machine

1. **Visual Studio 2022 17.14+** with the *.NET MAUI* workload. Clone into a short path, e.g. `C:\dev\PRJ381`.
2. **Firebase config:** save the team's `google-services.json` as exactly
   `src\BCOpendayApp\Platforms\Android\google-services.json`.
   - ⚠ The archive you sent had it as **`google-services(2).json`**. With that name, Firebase is silently **off**
     (offline data only, no scanner). Rename it.
   - Without the file the app still builds and runs, using the offline data; only Firebase features are missing.
3. Open `BCOpendayApp.sln` and let NuGet restore. If anyone gets **"path too long"**: enable long paths, set
   `NUGET_PACKAGES=C:\nuget`, then delete `bin`/`obj` (README section 1).
4. Build and run on a **real Android phone** over USB. The AR needs a real camera, GPS and compass.
5. Optional: run the `LocationService.Tests` project. Expect **47/47** to pass.

## C. Firebase console (one person, once). Project `bcopenday` at console.firebase.google.com

Visitors' anonymous sign-in and the `locations` data should already be set up by the team.
These steps are for the AR scanner:

1. **Authentication → Sign-in method → Add new provider → Email/Password → Enable.** Keep *Anonymous* enabled.
2. **Authentication → Users → Add user.** Enter the staff email and a strong password; this is the **scanner password**.
   Copy the account's **User UID**.
3. **Firestore Database → Data → Start collection** `scanners` → **Document ID = that UID** → add a field `name` (string) → Save.
   Repeat steps 2–3 for each staff member who should be able to scan.
4. **Firestore Database → Rules:** paste the whole of `firebase/firestore.rules` → **Publish**.
   This replaces the current rules. If someone added rules in the console for other collections, merge them first.
   The rules still include the old `member1_verification` block, which any visitor can write to. Delete it if nobody needs it.
5. Optional, if Node + Java are installed (a teammate already ran these): `cd firebase`, `npm install`, `npm run test:rules`.
   Expect **22** passing tests: 12 old ones and 10 new ones for buildings and scanners.

## D. Scan the buildings (on campus, outdoors)

1. Open the app → **About → Staff: building scanner** → sign in.
2. **+ Scan building**. Enter the name and summary, and optionally more info and a linked tour stop.
3. **Outline:** stand at each outside corner, wait for the GPS to show *good* or *OK*, then **Capture corner**. Go round
   the building in order. Or use **Centre + radius** for small or round buildings.
4. **Save and upload**, then **Test in AR** from 30–100 m away, from a couple of sides.
5. When everything is scanned, use **Export buildings.json**, replace `src/BCOpendayApp/Resources/Raw/buildings.json`
   with the exported file, commit, and rebuild. Then even a phone that has never been online has the buildings.

## E. Check before the open day

- [ ] App builds without `google-services.json` and shows the bundled offline locations
- [ ] With the file, the status says *Locations loaded from Firestore*
- [ ] Map: Library and Student Lounge pins on **Main**; Robotics and Manufacturing pins side by side on **North** (The Hangar);
      Lecture Hall says it has no single pin
- [ ] Start AR Tour → **Open AR camera**: camera shows, the top bar shows GPS ±m and a heading, and pointing at a scanned building
      shows its card (and the floating label)
- [ ] Scanner: wrong password is refused; an account not in `scanners` is refused; Sign out and lock works
- [ ] Scanner: walking from one corner to the next, the GPS line keeps updating (no need for "Test in AR")
- [ ] 360° tour (About → 360 virtual tour): moving the phone turns the view; the chip switches to swiping
- [ ] 360° tour → **VR headset**: side-by-side view, follows your head; looking at an arrow for 1.5 s opens that view;
      Back leaves VR
- [ ] Staff tools → 360° views → capture a view at two spots ~20 m apart: each shows an arrow to the other,
      pointing the right way (see `docs/panoramas.md`)
- [ ] After publishing the new `firestore.rules`: a captured view says "It is online", and a SECOND phone sees it
      in About → 360 virtual tour without a rebuild
- [ ] Airplane mode: tour, map and AR still work with the saved or bundled data
- [ ] A teammate who previously had restore errors can build

## What changed in this update

- Ported from `Development` (PR #13, *feature/map-pins*): map pins for Robotics Lab, Manufacturing Lab and Student Lounge,
  the new Lecture Hall note, the updated C#/Node tests, and the refreshed test evidence (`docs/evidence/`, including the
  rules-test run: 12/12 passed).
- AR guide (`src/BCOpendayApp/AR/`) and staff scanner (`src/BCOpendayApp/Scanner/`) are now part of the project.
- `.gitignore`: added more Firebase emulator logs and `Generated Files/`. `google-services*.json` (including the
  `(2)` copies) was already ignored. A simulated commit confirmed that no secrets or build output are picked up and that
  no file the build needs is ignored.
- Map page: correct wording when only one stop has no pin.

**Status:** nothing could be compiled here (no .NET SDK is available in this environment). Expect a small compile fix or two
on the first build. If you get build errors, paste them and I'll fix them.
