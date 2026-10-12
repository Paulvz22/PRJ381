using Microsoft.Maui.Storage;
namespace BCOpendayApp.Services;

public static class VisitorStore
{
    public static bool LargeText { get => Preferences.Default.Get("largeText", false); set => Preferences.Default.Set("largeText", value); }
    public static bool Narration { get => Preferences.Default.Get("narration", true); set => Preferences.Default.Set("narration", value); }
    public static string Name { get => Preferences.Default.Get("visitorName", ""); set => Preferences.Default.Set("visitorName", value); }
    public static string Interest { get => Preferences.Default.Get("interest", "Exploring my options"); set => Preferences.Default.Set("interest", value); }
    public static bool Visited(string id) => Preferences.Default.Get("visited_" + id, false);
    public static bool Saved(string id) => Preferences.Default.Get("saved_" + id, false);
    public static void MarkVisited(string id) => Preferences.Default.Set("visited_" + id, true);
    public static void Save(string id, bool value) => Preferences.Default.Set("saved_" + id, value);
    public static int Completed => CampusCatalog.Stops.Count(s => Visited(s.Id));
    public static void ResetProgress() { foreach (var s in CampusCatalog.Stops) Preferences.Default.Remove("visited_" + s.Id); }
}
