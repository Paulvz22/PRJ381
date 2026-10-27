# Backups

## restore-map-pins.patch

The Map page used to draw numbered pins (from the old placeholder tour stops in `locations.json`) on top of
the campus map image. They were removed because the map image already shows the campus layout. This patch
puts the pins back exactly as they were (it only touches `MapPage.xaml`, `MapPage.xaml.cs` and
`MapViewModel.cs`).

From the repository root:

```
git apply docs/backups/restore-map-pins.patch
```

Notes:
- The restored map lists the old placeholder stops again instead of the new campus locations.
- The restored page no longer sends the phone's Back button to Home; copy `OnBackButtonPressed` from the
  current `MapPage.xaml.cs` if you want to keep that.
- To undo it again: `git checkout -- src/BCOpendayApp/Views/MapPage.xaml src/BCOpendayApp/Views/MapPage.xaml.cs src/BCOpendayApp/ViewModels/MapViewModel.cs`
