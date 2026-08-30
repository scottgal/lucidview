using Avalonia.Input;
using LucidReader.Models;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The menu itself cannot be driven from the UI test harness. A macOS
/// NativeMenu is drawn by AppKit outside the window, and the harness can
/// neither locate nor click it; an in-window Menu opens its items into a
/// PopupRoot, which the harness cannot reach either, the same way it has
/// never been able to reach the sidebar's ContextMenu. So the structure, the
/// accelerators and the enablement rules are asserted here instead, which is
/// the whole reason the description lives in a plain class rather than in
/// MainWindow.
/// </summary>
public class ReaderMenuTests
{
    private static ReaderMenuSection Section(string header) =>
        Assert.Single(ReaderMenu.Build(), s => s.Header == header);

    private static ReaderMenuItem Item(string sectionHeader, string itemHeader) =>
        Assert.Single(Section(sectionHeader).Items, i => i.Header == itemHeader);

    [Fact]
    public void The_six_menus_are_present_and_in_order()
    {
        Assert.Equal(
            ["mylo", "File", "Edit", "View", "Feed", "Help"],
            ReaderMenu.Build().Select(s => s.Header));
    }

    [Fact]
    public void The_application_menu_is_named_after_the_product_not_the_toolkit()
    {
        Assert.Equal("mylo", ReaderMenu.AppMenuHeader);
        Assert.DoesNotContain(
            ReaderMenu.Build(),
            s => s.Header.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The point of the whole change: the two rare OPML operations left the
    /// toolbar and have exactly one home now.
    /// </summary>
    [Fact]
    public void Opml_import_and_export_live_under_File()
    {
        Assert.Equal(ReaderMenuAction.ImportOpml, Item("File", "Import OPML...").Action);
        Assert.Equal(ReaderMenuAction.ExportOpml, Item("File", "Export OPML...").Action);
    }

    /// <summary>
    /// The regression this project has already shipped once. Bare-letter
    /// KeyBindings fired while the user typed in the search box, so typing
    /// "compositor" opened a browser and wrote read and starred state. A
    /// macOS NativeMenuItem gesture is an AppKit key equivalent, matched
    /// before the focused field editor sees the key, so a bare-letter menu
    /// accelerator would be the same bug wearing a different hat. Every
    /// accelerator in the menu must therefore carry the command modifier,
    /// which this expresses by requiring that no gesture is a bare letter and
    /// that the letters ReaderShortcuts owns are never advertised at all
    /// except behind that modifier.
    /// </summary>
    [Fact]
    public void No_menu_item_advertises_a_bare_letter_accelerator()
    {
        var gestures = ReaderMenu.Build()
            .SelectMany(s => s.Items)
            .Where(i => i.HasGesture)
            .ToList();

        Assert.NotEmpty(gestures);

        // ExtraModifiers is what sits ON TOP of the command modifier, which
        // every accelerator carries by construction in MainWindow.Menu.cs.
        // Nothing may ask for a gesture with no modifier at all, and the
        // description has no way to express one: there is no "modifier-free"
        // flag. This asserts the shape stays that way.
        Assert.All(gestures, g => Assert.True(
            g.ExtraModifiers is KeyModifiers.None or KeyModifiers.Shift,
            $"{g.Header} asks for modifiers the command modifier does not cover"));
    }

    /// <summary>
    /// The four command-modifier gestures that already exist in
    /// ReaderShortcuts must be the same ones the menu draws, or the menu is
    /// telling the user something the keyboard does not do.
    /// </summary>
    [Theory]
    [InlineData("File", "Add Feed...", Key.N)]
    [InlineData("File", "Export Article...", Key.S)]
    [InlineData("Edit", "Find in Article", Key.F)]
    [InlineData("mylo", "Settings...", Key.OemComma)]
    public void The_menu_advertises_the_shortcuts_ReaderShortcuts_actually_resolves(
        string section, string item, Key key)
    {
        Assert.Equal(key, Item(section, item).GestureKey);
    }

    [Fact]
    public void The_feed_menu_gates_every_item_that_needs_a_feed()
    {
        Assert.Equal(ReaderMenuEnablement.RequiresFeed, Item("Feed", "Refresh Feed").Enablement);
        Assert.Equal(ReaderMenuEnablement.RequiresFeed, Item("Feed", "Mark All Read").Enablement);
        Assert.Equal(ReaderMenuEnablement.RequiresFeed, Item("Feed", "Feed Settings...").Enablement);
        Assert.Equal(ReaderMenuEnablement.RequiresFeed, Item("Feed", "Unsubscribe...").Enablement);

        // Resume is the exception, and deliberately so: on a healthy feed it
        // would mean nothing, which is why the toolbar hides rather than
        // merely disables its button.
        Assert.Equal(ReaderMenuEnablement.RequiresPausedFeed, Item("Feed", "Resume Feed").Enablement);

        // Refresh All needs nothing selected at all.
        Assert.Equal(ReaderMenuEnablement.Always, Item("Feed", "Refresh All").Enablement);
    }

    [Fact]
    public void Export_article_needs_an_article()
    {
        Assert.Equal(ReaderMenuEnablement.RequiresArticle, Item("File", "Export Article...").Enablement);
    }

    [Theory]
    [InlineData(ReaderMenuEnablement.Always, false, false, false, true)]
    [InlineData(ReaderMenuEnablement.RequiresFeed, false, false, false, false)]
    [InlineData(ReaderMenuEnablement.RequiresFeed, true, false, false, true)]
    [InlineData(ReaderMenuEnablement.RequiresPausedFeed, true, false, false, false)]
    [InlineData(ReaderMenuEnablement.RequiresPausedFeed, true, true, false, true)]
    [InlineData(ReaderMenuEnablement.RequiresArticle, true, true, false, false)]
    [InlineData(ReaderMenuEnablement.RequiresArticle, false, false, true, true)]
    public void Enablement_answers_only_the_question_it_was_asked(
        ReaderMenuEnablement rule, bool feed, bool paused, bool article, bool expected)
    {
        Assert.Equal(expected, ReaderMenu.IsEnabled(rule, feed, paused, article));
    }

    [Fact]
    public void Every_item_that_is_not_a_separator_carries_a_header_and_an_action()
    {
        foreach (var section in ReaderMenu.Build())
        foreach (var item in section.Items.Where(i => !i.IsSeparator))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Header));
            Assert.NotEqual(ReaderMenuAction.None, item.Action);
        }
    }

    [Fact]
    public void No_action_appears_in_two_places()
    {
        var actions = ReaderMenu.Build()
            .SelectMany(s => s.Items)
            .Where(i => !i.IsSeparator)
            .Select(i => i.Action)
            .ToList();

        Assert.Equal(actions.Count, actions.Distinct().Count());
    }

    [Fact]
    public void No_separator_opens_or_closes_a_menu()
    {
        foreach (var section in ReaderMenu.Build())
        {
            Assert.False(section.Items[0].IsSeparator, section.Header);
            Assert.False(section.Items[^1].IsSeparator, section.Header);
        }
    }

    /// <summary>
    /// The letters are not menu accelerators, so the summary in Help is the
    /// only place inside the app that names them.
    /// </summary>
    [Fact]
    public void The_help_summary_names_the_bare_letter_shortcuts()
    {
        var summary = ReaderMenu.KeyboardShortcutSummary;

        foreach (var fragment in new[] { "J ", "K ", "N ", "P ", "M ", "S ", "R ", "O ", "T ", "/" })
            Assert.Contains(fragment, summary);
    }
}
