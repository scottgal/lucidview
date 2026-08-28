using System.Text.Json;
using System.Text.Json.Serialization;
using LucidReader.Core.Model;

namespace LucidReader.Core.Storage;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Returns the defaults when the file is missing or unreadable. A corrupt
    /// settings file must not stop the app opening; the user's feeds and
    /// articles are in the database, and those are what matter.
    /// </summary>
    public static async Task<ReaderSettings> LoadAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path)) return ReaderSettings.Defaults;

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<ReaderSettings>(stream, Options, ct);
            return loaded ?? ReaderSettings.Defaults;
        }
        catch (JsonException)
        {
            return ReaderSettings.Defaults;
        }
        catch (IOException)
        {
            return ReaderSettings.Defaults;
        }
    }

    /// <summary>
    /// Writes to a temp file and moves it into place, so an interrupted save
    /// cannot leave a half-written settings file behind.
    /// </summary>
    public static async Task SaveAsync(
        string path,
        ReaderSettings settings,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
        }

        File.Move(temp, path, overwrite: true);
    }
}
