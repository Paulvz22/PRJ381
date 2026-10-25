using System.Text.Json.Serialization;

namespace BCOpendayApp.AR.Nav;

/// <summary>JSON shape of a nav graph ({'nodes':[...],'edges':[...]}).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(NavGraphDocument))]
public partial class NavGraphJsonContext : JsonSerializerContext
{
}
