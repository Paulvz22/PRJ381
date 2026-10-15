using System.Text.Json;
using BCOpendayApp.AR.Data;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.Scanner.Services;

/// <summary>
/// Every scan is saved on the phone BEFORE uploading, so a failed upload (no signal on campus, session expired)
/// never loses a walk around a building. Uploaded drafts are removed; the rest can be retried from the scanner home.
/// </summary>
public sealed class ScannerDraftStore
{
    private static string FilePath => Path.Combine(FileSystem.Current.AppDataDirectory, "scanner-drafts.json");

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<BuildingAnchor>> LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            return await ReadAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public Task UpsertAsync(BuildingAnchor building) =>
        ChangeAsync(list => list.Where(b => b.Id != building.Id).Append(building));

    public Task RemoveAsync(string id) => ChangeAsync(list => list.Where(b => b.Id != id));

    private async Task ChangeAsync(Func<IEnumerable<BuildingAnchor>, IEnumerable<BuildingAnchor>> change)
    {
        await gate.WaitAsync();
        try
        {
            var updated = change(await ReadAsync()).ToList();
            var json = JsonSerializer.Serialize(updated, BuildingJsonContext.Default.ListBuildingAnchor);
            var temp = FilePath + ".tmp";
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<IReadOnlyList<BuildingAnchor>> ReadAsync()
    {
        if (!File.Exists(FilePath))
        {
            return Array.Empty<BuildingAnchor>();
        }

        try
        {
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(FilePath), BuildingJsonContext.Default.ListBuildingAnchor)
                ?? new List<BuildingAnchor>();
        }
        catch (JsonException)
        {
            return Array.Empty<BuildingAnchor>();
        }
    }
}
