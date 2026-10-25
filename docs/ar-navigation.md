# AR navigation ("Go to...") - how it works

Visitors can tap **Go to...** in the AR camera view, pick a building, and then follow an on-screen banner, a
rotating arrow and a marker on the destination until they arrive. It sits on top of the existing AR guide
([`ar-guide.md`](ar-guide.md)) and reuses its GPS position and camera heading.

---

## 1. What the visitor sees

1. Tap **Go to...**. A list of buildings appears, each with its distance from the visitor.
2. Tap a building. It becomes the destination and guidance starts.
3. While walking, the app shows:
   - a **banner** with a short instruction (for example "Bear right") and the distance to the destination,
   - an **arrow** that rotates to point toward the destination relative to where the camera is facing,
   - a **marker** drawn on the destination building in the camera view.
4. When the visitor is close enough, the banner changes to "You have arrived at [building name]".
5. Stopping navigation clears the destination and hides all guidance.

---

## 2. How the guidance works

The maths is in `src/BCOpendayApp/AR/Nav/NavGuidance.cs` (namespace `BCOpendayApp.AR.Geo`). It is pure C#
with no MAUI types, so it is unit-tested in the console test project.

`NavGuidance.Next(user, headingDegrees, route, startIndex, gpsAccuracyMeters)` takes the visitor's GPS
position, the camera heading, a list of waypoints (the last one is the destination), the index of the waypoint
being walked to, and the current GPS accuracy. It returns a `GuidanceStep` containing:

| Field | Meaning |
|---|---|
| `NextIndex` | Waypoint being walked to. Fed back in as `startIndex` on the next call. |
| `Next` | That waypoint's position. |
| `DistanceToNextMeters` | Distance from the visitor to that waypoint. |
| `RemainingMeters` | Distance to the next waypoint plus the lengths of all remaining legs. |
| `BearingToNext` | Compass bearing from the visitor to the next waypoint. |
| `TurnDegrees` | Signed angle from where the camera points to the next waypoint (negative = left, positive = right). |
| `Arrived` | True when on the last waypoint and within the arrival radius. |

**Steps in `Next`:**

1. An empty route returns `null`.
2. The arrival radius is calculated from the GPS accuracy (below).
3. Starting at `startIndex`, any waypoint within the arrival radius is skipped, so progress only moves forward
   and never goes back below `startIndex`.
4. Distance and bearing to the next waypoint are calculated, and the turn angle is the difference between that
   bearing and the camera heading (wrapped correctly across north).

**Arrival radius:** the GPS accuracy in metres, clamped between 8 m and 25 m. If the accuracy is unknown
(NaN) it uses 8 m. This stops the visitor being told they have not arrived when GPS error is larger than
the distance left, without ever being too generous.

**Instruction text** (`NavGuidance.Describe`), based on the size of the turn:

| Turn size | Instruction |
|---|---|
| up to 20 degrees | Straight ahead |
| 21 to 60 degrees | Bear left / Bear right |
| 61 to 135 degrees | Turn left / Turn right |
| over 135 degrees | Turn around |

---

## 3. Where the code is

| Piece | Location |
|---|---|
| Guidance maths | `src/BCOpendayApp/AR/Nav/NavGuidance.cs` |
| State and banner text | `src/BCOpendayApp/AR/ViewModels/ArViewerViewModel.cs` (destination list, `UpdateNavigation`, `StopNavigating`) |
| Banner, arrow, marker, destination list | `src/BCOpendayApp/AR/Views/ArViewerPage.xaml` and `.xaml.cs` |
| Unit tests | `Tests/LocationService.Tests/NavTests.cs` |

`ArViewerViewModel` calls `UpdateNavigation()` whenever the position or heading updates. It passes the route to
`NavGuidance.Next`, then updates the instruction, distance text, arrow angle and arrival message.
The page uses `DestinationId` to decide which building gets the marker.

---

## 4. Testing

The guidance maths has unit tests in `NavTests.cs`, run with `dotnet run --project Tests/LocationService.Tests`:


There are 10 nav tests in `NavTests.cs`: target straight ahead, left/right turns depending on heading, turn angle wrapping across north, reached waypoints being skipped, progress never going backwards, remaining distance adding up the legs, arrival only at the last waypoint inside the radius, weak GPS widening the arrival radius (capped at 25 m), an empty route giving no guidance, and the turn text. The whole suite passes: 57/57 on 16 October 2026.

The UI was tested by hand on a Samsung Galaxy Note 9 (Android). Destinations tested: the library and a residence building.

---

## 5. Limitations (honest list)

- **The route is a straight line.** At the moment the only waypoint is the centre of the destination
  building (`ArViewerViewModel`). The arrow points directly at the building and does not follow paths,
  avoid obstacles or know about roads, gates or walkways.
- **Accuracy depends on GPS and compass.** The same limits as the AR guide apply: GPS is typically 3-10 m
  accurate and nearby metal disturbs the compass, so the arrow can be off by several degrees.
- **Arrival is judged by the building's saved centre.** If a building's saved location is slightly off, the
  arrival point is too. Re-scanning a building fixes this.
- **Outdoors only**, within the same range as the AR guide.
- No indoor navigation and no spoken instructions.

---

## 6. Future work: waypoint graph and Dijkstra routing

To follow real paths, the route would come from a graph of campus waypoints instead of one straight leg.
Planned design (not built yet):

- `Resources/Raw/nav-graph.json` with nodes `{id, lat, lng, buildingId?}` and edges `{from, to}`.
- Edge distances computed when the file is loaded.
- A `PriorityQueue`-based Dijkstra in `AR/Nav/NavRouter.cs`, written test-first.
- `ArViewerViewModel` would ask the router for the waypoint list and pass it to `NavGuidance.Next`, which
  already supports multi-waypoint routes.
- Real campus waypoints would then be recorded on site.

---

## 7. References and design inspiration

- Karl Cyrus, **NAvSU - Augmented Reality Navigation System** (Unity, Niantic Lightship WPS, Dijkstra routing).
  https://github.com/karlcyrus/navsu-augmented-reality-navigation-system (accessed 16 October 2026).
  Used as **design inspiration only** (the idea of AR guidance on a routed path). The repository has no
  licence file and states "All rights reserved", so **no code was copied or adapted**. The guidance maths
  here was written independently.
- Niantic Lightship was considered for positioning but not used: it is aimed at Unity, no .NET MAUI binding
  was found, WPS is experimental, and VPS needs Niantic-approved scanned locations. TODO: add the exact
  Lightship documentation URLs you read.