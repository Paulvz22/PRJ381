using System.Text.Json.Serialization;

namespace BCOpendayApp.Models;

/// <summary>A tour stop. X and Y are normalized IMAGE coordinates, not GPS values.</summary>
public sealed record Hotspot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Image { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Description { get; init; } = "";
    public string Highlight { get; init; } = "";
    public int SortOrder { get; init; }
    public string MapSection { get; init; } = "";
    public double? X { get; init; }
    public double? Y { get; init; }
    public string PositionNote { get; init; } = "";

    [JsonIgnore]
    public string Details => Description;

    [JsonIgnore]
    public bool HasMapPosition => MapSection is "main" or "north" or "west"
        && X is >= 0 and <= 1 && Y is >= 0 and <= 1;
}
