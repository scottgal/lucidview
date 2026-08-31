using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;
using LucidReader.Models;
using LucidReader.Services;

namespace LucidReader.Views;

/// <summary>
/// The application menu.
///
/// Two renderings of one description. macOS draws menus in the system menu
/// bar, outside the window, and the control that puts them there is
/// NativeMenu; Windows and Linux have no system menu bar, so there the same
/// description is built as an ordinary in-window Menu at the top of the
/// window. Exactly one of the two is ever created, so macOS does not end up
/// with the menus twice.
///
/// The shape itself is in LucidReader.Models.ReaderMenu, which knows nothing
/// about Avalonia and is unit-tested. This file is only the part that needs a
/// live window: turning items into controls, enabling and disabling them as
/// the selection changes, and running the action.
///
/// Nothing here duplicates a command body. Every action below either invokes
/// one of the RelayCommands from MainWindow.Actions.cs or calls the same
/// public method the toolbar and the sidebar context menu already call.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Every menu item that has an enablement rule, paired with the rule, so
    /// UpdateMenuEnablement has one list to walk rather than having to find
    /// the items again in two different menu trees. Both NativeMenuItem and
    /// MenuItem expose IsEnabled, but they share no base type that does, so
    /// the setter is captured as an Action here instead.
    /// </summary>
    private readonly List<(ReaderMenuEnablement Rule, Action<bool> SetEnabled)> _menuEnablement = [];

    /// <summary>
    /// Called once from the constructor, after ConfigurePlatformKeyBindings
    /// has resolved the command modifier the accelerators are built from.
    /// </summary>
    private void InstallMenus()
    {
        var sections = ReaderMenu.Build();

        if (UseNativeMenu)
            InstallNativeMenu(sections);
        else
            InstallWindowMenu(sections);

        UpdateMenuEnablement();
    }

    /// <summary>
    /// True on macOS, where menus belong in the system menu bar.
    ///
    /// In a Debug build only, MYLO_FORCE_WINDOW_MENU=1 forces the in-window
    /// Menu instead. That exists for one reason: the UI test harness cannot
    /// see a macOS NativeMenu at all. AppKit draws it outside the window, in
    /// a surface Avalonia does not own, so the harness can neither locate an
    /// item, click one, nor screenshot the result - and on a machine with the
    /// menu bar set to auto-hide, neither can a full-screen capture. The
    /// switch lets the same menu description be rendered as controls the
    /// harness can find, which is the only way to look at what the menus
    /// actually contain on this platform.
    ///
    /// Debug-only, like MYLO_DATA_DIR and the harness itself: the Release
    /// build always follows the platform.
    /// </summary>
    private static bool UseNativeMenu
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("MYLO_FORCE_WINDOW_MENU") == "1") return false;
#endif
            return OperatingSystem.IsMacOS();
        }
    }

    /// <summary>
    /// macOS. The menu is attached to the Application, not to the window, and
    /// that is not a stylistic choice: attached to a window, Avalonia adds
    /// these menus AFTER the standard application menu AppKit has already
    /// built, so the menu bar came out reading "mylo mylo File Edit View Feed
    /// Help" with the product's name in it twice. Attached to the
    /// Application, the first NativeMenuItem IS the application menu, which
    /// is where About, Settings and Quit belong.
    ///
    /// The first section's header is passed through for completeness but
    /// AppKit ignores it: the leftmost menu is always named after the
    /// application, which is why App.axaml sets Name to "mylo". Without that
    /// the menu bar reads "Avalonia Application".
    /// </summary>
    private void InstallNativeMenu(IReadOnlyList<ReaderMenuSection> sections)
    {
        // The application menu goes on the Application and the rest go on the
        // window, and the split is not a preference. Everything on the window
        // and the menu bar reads "mylo mylo File Edit ...": AppKit has already
        // built its own application menu from the app name by then, so the
        // first section arrives as a seventh menu beside it. Everything on the
        // Application and only the application menu appears at all: Avalonia
        // takes the first item as the app menu and the others are never drawn.
        // Both were tried; this is what leaves one "mylo" menu on the left
        // with About, Settings and Quit inside it, and the other five beside
        // it.
        if (Application.Current is { } app)
            NativeMenu.SetMenu(app, BuildNativeMenu([sections[0]]));

        NativeMenu.SetMenu(this, BuildNativeMenu(sections.Skip(1).ToList()));
    }

    private NativeMenu BuildNativeMenu(IReadOnlyList<ReaderMenuSection> sections)
    {
        var root = new NativeMenu();

        foreach (var section in sections)
        {
            var submenu = new NativeMenu();

            foreach (var item in section.Items)
            {
                if (item.IsSeparator)
                {
                    submenu.Add(new NativeMenuItemSeparator());
                    continue;
                }

                var native = new NativeMenuItem(item.Header);

                if (item.GestureKey is { } key)
                    native.Gesture = new KeyGesture(key, _commandModifier | item.ExtraModifiers);

                var action = item.Action;
                native.Click += (_, _) => RunMenuAction(action);

                if (item.Enablement != ReaderMenuEnablement.Always)
                    _menuEnablement.Add((item.Enablement, enabled => native.IsEnabled = enabled));

                submenu.Add(native);
            }

            root.Add(new NativeMenuItem(section.Header) { Menu = submenu });
        }

        return root;
    }

    /// <summary>
    /// Windows and Linux. WindowMenu is a Menu declared in MainWindow.axaml
    /// with IsVisible false, in a zero-height Auto row above the toolbar; it
    /// is only ever made visible here, so on macOS that row costs nothing and
    /// the toolbar keeps the title-bar band the traffic lights are drawn in.
    ///
    /// The gestures are drawn as labels only. An Avalonia MenuItem's
    /// InputGesture does not bind the key; the keystrokes themselves are
    /// resolved by ReaderShortcuts from the window's KeyDown handler, which
    /// is the one place in this app that decides what a key means.
    /// </summary>
    private void InstallWindowMenu(IReadOnlyList<ReaderMenuSection> sections)
    {
        if (WindowMenu is null) return;

        var top = new List<MenuItem>();

        foreach (var section in sections)
        {
            var header = new MenuItem { Header = section.Header };
            var children = new List<Control>();

            foreach (var item in section.Items)
            {
                if (item.IsSeparator)
                {
                    children.Add(new Separator());
                    continue;
                }

                var menuItem = new MenuItem { Header = item.Header };

                if (item.GestureKey is { } key)
                    menuItem.InputGesture = new KeyGesture(key, _commandModifier | item.ExtraModifiers);

                var action = item.Action;
                menuItem.Click += (_, _) => RunMenuAction(action);

                if (item.Enablement != ReaderMenuEnablement.Always)
                    _menuEnablement.Add((item.Enablement, enabled => menuItem.IsEnabled = enabled));

                children.Add(menuItem);
            }

            header.ItemsSource = children;
            top.Add(header);
        }

        WindowMenu.ItemsSource = top;
        WindowMenu.IsVisible = true;
    }

    /// <summary>
    /// Re-evaluates every gated item. Called from the constructor and from the
    /// two selection properties, because those are the only three things the
    /// rules depend on: whether a feed is selected, whether that feed is
    /// auto-paused, and whether an article is selected.
    /// </summary>
    internal void UpdateMenuEnablement()
    {
        var feed = IsFeedSelected;
        var paused = IsPausedFeedSelected;
        var article = SelectedItemRow is not null;

        foreach (var (rule, setEnabled) in _menuEnablement)
            setEnabled(ReaderMenu.IsEnabled(rule, feed, paused, article));
    }

    /// <summary>
    /// Runs a menu action. Every branch either executes an existing
    /// RelayCommand, whose Execute is the one async void in this app that
    /// already catches everything, or calls RunGuardedAsync, which reports to
    /// the status bar. Nothing here is an unguarded async void.
    /// </summary>
    private void RunMenuAction(ReaderMenuAction action)
    {
        switch (action)
        {
            case ReaderMenuAction.About:
                _ = RunGuardedAsync(ShowAboutAsync, "show the about box");
                break;
            case ReaderMenuAction.OpenSettings:
                OpenSettingsCommand.Execute(null);
                break;
            case ReaderMenuAction.Quit:
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                break;

            case ReaderMenuAction.AddFeed:
                AddFeedCommand.Execute(null);
                break;
            case ReaderMenuAction.ImportOpml:
                _ = RunGuardedAsync(ImportOpmlAsync, "import subscriptions");
                break;
            case ReaderMenuAction.ExportOpml:
                _ = RunGuardedAsync(ExportOpmlAsync, "export subscriptions");
                break;
            case ReaderMenuAction.ExportArticle:
                ExportArticleCommand.Execute(null);
                break;

            case ReaderMenuAction.Undo:
                FocusedTextBox()?.Undo();
                break;
            case ReaderMenuAction.Cut:
                FocusedTextBox()?.Cut();
                break;
            case ReaderMenuAction.Copy:
                FocusedTextBox()?.Copy();
                break;
            case ReaderMenuAction.Paste:
                FocusedTextBox()?.Paste();
                break;
            case ReaderMenuAction.SelectAll:
                FocusedTextBox()?.SelectAll();
                break;
            case ReaderMenuAction.EditTags:
                EditTagsCommand.Execute(null);
                break;
            case ReaderMenuAction.FindInArticle:
                FindInArticleCommand.Execute(null);
                break;

            case ReaderMenuAction.LayoutThreePane:
                SetLayoutMode(ReaderLayoutMode.ThreePane);
                break;
            case ReaderMenuAction.LayoutListAndReading:
                SetLayoutMode(ReaderLayoutMode.ListAndReading);
                break;
            case ReaderMenuAction.LayoutReadingOnly:
                SetLayoutMode(ReaderLayoutMode.ReadingOnly);
                break;
            case ReaderMenuAction.CycleLayout:
                SetLayoutMode(ReaderLayout.Next(LayoutMode));
                break;
            case ReaderMenuAction.FilterAll:
                IsFilterAll = true;
                break;
            case ReaderMenuAction.FilterUnread:
                IsFilterUnread = true;
                break;
            case ReaderMenuAction.FilterStarred:
                IsFilterStarred = true;
                break;
            case ReaderMenuAction.IncreaseFontSize:
                StepReadingFontSize(ReadingFontScale.Increase(_services.Settings.FontSize));
                break;
            case ReaderMenuAction.DecreaseFontSize:
                StepReadingFontSize(ReadingFontScale.Decrease(_services.Settings.FontSize));
                break;
            case ReaderMenuAction.ResetFontSize:
                StepReadingFontSize(ReadingFontScale.Reset());
                break;

            case ReaderMenuAction.RefreshAll:
                RefreshAllCommand.Execute(null);
                break;
            case ReaderMenuAction.RefreshFeed:
                RefreshCurrentFeedCommand.Execute(null);
                break;
            case ReaderMenuAction.MarkAllRead:
                if (SelectedFeedNode?.FeedId is { } markId)
                    _ = RunGuardedAsync(() => MarkFeedReadAsync(markId), "mark this feed read");
                break;
            case ReaderMenuAction.FeedSettings:
                if (SelectedFeedNode?.FeedId is { } settingsId)
                    _ = RunGuardedAsync(() => ShowFeedSettingsAsync(settingsId), "open feed settings");
                break;
            case ReaderMenuAction.ResumeFeed:
                if (SelectedFeedNode?.FeedId is { } resumeId)
                    _ = RunGuardedAsync(() => ResumeFeedAsync(resumeId), "resume this feed");
                break;
            case ReaderMenuAction.Unsubscribe:
                if (SelectedFeedNode?.FeedId is { } unsubscribeId)
                    _ = RunGuardedAsync(() => UnsubscribeAsync(unsubscribeId), "unsubscribe from this feed");
                break;

            case ReaderMenuAction.OpenHelpSite:
                if (!SafeLinkOpener.TryOpen(HelpSiteUrl, out var reason))
                    StatusMessage = reason ?? "Could not open the help page.";
                break;
            case ReaderMenuAction.ShowKeyboardShortcuts:
                StatusMessage = ReaderMenu.KeyboardShortcutSummary;
                break;
        }
    }

    private const string HelpSiteUrl = "https://www.mostlylucid.net";

    /// <summary>
    /// Writes a new reading font size, and only when it actually changed:
    /// ReadingFontScale clamps, so at either end of the range every further
    /// press returns the same number and saving it would rewrite settings.json
    /// on each keystroke for no effect.
    /// </summary>
    private void StepReadingFontSize(double size)
    {
        if (Math.Abs(size - _services.Settings.FontSize) < 0.001) return;

        _ = RunGuardedAsync(
            () => _services.UpdateSettingsAsync(_services.Settings with { FontSize = size }),
            "change the text size");
    }

    /// <summary>
    /// The text box the Edit menu acts on.
    ///
    /// The active window, not this one. A macOS native menu stays on the menu
    /// bar while a modal dialog is up, so Copy pressed in the Add Feed
    /// dialog's address box must find that box, not whatever this window last
    /// had focused. Each dialog is its own Window with its own FocusManager,
    /// so the active window has to be found first.
    /// </summary>
    private static TextBox? FocusedTextBox()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var window = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        var focused = window?.FocusManager?.GetFocusedElement();

        return focused as TextBox
               ?? (focused as Visual)?.FindAncestorOfType<TextBox>();
    }

    /// <summary>
    /// A dialog rather than a status line, because a status line is gone the
    /// moment anything else happens and About is the one place the version
    /// number can be read.
    /// </summary>
    private async Task ShowAboutAsync()
    {
        var version = Assembly.GetExecutingAssembly()
                          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "unknown";

        // The build metadata suffix a source-linked build appends is noise here.
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        var dialog = new ConfirmDialog(
            "About mylo",
            $"mylo {version}\n\nA native RSS and Atom reader built on the lucidVIEW rendering stack.",
            "OK");

        await dialog.ShowDialog(this);
    }
}
