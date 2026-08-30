using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using LucidReader.Core.Model;

namespace LucidReader.Models;

/// <summary>
/// One row in the item list. Wraps a FeedItem so read and starred state can
/// change in place without requerying, which matters because the list is
/// virtualised and requerying on every keystroke of J would be visible.
/// </summary>
public sealed class ItemRow : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isStarred;
    private string? _thumbnailPath;

    public required FeedItem Item { get; init; }
    public required string FeedName { get; init; }

    public long Id => Item.Id;
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "Untitled" : Item.Title!;

    /// <summary>
    /// Plain-text preview shown under the title, the way Mail previews a
    /// message body. Computed once when the row is built rather than bound
    /// through a converter, since the source markdown/summary never changes
    /// after the row exists.
    /// </summary>
    public string Snippet { get; init; } = string.Empty;

    /// <summary>
    /// The passage FTS5 says matched, delimited with SearchHit.MatchStart and
    /// MatchEnd, set only on rows built from a search result. Empty on an
    /// ordinary feed-list row.
    /// </summary>
    public string MatchedSnippet { get; init; } = string.Empty;

    /// <summary>
    /// True on a row that came from a search. Decides which of the two
    /// preview lines the row shows: the stored summary preview, or the passage
    /// that matched. The two are different things and a search result showing
    /// the first cannot say why it is in the list.
    /// </summary>
    public bool IsSearchResult { get; init; }

    /// <summary>
    /// The matched passage split into words, each flagged as matching or not,
    /// which is how the row highlights the query terms. Computed once here
    /// rather than in a converter, same as <see cref="Snippet"/>.
    ///
    /// Two explicit visibility properties rather than a "!IsSearchResult"
    /// binding, for the reason given on <see cref="IsUnread"/>: this project
    /// runs with reflection bindings and a named property cannot be
    /// misread.
    /// </summary>
    public IReadOnlyList<SnippetWord> SnippetWords =>
        _snippetWords ??= SearchSnippet.ToWords(MatchedSnippet);

    private IReadOnlyList<SnippetWord>? _snippetWords;

    public bool IsSearchSnippetVisible => IsSearchResult && MatchedSnippet.Length > 0;
    public bool IsPlainSnippetVisible => !IsSearchSnippetVisible;

    public bool IsRead
    {
        get => _isRead;
        set { if (_isRead == value) return; _isRead = value; Raise(); Raise(nameof(TitleWeight)); Raise(nameof(IsUnread)); }
    }

    /// <summary>
    /// The unread-dot gutter binds to this rather than a binding-syntax
    /// negation (<c>!IsRead</c>), since this project runs with reflection
    /// bindings (AvaloniaUseCompiledBindingsByDefault is false) and an
    /// explicit property is unambiguous either way.
    /// </summary>
    public bool IsUnread => !_isRead;

    public bool IsStarred
    {
        get => _isStarred;
        set { if (_isStarred == value) return; _isStarred = value; Raise(); Raise(nameof(StarGlyph)); Raise(nameof(IsNotStarred)); }
    }

    /// <summary>
    /// Same binding-negation avoidance as <see cref="IsUnread"/>, used by
    /// RowActions to swap between a hollow and filled star glyph depending
    /// on this row's current state.
    /// </summary>
    public bool IsNotStarred => !_isStarred;

    /// <summary>
    /// Local cached path for the list row's thumbnail, resolved from
    /// <c>Item.ImageUrl</c> (Task 8b's OpenGraph image). Starts null - the
    /// row renders immediately with text only, occupying the full width -
    /// and is assigned later, on the UI thread, by MainWindow's background
    /// resolution pass (Task 8c). Must raise change notification: the row
    /// is already on screen and possibly scrolled into view when this is set.
    /// </summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set { if (_thumbnailPath == value) return; _thumbnailPath = value; Raise(); Raise(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailPath);

    /// <summary>
    /// A real FontWeight, not the string "Normal"/"SemiBold" this used to be.
    /// The binding target (MainWindow.axaml, TextBlock.FontWeight) is typed
    /// FontWeight, and a string reached it only through Avalonia's implicit
    /// enum coercion. That is the same class of silent conversion that
    /// already broke an Indent binding on this branch, and with
    /// AvaloniaUseCompiledBindingsByDefault false there is no compile-time
    /// check to catch the next one. Typing the property removes the coercion.
    /// </summary>
    public FontWeight TitleWeight => _isRead ? FontWeight.Normal : FontWeight.SemiBold;
    public string StarGlyph => _isStarred ? "★" : "☆";

    /// <summary>
    /// Relative age, computed against a clock the caller supplies so tests are
    /// not at the mercy of the wall clock.
    /// </summary>
    public string RelativeDate { get; init; } = string.Empty;

    public static string FormatRelative(DateTimeOffset when, DateTimeOffset nowUtc)
    {
        var span = nowUtc - when;
        if (span < TimeSpan.Zero) return "just now";
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h";
        if (span < TimeSpan.FromDays(7)) return $"{(int)span.TotalDays}d";
        return when.ToLocalTime().ToString("d MMM yyyy");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
