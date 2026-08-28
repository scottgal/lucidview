namespace LucidReader.Core.Storage;

/// <summary>
/// Where lucidREADER keeps its data. The database sits beside settings.json
/// so the two travel together when a user copies their profile.
/// </summary>
public static class ReaderPaths
{
    public const string AppFolderName = "lucidREADER";

    public static string AppDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create),
            AppFolderName);

    public static string DefaultDatabasePath =>
        Path.Combine(AppDataDirectory, "reader.db");

    public static string DefaultSettingsPath =>
        Path.Combine(AppDataDirectory, "settings.json");
}
