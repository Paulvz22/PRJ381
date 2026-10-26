# 360° views: capture, arrows and look-around

This covers the 360° tour (`Views/PanoramaPage`) and the staff tools in `src/BCOpendayApp/Panorama/`.

## What visitors get

- **Tour → Open 360° view** opens the tour (or type a location code / choose from locations to open one place). It shows the bundled scenes from `Resources/Raw/pano/tour.json`,
  plus any views saved on this phone.
- **Look around by moving the phone.** This is on by default. Tap the **Move phone** chip (top right) to switch
  to swiping instead. Swiping while it is on also works: the view simply continues from where you swiped to.
  It is relative, so wherever the phone points when the view opens is the starting direction. That way it also
  works when the visitor is not on campus.
- **New views appear automatically**: views staff capture are put online and show up in every visitor's tour
  (downloaded once per phone, then available offline).
- **Arrows between views** work like Google Street View. From each view, the nearest other view in every
  direction gets a glowing arrow that points the way it really is, labelled with its name and distance. A view
  farther away in roughly the same direction is skipped, because you reach it through the nearer one. Tapping
  an arrow opens that view, facing the way you walked.

## VR mode (phone in a cheap headset)

For Cardboard-style viewers such as the Voyager / "VR Box" headsets, which are just lenses with no buttons:

1. Open the 360 tour and tap **VR headset** (top right).
2. The phone switches to landscape and full screen, shows the view twice side by side (one per eye), and keeps
   the screen on. Put it in the headset.
3. **Look around** by turning your head.
4. **To walk to another view, look at its arrow.** A yellow ring fills up and the view's name appears under
   the centre dot. After **1.5 seconds** the next view opens, still in VR. Looking away before the ring is full
   cancels it. Headsets that have a "tap the screen" button can also press it to go straight away.
5. **Press Back** (or swipe back) to leave VR.

Arrow names now also show all the time in the normal view, not only when tapped.

How it works: `Resources/Raw/pano/bc-viewer.js` and `bc-viewer.css`. A 360° photo has no depth, so both eyes
get the same image from two Pannellum viewers that are kept pointing the same way. "Looked at" means the arrow
is within 7° of the centre of view. The 1.5 s and 7° are constants at the top of `bc-viewer.js`.

Not covered yet: lens distortion correction (cheap lenses work fine without it), and proper headsets such as the
Quest 2, which would need WebXR or a native VR app.

## For staff: capturing a new view (Android)

1. **Tour → New 360° view** → sign in with the staff account (the same account as the building scanner).
2. Pick the **Location** the view belongs to (or *New location*) and optionally add a description
   (see [locations.md](locations.md)).
3. Stand on the spot, outdoors if possible, and wait for the GPS line at the top. The views' map pins come
   from it.
4. Type a name (e.g. *Library entrance*). Hold the phone **upright at eye level** and tap **Start**.
5. **Turn slowly on the spot.** Follow the arrow; a photo is taken automatically each time the phone points at
   the next target and is held still. It buzzes after each photo. There are 10–12 photos for a full circle.
   - If it keeps saying *Hold still*, stop turning for a second.
   - If it says *Tilt up/down*, aim the middle of the screen at the horizon.
   - **Take photo now** forces a photo when the automatic one does not trigger.
   - Turn **with your body, around the phone**, not by swinging your arm. Swinging moves the camera sideways
     and breaks the joins for nearby objects.
6. After the last photo, the app joins the photos (a few seconds) and opens the new view. Arrows to and from
   views within 150 m appear immediately.

### How the joining works (for the report)

- Each photo is placed at the compass heading measured when it was taken.
- Phone compasses are typically off by a few degrees, so neighbouring photos are compared where they overlap
  (normalised cross-correlation). Each pair is shifted by the angle that matches best, and the error left over
  around the full circle is spread evenly.
- Pairs that do not match convincingly keep their compass position instead of a guessed one. Examples are
  blank walls and sky.
- The result is a 360° × ~65° strip, 4096 px wide. Its centre column faces true north, so the panorama's yaw
  equals the real compass heading.
- Code: `Panorama/Capture/PanoramaStitcher.cs`, which is pure C# and unit-tested on a synthetic scene with
  known answers. Photo decode and encode use Android's own Bitmap APIs (`PanoramaFrameCodec.cs`), so no
  image library was added.
- Limits:
  - Only the band around the horizon is covered, not straight up or down. Phones can't capture that well
    without a tripod.
  - People walking through the shot can appear twice.
  - Very close objects (under ~2 m) may show small seams.
  - Capture works on Android only.

## Where views are stored: online in Firestore (prototype)

When a view is captured, it is saved on the phone **and uploaded straight away**. Every visitor's 360° tour then
shows it the next time they open the tour. No app rebuild is needed. If the upload fails (no signal, Firebase
not set up), the view stays on the phone; use **Upload** in the 360° views list later.

How it is stored (`Panorama/Services/PanoramaChunks.cs`, `FirestorePanoramaStore`):

| Firestore path | What's in it |
|---|---|
| `panoramas/{id}` | name, size, vertical field of view, GPS position, north offset, `chunkCount`, `byteLength`, `sha256`, `uploadId` |
| `panoramas/{id}/chunks/0` … `/15` | the JPEG as base64 text, up to 700,000 characters (≈525 KB of image) per document |

- **Why pieces?** A Firestore document holds at most 1 MB, and Cloud Storage needs Firebase's paid Blaze plan.
  A captured view is 1–3 MB, so it is 2–6 pieces. The maximum is 16 pieces (8.4 MB), so very large imported
  panoramas cannot go online.
- **Upload order.** All pieces are written first, then the `panoramas/{id}` document. Visitors only ever see
  complete views. Every piece carries the upload's `uploadId`, and the joined image must match `sha256`, so a
  half-finished re-upload is never shown.
- **On the visitor's phone** (`PanoramaOnlineLibrary`):
  - The small list of views is fetched each time the tour opens.
  - An image is downloaded the first time its view is opened, then kept until staff upload a new version.
  - Offline, the tour uses whatever the phone has already seen.
- **Security** (`firebase/firestore.rules`): any signed-in visitor (the app's anonymous sign-in) can read.
  Only staff accounts on the `scanners` list can upload or remove, the same as for buildings. Field names,
  sizes and piece numbers are checked by the rules.
- **Staff screen** (Staff tools → 360° views): **Upload / Re-upload** on each view on the phone, and an
  **Online views** list with **Remove** (removes it for every visitor).
- **Free-plan limits** (Firestore Spark, check the current numbers in the Firebase console):
  - roughly 1 GB stored;
  - 50,000 document reads a day: opening a view costs 1 read per piece, once per phone;
  - 10 GB of downloads a month.

  A few hundred views and a busy open day fit, but this is a prototype choice. Moving to Cloud Storage
  later only means swapping `FirestorePanoramaStore` for a storage-based `IPanoramaRemoteStore`.

**Bundling a view into the app** still works as a backup, for example so the tour works with no signal on
the first launch. Use **Export** on a view to get the image and a ready-made `tour.json` scene:

1. Copy the `.jpg` into `src/BCOpendayApp/Resources/Raw/pano/scenes/`.
2. Paste the scene into the `"scenes"` object of `Resources/Raw/pano/tour.json`.
3. Rebuild the app.

**Bundled scenes and arrows.** The two original scenes (`spot-1`, `spot-2`) have no GPS position, so they keep
only their hand-made arrows. Hand-made arrows in `tour.json` (`"type": "scene", "sceneId": ...`) still work.
Give any bundled scene these three fields to join the automatic arrows:

- `"lat"` and `"lng"`: the GPS position.
- `"northOffset"`: the compass heading shown at yaw 0.

**One-time setup.** Publish the updated `firebase/firestore.rules` (Firebase console → Firestore → Rules →
paste → Publish). Until then, uploads are refused. Staff accounts are the existing scanner accounts; nothing
else to create.

## Fixed in the same update: scanner GPS stuck on one position

On Android, .NET MAUI's live location listener (`Geolocation.StartListeningForegroundAsync`) passes its accuracy
setting (50 m for "Best") to Android as the **minimum distance between updates**. The phone therefore only
reported a new position after moving 50 m, which is more than the distance between two corners of a building.
"Test in AR" seemed to fix it because restarting the sensors fetched a fresh last-known position.

`AR/Sensors/AndroidLocationFeed.cs` now asks Android's `LocationManager` directly for one GPS fix per second,
with minimum distance 0. This also makes the AR guide's position update continuously while walking.
