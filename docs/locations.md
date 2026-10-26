# Campus locations and journey progress

## Where locations come from

Nobody types locations in. A location exists as soon as there is an **AR building scan** for it, a
**360° view** of it, or both (`Services/Campus/CampusLocation.cs`, `CampusLocationCatalog.Build`):

- Every active building from the scanner (`buildings` in Firestore) is a location. Its id, name and
  description come from the scan.
- Every online 360° view belongs to a location through its `locationId`:
  - When staff capture a view they pick **Location**: an existing one (e.g. the building they scanned) or
    **New location**, which makes a new id from the view's name (`Main Library` → `main-library`).
  - Views made before this update have no `locationId`; each is its own location.
- The **code** of a location is `BC-` + its id in capitals, e.g. `BC-MAIN-LIBRARY`. It is shown on the
  location page. Typing it on the Tour page opens that place's 360° view (or the location page, if it only
  has an AR scan).

Locations are listed on **Locations** (with search) and under the map on **Map**. Tapping one opens its page:
photo, name, description, code, **Open 360° view** and **Find it with the AR camera**.

## Location photo

One photo per location, e.g. the front of the building. Staff who are signed in see **Take photo** /
**From gallery** on the location page.

- The app turns it upright, shrinks it to at most 1280 px and 600 KB, and stores it as base64 in **one**
  Firestore document `locationPhotos/{locationId}` (no splitting needed; one document holds 1 MB).
- Phones keep a copy and check for a newer one every 6 hours. Offline, the saved copy is shown.
- Rules: any signed-in visitor can read one photo; only scanner accounts can change it.

## Journey progress (Home)

A location counts as **visited** when the visitor sees it in the AR camera (the building is recognised) or
opens one of its 360° views. Progress = visited locations / all locations. It is stored on the phone
(`VisitorProgressService`, same keys as before).

## Firebase: one-time step

Publish the updated `firebase/firestore.rules` again (Firebase console → Firestore → Rules → paste →
Publish). It adds `locationPhotos` and allows the new `locationId` / `description` fields on 360° views.
Until it is published, uploading a view with a location or adding a photo is refused.

## What changed in the screens

- **Tour tab** = the old "Start AR Tour" page, now **Start Tour**: AR camera, 360° view, code, choose from
  locations, and staff buttons **New AR scan** / **New 360° view** (they ask for the staff sign-in).
  The old Tour tab page (`TourPage`) was only reachable from the tab, so it was removed.
- **Map**: numbered pins removed (backup: `docs/backups/restore-map-pins.patch`). Back now returns to Home
  on the Map and Tour tabs.
- **About**: 360 virtual tour and staff scanner buttons removed; *Visit the Belgium Campus website* added;
  *Local panorama drafts* kept for now.
- The old placeholder stop pages (stop viewer, info, gallery) are kept but nothing links to them any more.
