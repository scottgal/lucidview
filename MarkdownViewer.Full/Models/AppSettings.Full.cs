using System.Text.Json;

namespace MarkdownViewer.Models;

/// <summary>
/// FULL-only persistent settings (LLM + Playwright + first-run state).
/// Deliberately uses runtime <see cref="JsonSerializer"/> reflection instead of
/// lean's source-generated <c>AppSettingsContext</c> — FULL is permitted to be
/// non-AOT (it pulls Microsoft.Playwright and LLamaSharp which are themselves
/// reflection-heavy), so the AOT-safe serializer pattern is unnecessary
/// ceremony here. See lean's <c>AppSettings.cs</c> for the AOT pattern.
/// </summary>
public sealed class AppSettingsFull
{
    private static readonly JsonSerializerOptions _writeOptions =
        new JsonSerializerOptions { WriteIndented = true };

    public string LlmModelPath { get; set; } =
        "Qwen/Qwen2.5-0.5B-Instruct-GGUF/qwen2.5-0.5b-instruct-q4_k_m.gguf";
    public bool LlmEnabled { get; set; } = true;
    public bool PlaywrightEnabled { get; set; } = true;
    public int LlmContextSize { get; set; } = 512;
    public int LlmThreads { get; set; } = Environment.ProcessorCount;
    public int LlmGpuLayerCount { get; set; } = -1;
    public bool HasRunBefore { get; set; }

    public static AppSettingsFull Load()
    {
        if (!File.Exists(AppPaths.SettingsFilePath))
            return new AppSettingsFull();
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettingsFull>(json) ?? new AppSettingsFull();
        }
        catch
        {
            return new AppSettingsFull();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, _writeOptions);
        File.WriteAllText(AppPaths.SettingsFilePath, json);
    }
}
