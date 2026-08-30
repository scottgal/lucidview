using Avalonia.Input;

namespace LucidReader.Services;

/// <summary>
/// Every keyboard action the reader window offers. Named rather than
/// expressed as a command reference so the decision below can be tested
/// without a Window and without any of the commands existing.
/// </summary>
public enum ReaderShortcut
{
    None,
    NextItem,
    PreviousItem,
    NextUnread,
    PreviousUnread,
    ToggleRead,
    ToggleStar,
    RefreshCurrentFeed,
    RefreshAll,
    OpenOriginal,
    FocusSearch,
    EditTags,
    FindInArticle,
    AddFeed,
    OpenSettings,
    ExportArticle
}

/// <summary>
/// The one place that decides which keystroke means which action.
///
/// This used to be ten <c>Window.KeyBindings</c> entries in MainWindow.axaml
/// with no modifier at all (J K N P M S R O T and OemQuestion). Avalonia's
/// KeyboardDevice evaluates KeyBindings by walking up from the focused
/// element BEFORE the routed KeyDown is raised, and KeyBinding.TryHandle has
/// no focus or text-input guard of its own. So typing the word "compositor"
/// into the search box ran OpenOriginal (which launches a browser), wrote
/// read state, wrote starred state, started a network fetch and opened the
/// modal tags dialog mid-word, and marked the keystrokes handled so the
/// characters did not reliably reach the box either.
///
/// A plain function taking the focus state as a parameter, rather than a
/// method on the window that reads FocusManager itself, is what makes all
/// twenty combinations (ten gestures, focused and unfocused) unit-testable:
/// constructing an Avalonia Window in a test is not something this repo does.
/// The window supplies the two things only it can know - whether keyboard
/// focus is in a text-entry control, and what this platform's command
/// modifier is (Meta on macOS, Control elsewhere).
/// </summary>
public static class ReaderShortcuts
{
    /// <summary>
    /// Resolves a keystroke to the action it should run, or
    /// <see cref="ReaderShortcut.None"/> when the window should leave it alone.
    /// </summary>
    /// <param name="focusIsTextEntry">
    /// True when keyboard focus is in a TextBox (or anything derived from
    /// one). Every bare-letter gesture is suppressed in that case, including
    /// Shift+R and the printable "/" of OemQuestion. The command-modifier
    /// gestures are not: Cmd+S while typing is still Export, the same way it
    /// is in every other app.
    /// </param>
    /// <param name="commandModifier">
    /// Avalonia's PlatformSettings.HotkeyConfiguration.CommandModifiers, so
    /// Cmd on macOS and Ctrl on Windows and Linux without this function
    /// branching on OperatingSystem.
    /// </param>
    public static ReaderShortcut Resolve(
        Key key,
        KeyModifiers modifiers,
        bool focusIsTextEntry,
        KeyModifiers commandModifier)
    {
        // Command-modifier gestures first, and deliberately outside the
        // text-entry guard: they carry a modifier, so they cannot be produced
        // by typing a word.
        if (commandModifier != KeyModifiers.None && modifiers == commandModifier)
        {
            return key switch
            {
                Key.F => ReaderShortcut.FindInArticle,
                Key.N => ReaderShortcut.AddFeed,
                Key.OemComma => ReaderShortcut.OpenSettings,
                Key.S => ReaderShortcut.ExportArticle,
                _ => ReaderShortcut.None
            };
        }

        // Everything below is a printable character. Typing one into a text
        // box means the character, never the action.
        if (focusIsTextEntry) return ReaderShortcut.None;

        if (modifiers == KeyModifiers.Shift)
            return key == Key.R ? ReaderShortcut.RefreshAll : ReaderShortcut.None;

        if (modifiers != KeyModifiers.None) return ReaderShortcut.None;

        return key switch
        {
            Key.J => ReaderShortcut.NextItem,
            Key.K => ReaderShortcut.PreviousItem,
            Key.N => ReaderShortcut.NextUnread,
            Key.P => ReaderShortcut.PreviousUnread,
            Key.M => ReaderShortcut.ToggleRead,
            Key.S => ReaderShortcut.ToggleStar,
            Key.R => ReaderShortcut.RefreshCurrentFeed,
            Key.O => ReaderShortcut.OpenOriginal,
            Key.T => ReaderShortcut.EditTags,
            Key.OemQuestion => ReaderShortcut.FocusSearch,
            _ => ReaderShortcut.None
        };
    }
}
