using LucidReader.Core.Model;
using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lucidreader-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_missing_file_yields_the_defaults()
    {
        var settings = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(ReaderSettings.Defaults, settings);
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        var original = ReaderSettings.Defaults with
        {
            DefaultRefreshIntervalMinutes = 90,
            AutoDownloadArticles = false,
            Theme = "Dark"
        };

        await SettingsStore.SaveAsync(Path_, original);
        var loaded = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(original, loaded);
    }

    [Fact]
    public async Task A_corrupt_file_falls_back_to_the_defaults_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path_, "{ this is not json");

        var settings = await SettingsStore.LoadAsync(Path_);

        Assert.Equal(ReaderSettings.Defaults, settings);
    }

    [Fact]
    public async Task Saving_leaves_no_temp_file_behind()
    {
        await SettingsStore.SaveAsync(Path_, ReaderSettings.Defaults);

        Assert.False(File.Exists(Path_ + ".tmp"));
    }
}
