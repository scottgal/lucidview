using LucidReader.Core.Storage;
using Xunit;

namespace LucidReader.Core.Tests.Storage;

/// <summary>
/// The rename from lucidREADER to mylo moved the profile directory. These
/// cover the three states a machine can be in when the renamed build first
/// starts, and the one thing that must never happen: an existing directory
/// being removed or overwritten.
/// </summary>
public class LegacyProfileMoveTests
{
    [Fact]
    public void Neither_directory_exists_is_nothing_to_do()
    {
        Assert.Equal(
            LegacyProfileAction.None,
            LegacyProfileMove.Decide(legacyExists: false, currentExists: false));
    }

    [Fact]
    public void Only_the_old_directory_exists_so_it_is_moved()
    {
        Assert.Equal(
            LegacyProfileAction.Move,
            LegacyProfileMove.Decide(legacyExists: true, currentExists: false));
    }

    [Fact]
    public void Both_directories_exist_so_the_new_one_wins()
    {
        Assert.Equal(
            LegacyProfileAction.KeepBoth,
            LegacyProfileMove.Decide(legacyExists: true, currentExists: true));
    }

    [Fact]
    public void A_new_directory_on_its_own_is_left_alone()
    {
        Assert.Equal(
            LegacyProfileAction.None,
            LegacyProfileMove.Decide(legacyExists: false, currentExists: true));
    }

    [Fact]
    public void Nothing_is_created_when_there_is_no_old_directory()
    {
        using var root = new TempDirectory();
        var legacy = Path.Combine(root.Path, "lucidREADER");
        var current = Path.Combine(root.Path, "mylo");

        var result = LegacyProfileMove.Apply(legacy, current);

        Assert.Equal(LegacyProfileAction.None, result.Action);
        Assert.False(result.Moved);
        Assert.False(Directory.Exists(current));
    }

    [Fact]
    public void The_old_directory_and_its_contents_arrive_under_the_new_name()
    {
        using var root = new TempDirectory();
        var legacy = Path.Combine(root.Path, "lucidREADER");
        var current = Path.Combine(root.Path, "mylo");

        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "reader.db"), "database");
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        var result = LegacyProfileMove.Apply(legacy, current);

        Assert.Equal(LegacyProfileAction.Move, result.Action);
        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("database", File.ReadAllText(Path.Combine(current, "reader.db")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(current, "settings.json")));
    }

    [Fact]
    public void With_both_present_the_new_one_is_used_and_the_old_one_is_untouched()
    {
        using var root = new TempDirectory();
        var legacy = Path.Combine(root.Path, "lucidREADER");
        var current = Path.Combine(root.Path, "mylo");

        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "reader.db"), "old");
        File.WriteAllText(Path.Combine(current, "reader.db"), "new");

        var result = LegacyProfileMove.Apply(legacy, current);

        Assert.Equal(LegacyProfileAction.KeepBoth, result.Action);
        Assert.False(result.Moved);
        Assert.Equal("old", File.ReadAllText(Path.Combine(legacy, "reader.db")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(current, "reader.db")));
    }

    [Fact]
    public void Each_outcome_says_which_directory_is_in_use()
    {
        using var root = new TempDirectory();
        var legacy = Path.Combine(root.Path, "lucidREADER");
        var current = Path.Combine(root.Path, "mylo");

        Directory.CreateDirectory(legacy);

        var moved = LegacyProfileMove.Apply(legacy, current);
        var text = LegacyProfileMove.Describe(moved, legacy, current);

        Assert.Contains(legacy, text);
        Assert.Contains(current, text);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mylo-profile-move",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
