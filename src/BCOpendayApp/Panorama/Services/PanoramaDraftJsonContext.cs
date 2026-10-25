using System.Text.Json.Serialization;
using BCOpendayApp.Panorama.Models;

namespace BCOpendayApp.Panorama.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PanoramaDraft>))]
internal sealed partial class PanoramaDraftJsonContext : JsonSerializerContext
{
}
