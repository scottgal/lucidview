using Avalonia.Input;
using LucidReader.Services;
using Xunit;

namespace LucidReader.Core.Tests.Ui;

/// <summary>
/// The View menu's three text-size items are the only menu actions whose
/// accelerators did not already exist in ReaderShortcuts. They are resolved
/// here as well as drawn in the menu because an in-window Avalonia MenuItem's
/// InputGesture is a label and nothing more: on Windows and Linux the routed
/// handler is the only thing that can make them work.
///
/// The other half of that bargain is that they must stay suppressed inside a
/// text box the same way every other printable gesture is - except that these
/// three carry the command modifier, which is exactly what makes them safe to
/// leave working while typing, the way Cmd+S already is.
/// </summary>
public class ReaderShortcutsMenuGesturesTests
{
    private const KeyModifiers Command = KeyModifiers.Meta;

    private static ReaderShortcut Resolve(Key key, bool textEntry) =>
        ReaderShortcuts.Resolve(key, Command, textEntry, Command);

    [Theory]
    [InlineData(Key.OemPlus, ReaderShortcut.IncreaseFontSize)]
    [InlineData(Key.OemMinus, ReaderShortcut.DecreaseFontSize)]
    [InlineData(Key.D0, ReaderShortcut.ResetFontSize)]
    public void The_text_size_gestures_resolve_with_the_command_modifier(Key key, ReaderShortcut expected)
    {
        Assert.Equal(expected, Resolve(key, textEntry: false));
        Assert.Equal(expected, Resolve(key, textEntry: true));
    }

    /// <summary>
    /// Without the modifier they are the characters "=", "-" and "0", and
    /// they must stay characters.
    /// </summary>
    [Theory]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.D0)]
    public void They_mean_nothing_without_the_command_modifier(Key key)
    {
        Assert.Equal(
            ReaderShortcut.None,
            ReaderShortcuts.Resolve(key, KeyModifiers.None, focusIsTextEntry: false, Command));
        Assert.Equal(
            ReaderShortcut.None,
            ReaderShortcuts.Resolve(key, KeyModifiers.None, focusIsTextEntry: true, Command));
    }

    /// <summary>
    /// The regression guard that must never be allowed to rot: the ten
    /// bare-letter gestures still work outside a text field and still do
    /// nothing inside one, after the menu work added three new entries to the
    /// command-modifier branch above them.
    /// </summary>
    [Theory]
    [InlineData(Key.J, ReaderShortcut.NextItem)]
    [InlineData(Key.K, ReaderShortcut.PreviousItem)]
    [InlineData(Key.N, ReaderShortcut.NextUnread)]
    [InlineData(Key.P, ReaderShortcut.PreviousUnread)]
    [InlineData(Key.M, ReaderShortcut.ToggleRead)]
    [InlineData(Key.S, ReaderShortcut.ToggleStar)]
    [InlineData(Key.R, ReaderShortcut.RefreshCurrentFeed)]
    [InlineData(Key.O, ReaderShortcut.OpenOriginal)]
    [InlineData(Key.T, ReaderShortcut.EditTags)]
    [InlineData(Key.OemQuestion, ReaderShortcut.FocusSearch)]
    public void The_bare_letters_still_work_outside_a_text_field_and_not_inside_one(
        Key key, ReaderShortcut expected)
    {
        Assert.Equal(
            expected,
            ReaderShortcuts.Resolve(key, KeyModifiers.None, focusIsTextEntry: false, Command));
        Assert.Equal(
            ReaderShortcut.None,
            ReaderShortcuts.Resolve(key, KeyModifiers.None, focusIsTextEntry: true, Command));
    }
}
