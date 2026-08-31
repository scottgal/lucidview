namespace LucidReader.Core.Storage;

/// <summary>
/// Where mylo keeps its data. The database sits beside settings.json
/// so the two travel together when a user copies their profile.
/// </summary>
public static class ReaderPaths
{
    public const string AppFolderName = "mylo";

    /// <summary>
    /// The folder name used before the product was renamed to mylo. Kept only
    /// so <see cref="LegacyProfileMove"/> can find an existing profile and move
    /// it across; nothing writes here.
    /// </summary>
    public const string LegacyAppFolderName = "lucidREADER";

    public static string ApplicationDataRoot =>
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

    public static string AppDataDirectory =>
        Path.Combine(ApplicationDataRoot, AppFolderName);

    public static string LegacyAppDataDirectory =>
        Path.Combine(ApplicationDataRoot, LegacyAppFolderName);

    public static string DefaultDatabasePath =>
        Path.Combine(AppDataDirectory, "reader.db");

    public static string DefaultSettingsPath =>
        Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>
    /// The templates learned for scraped feeds. A separate file from reader.db
    /// because it holds nothing of the user's: it is a cache of selectors read
    /// off other people's markup, safe to delete at any point, and the only
    /// cost of losing it is that the next refresh of each scraped feed works
    /// out the page's structure again. See
    /// <see cref="Feeds.ScrapeTemplateStore"/>.
    /// </summary>
    public static string DefaultScrapeTemplatePath =>
        Path.Combine(AppDataDirectory, Feeds.ScrapeTemplateStore.FileName);
}
