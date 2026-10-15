using System.Text.Json.Serialization;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Data;

/// <summary>JSON shape of Resources/Raw/buildings.json, the on-phone cache and the scanner's export file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(List<BuildingAnchor>))]
public partial class BuildingJsonContext : JsonSerializerContext
{
}
