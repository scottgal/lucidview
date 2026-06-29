// Stub for AppSettingsLab — used by the #if LAB first-run check.
// Real implementation wired in Task 4 of the lucidLAB plan.
using System.Text.Json;

namespace MarkdownViewer.Models;

public sealed class AppSettingsLab
{
    private static readonly JsonSerializerOptions WriteOptions =
        new JsonSerializerOptions { WriteIndented = true };

    public string LlmModelPath { get; set; } =
        "unsloth/Qwen3.5-4B-GGUF/Qwen3.5-4B-Q4_K_M.gguf";
    public bool LlmEnabled { get; set; } = true;
    public bool PlaywrightEnabled { get; set; } = true;
    public int LlmContextSize { get; set; } = 8192;
    public int LlmThreads { get; set; } = Environment.ProcessorCount;
    public int LlmGpuLayerCount { get; set; } = -1;
    public bool HasRunBefore { get; set; }

    private static string SettingsFilePath =>
        Path.Combine(MarkdownViewer.Lab.AppPaths.LocalState, "settings.json");

    public static AppSettingsLab Load()
    {
        if (!File.Exists(SettingsFilePath))
            return new AppSettingsLab();
        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettingsLab>(json) ?? new AppSettingsLab();
        }
        catch
        {
            return new AppSettingsLab();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, WriteOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
