using System.Text.Json.Serialization;

namespace Mostlylucid.LucidView.Markdown.Services;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ImageCacheEntry))]
internal partial class ImageCacheJsonContext : JsonSerializerContext
{
}
