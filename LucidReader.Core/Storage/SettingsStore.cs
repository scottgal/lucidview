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
    ///
    /// The app then opens with defaults in memory, and the very next SaveAsync
    /// call would otherwise overwrite the original file with those defaults,
    /// permanently losing whatever the user had configured. To prevent that,
    /// when the existing file cannot be parsed or read, it is copied aside to
    /// "&lt;path&gt;.corrupt" (overwriting any previous backup) before defaults
    /// are returned, so the user's real values remain recoverable on disk.
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
            // File exists and is readable but its content is bad. Preserving
            // it is expected to succeed; if it doesn't, that's a second,
            // separate problem, but it still must not crash startup.
            TryPreserveCorruptFile(path);
            return ReaderSettings.Defaults;
        }
        catch (IOException)
        {
            // The file may be locked or transiently unreadable rather than
            // actually bad. Attempt to preserve it for the same reason as
            // above, but never let a failed backup attempt (of a file we
            // could not even read) overwrite a previously-good backup with
            // nothing, and never let it throw.
            TryPreserveCorruptFile(path);
            return ReaderSettings.Defaults;
        }
    }

    /// <summary>
    /// Best-effort copy of the unreadable/corrupt settings file to a backup
    /// path so it is not lost when the caller subsequently saves defaults
    /// over the original. Never throws; a failure here must not prevent the
    /// app from starting with defaults.
    /// </summary>
    private static void TryPreserveCorruptFile(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Could not read the source to back it up. Leave any existing
            // backup untouched rather than clobbering it with a failed copy.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning as above; a permissions failure here is not
            // fatal to startup.
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
