using LucidReader.Core.Model;
using Xunit;

namespace LucidReader.Core.Tests.Model;

/// <summary>
/// The tag-name rules, at their boundaries. Every one of these is a decision
/// somebody could reasonably have made differently, so each is pinned here
/// rather than left to whatever the implementation happens to do.
/// </summary>
public class TagNameTests
{
    [Theory]
    [InlineData("dotnet", "dotnet")]
    [InlineData("  dotnet  ", "dotnet")]
    [InlineData("dot net", "dot net")]
    [InlineData("dot   net", "dot net")]
    [InlineData("\tdot\t\tnet\n", "dot net")]
    [InlineData("DotNet", "DotNet")]
    [InlineData("c#", "c#")]
    [InlineData("read/later", "read/later")]
    [InlineData("kaffee-pause", "kaffee-pause")]
    [InlineData("日本語", "日本語")]
    public void Accepted_names_are_trimmed_and_have_their_internal_whitespace_collapsed(
        string raw, string expected)
    {
        Assert.True(TagName.TryNormalise(raw, out var name, out var error));
        Assert.Equal(expected, name);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void A_blank_name_is_refused_without_an_error_to_show(string? raw)
    {
        Assert.False(TagName.TryNormalise(raw, out var name, out var error));
        Assert.Equal(string.Empty, name);
        Assert.Null(error);
    }

    [Fact]
    public void A_comma_is_refused_because_it_is_the_list_separator()
    {
        Assert.False(TagName.TryNormalise("read,later", out _, out var error));
        Assert.Equal("A tag name cannot contain a comma.", error);
    }

    [Fact]
    public void A_control_character_is_refused_because_it_is_invisible()
    {
        Assert.False(TagName.TryNormalise("read\u0007later", out _, out var error));
        Assert.Equal("A tag name cannot contain control characters.", error);
    }

    [Fact]
    public void A_name_of_exactly_the_maximum_length_is_accepted()
    {
        var name = new string('a', TagName.MaxLength);

        Assert.True(TagName.TryNormalise(name, out var normalised, out _));
        Assert.Equal(name, normalised);
    }

    [Fact]
    public void A_name_one_character_past_the_maximum_is_refused_rather_than_truncated()
    {
        var tooLong = new string('a', TagName.MaxLength + 1);

        Assert.False(TagName.TryNormalise(tooLong, out var normalised, out var error));
        Assert.Equal(string.Empty, normalised);
        Assert.Equal($"A tag name can be at most {TagName.MaxLength} characters.", error);
    }

    /// <summary>
    /// The length is measured AFTER normalisation, so padding does not count
    /// against the limit: a name that fits once trimmed is a name that fits.
    /// </summary>
    [Fact]
    public void Length_is_measured_after_trimming()
    {
        var padded = "   " + new string('a', TagName.MaxLength) + "   ";

        Assert.True(TagName.TryNormalise(padded, out var name, out _));
        Assert.Equal(TagName.MaxLength, name.Length);
    }

    [Fact]
    public void Normalise_throws_with_the_rule_that_refused_the_name()
    {
        var thrown = Assert.Throws<ArgumentException>(() => TagName.Normalise("a,b"));
        Assert.Contains("comma", thrown.Message);
    }

    [Theory]
    [InlineData("dotnet", "DotNet", true)]
    [InlineData("DOTNET", "dotnet", true)]
    [InlineData("dot net", "DOT NET", true)]
    [InlineData("dotnet", "dot net", false)]
    [InlineData("dotnet", "dotnets", false)]
    public void Identity_ignores_ascii_case(string a, string b, bool same) =>
        Assert.Equal(same, TagName.AreSame(a, b));

    /// <summary>
    /// The deliberate limit of the case rule. SQLite's NOCASE collation folds
    /// ASCII only, and every tag lookup in TagRepository runs through it, so
    /// folding more than that here would have the app show one tag while the
    /// database holds two. Pinned so a later "improvement" to a Unicode-aware
    /// comparison has to argue with a test rather than slip through.
    /// </summary>
    [Theory]
    [InlineData("café", "CAFÉ")]
    [InlineData("straße", "STRASSE")]
    public void Identity_does_not_fold_beyond_ascii_because_sqlite_does_not(string a, string b) =>
        Assert.False(TagName.AreSame(a, b));

    [Fact]
    public void A_list_is_split_normalised_and_kept_in_the_order_it_was_typed()
    {
        var parsed = TagName.ParseList(" avalonia ,  dot   net,c# ");

        Assert.Equal(["avalonia", "dot net", "c#"], parsed.Names);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void A_duplicate_in_a_list_collapses_onto_the_first_spelling_and_is_not_an_error()
    {
        var parsed = TagName.ParseList("DotNet, dotnet, DOTNET");

        Assert.Equal(["DotNet"], parsed.Names);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void Empty_entries_in_a_list_are_dropped_silently()
    {
        var parsed = TagName.ParseList("avalonia,,  , dotnet,");

        Assert.Equal(["avalonia", "dotnet"], parsed.Names);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void A_refused_entry_is_reported_while_the_rest_of_the_list_still_parses()
    {
        var parsed = TagName.ParseList("avalonia, " + new string('x', TagName.MaxLength + 1));

        Assert.Equal(["avalonia"], parsed.Names);
        Assert.Single(parsed.Errors);
        Assert.Contains("at most", parsed.Errors[0]);
    }

    [Fact]
    public void The_same_reason_is_reported_once_however_many_entries_hit_it()
    {
        var tooLong = new string('x', TagName.MaxLength + 1);
        var parsed = TagName.ParseList($"{tooLong}, {tooLong}y");

        Assert.Empty(parsed.Names);
        Assert.Single(parsed.Errors);
    }

    [Fact]
    public void An_entirely_blank_list_yields_nothing_and_says_nothing()
    {
        var parsed = TagName.ParseList("  , ,  ");

        Assert.Empty(parsed.Names);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void The_comparer_deduplicates_the_way_AreSame_compares()
    {
        var set = new HashSet<string>(TagName.Comparer) { "DotNet" };

        Assert.False(set.Add("dotnet"));
        Assert.True(set.Add("dot net"));
    }
}
