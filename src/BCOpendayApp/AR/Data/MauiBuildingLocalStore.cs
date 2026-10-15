namespace BCOpendayApp.AR.Data;

/// <summary>Bundled file = Resources/Raw/buildings.json; cache = buildings-cache.json in the app's private data folder.</summary>
public sealed class MauiBuildingLocalStore : IBuildingLocalStore
{
    private static string CachePath => Path.Combine(FileSystem.Current.AppDataDirectory, "buildings-cache.json");

    public bool IsInternetAvailable => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public async Task<string?> ReadBundledAsync()
    {
        if (!await FileSystem.Current.AppPackageFileExistsAsync("buildings.json"))
        {
            return null;
        }

        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync("buildings.json");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public async Task<string?> ReadCacheAsync() =>
        File.Exists(CachePath) ? await File.ReadAllTextAsync(CachePath) : null;

    public async Task WriteCacheAsync(string json)
    {
        // Write to a temp file first so a crash mid-write cannot leave a half-written cache behind.
        var temp = CachePath + ".tmp";
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, CachePath, overwrite: true);
    }
}
