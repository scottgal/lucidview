using Avalonia.Input;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The ten bare-letter gestures used to be Window.KeyBindings, which Avalonia
/// evaluates before the routed KeyDown and without any text-input guard, so
/// typing a word into the search box ran them. Nothing ever exercised them:
/// the UI harness's PressKey raises routed events, which KeyBindings never
/// see, and constructing a Window in an xunit test is not something this repo
/// does. Extracting the decision into ReaderShortcuts is what makes every one
/// of them checkable here, in both focus states.
/// </summary>
public class ReaderShortcutsTests
{
    private const KeyModifiers Command = KeyModifiers.Meta;

    private static ReaderShortcut Unfocused(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        ReaderShortcuts.Resolve(key, modifiers, focusIsTextEntry: false, Command);

    private static ReaderShortcut Typing(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        ReaderShortcuts.Resolve(key, modifiers, focusIsTextEntry: true, Command);

    public static TheoryData<Key, ReaderShortcut> BareGestures => new()
    {
        { Key.J, ReaderShortcut.NextItem },
        { Key.K, ReaderShortcut.PreviousItem },
        { Key.N, ReaderShortcut.NextUnread },
        { Key.P, ReaderShortcut.PreviousUnread },
        { Key.M, ReaderShortcut.ToggleRead },
        { Key.S, ReaderShortcut.ToggleStar },
        { Key.R, ReaderShortcut.RefreshCurrentFeed },
        { Key.O, ReaderShortcut.OpenOriginal },
        { Key.T, ReaderShortcut.EditTags },
        { Key.OemQuestion, ReaderShortcut.FocusSearch }
    };

    [Theory]
    [MemberData(nameof(BareGestures))]
    public void Bare_letter_gestures_run_their_action_when_focus_is_not_in_a_text_box(
        Key key, ReaderShortcut expected)
    {
        Assert.Equal(expected, Unfocused(key));
    }

    /// <summary>
    /// The regression this whole change exists for. Every one of these was a
    /// side effect of typing a word into the search box: o launched a browser,
    /// m and s wrote to the database, r started a network fetch, t opened a
    /// modal dialog mid-word.
    /// </summary>
    [Theory]
    [MemberData(nameof(BareGestures))]
    public void Bare_letter_gestures_are_inert_while_typing(Key key, ReaderShortcut _)
    {
        Assert.Equal(ReaderShortcut.None, Typing(key));
    }

    [Fact]
    public void Every_letter_of_compositor_is_inert_while_typing()
    {
        Key[] compositor =
        [
            Key.C, Key.O, Key.M, Key.P, Key.O, Key.S, Key.I, Key.T, Key.O, Key.R
        ];

        Assert.All(compositor, key => Assert.Equal(ReaderShortcut.None, Typing(key)));
    }

    [Fact]
    public void Shift_R_is_refresh_all_unfocused_and_inert_while_typing()
    {
        Assert.Equal(ReaderShortcut.RefreshAll, Unfocused(Key.R, KeyModifiers.Shift));
        Assert.Equal(ReaderShortcut.None, Typing(Key.R, KeyModifiers.Shift));
    }

    /// <summary>
    /// OemQuestion is the printable "/" character, so it needs the same guard
    /// as the letters even though it is not one.
    /// </summary>
    [Fact]
    public void Slash_focuses_search_only_when_not_already_typing()
    {
        Assert.Equal(ReaderShortcut.FocusSearch, Unfocused(Key.OemQuestion));
        Assert.Equal(ReaderShortcut.None, Typing(Key.OemQuestion));
    }

    [Theory]
    [InlineData(Key.F, ReaderShortcut.FindInArticle)]
    [InlineData(Key.N, ReaderShortcut.AddFeed)]
    [InlineData(Key.OemComma, ReaderShortcut.OpenSettings)]
    [InlineData(Key.S, ReaderShortcut.ExportArticle)]
    public void Command_modifier_gestures_work_in_both_focus_states(Key key, ReaderShortcut expected)
    {
        // They carry a modifier, so they cannot be produced by typing a word
        // and do not need the guard. Cmd+S while the cursor is in the search
        // box still means Export, the way it does in every other app.
        Assert.Equal(expected, Unfocused(key, Command));
        Assert.Equal(expected, Typing(key, Command));
    }

    /// <summary>
    /// Cmd+N and plain N are two different actions, and Cmd+S and plain S are
    /// two different actions. Neither pair may leak into the other.
    /// </summary>
    [Fact]
    public void The_command_modifier_selects_a_different_action_from_the_bare_letter()
    {
        Assert.Equal(ReaderShortcut.AddFeed, Unfocused(Key.N, Command));
        Assert.Equal(ReaderShortcut.NextUnread, Unfocused(Key.N));

        Assert.Equal(ReaderShortcut.ExportArticle, Unfocused(Key.S, Command));
        Assert.Equal(ReaderShortcut.ToggleStar, Unfocused(Key.S));
    }

    /// <summary>
    /// On Windows and Linux the platform modifier is Control, and the same
    /// four gestures have to resolve there without this function branching on
    /// OperatingSystem.
    /// </summary>
    [Fact]
    public void The_command_modifier_is_whatever_the_platform_says_it_is()
    {
        Assert.Equal(
            ReaderShortcut.OpenSettings,
            ReaderShortcuts.Resolve(Key.OemComma, KeyModifiers.Control, false, KeyModifiers.Control));

        // Meta on a machine whose command modifier is Control is not a
        // shortcut, it is just an unhandled keystroke.
        Assert.Equal(
            ReaderShortcut.None,
            ReaderShortcuts.Resolve(Key.OemComma, KeyModifiers.Meta, false, KeyModifiers.Control));
    }

    [Fact]
    public void Unrelated_modifiers_and_keys_resolve_to_nothing()
    {
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.J, KeyModifiers.Alt));
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.J, KeyModifiers.Control | KeyModifiers.Meta));
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.Q));
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.Escape));
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.Enter));
        Assert.Equal(ReaderShortcut.None, Unfocused(Key.Back));
    }

    /// <summary>
    /// Text editing keys must never be claimed by this function: the window
    /// marks anything it resolves as handled, so claiming Back or the arrows
    /// would break the search box outright.
    /// </summary>
    [Fact]
    public void Text_editing_keys_are_never_claimed_while_typing()
    {
        Key[] editing = [Key.Back, Key.Delete, Key.Left, Key.Right, Key.Home, Key.End, Key.Space];

        Assert.All(editing, key => Assert.Equal(ReaderShortcut.None, Typing(key)));
    }
}
