using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkdownViewer.Models;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MarkdownViewer",
        "settings.json"
    );

    // Theme
    public AppTheme Theme { get; set; } = AppTheme.Auto;
    public CodeTheme CodeTheme { get; set; } = CodeTheme.Auto;

    // Typography
    // Bundled Raleway font with system fallbacks
    public string FontFamily { get; set; } = "avares://lucidVIEW/Assets/Raleway-Regular.ttf#Raleway, Segoe UI, Inter, sans-serif";
    public double FontSize { get; set; } = 15;
    public double LineHeight { get; set; } = 1.6;
    public string CodeFontFamily { get; set; } = "Consolas, Cascadia Code, JetBrains Mono, Menlo, Monaco, monospace";
    public double CodeFontSize { get; set; } = 13;

    // Layout
    public int WindowWidth { get; set; } = 1100;
    public int WindowHeight { get; set; } = 750;
    public bool ShowNavigationPanel { get; set; } = false;
    public double NavigationPanelWidth { get; set; } = 260;
    public bool WordWrap { get; set; } = true;
    public double ContentMaxWidth { get; set; } = 900;

    // Recent files
    public List<RecentFile> RecentFiles { get; set; } = [];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, AppSettingsContext.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }

    public void AddRecentFile(string path, string? displayName = null)
    {
        RecentFiles.RemoveAll(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, new RecentFile
        {
            Path = path,
            DisplayName = displayName ?? Path.GetFileName(path),
            LastOpened = DateTime.UtcNow
        });

        if (RecentFiles.Count > 10) RecentFiles = RecentFiles.Take(10).ToList();

        Save();
    }
}

public class RecentFile
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext
{
}
