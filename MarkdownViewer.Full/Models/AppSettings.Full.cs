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

    // Production default per stylobot-extract's bench:
    // tests/StyloExtract.Llm.Benchmark/README.md ranks qwen3.5:4b as best
    // F1 (0.805) for template induction. unsloth's GGUF reupload is the
    // canonical HuggingFace home; Q4_K_M is ~2.5 GB.
    public string LlmModelPath { get; set; } =
        "unsloth/Qwen3.5-4B-GGUF/Qwen3.5-4B-Q4_K_M.gguf";
    public bool LlmEnabled { get; set; } = true;
    public bool PlaywrightEnabled { get; set; } = true;
    // 8192 matches the LlamaSharp README example in stylobot-extract.
    // Template induction needs the bigger window — 512 was the old stylobot
    // bot-detection prompt budget and silently truncates serious prompts.
    public int LlmContextSize { get; set; } = 8192;
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
