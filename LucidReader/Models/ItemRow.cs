using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public required FeedItem Item { get; init; }
    public required string FeedName { get; init; }

    public long Id => Item.Id;
    public string Title => string.IsNullOrWhiteSpace(Item.Title) ? "Untitled" : Item.Title!;

    public bool IsRead
    {
        get => _isRead;
        set { if (_isRead == value) return; _isRead = value; Raise(); Raise(nameof(TitleWeight)); }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set { if (_isStarred == value) return; _isStarred = value; Raise(); Raise(nameof(StarGlyph)); }
    }

    public string TitleWeight => _isRead ? "Normal" : "SemiBold";
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
