using System.Text.Json.Serialization;
using BCOpendayApp.Models;

namespace BCOpendayApp.Services;

// Source-generated JSON avoids reflection-only serialization in trimmed mobile builds.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(List<Hotspot>))]
internal partial class LocationJsonContext : JsonSerializerContext { }
