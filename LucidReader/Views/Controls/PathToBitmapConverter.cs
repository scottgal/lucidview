using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace LucidReader.Views.Controls;

/// <summary>
/// Binds a local file path string (FeedTreeNode.IconPath, ItemRow.ThumbnailPath,
/// MainWindow.HeroImagePath - all Task 8c) to an Image control's Source, which
/// is typed IImage, not string. Avalonia has no built-in string-to-Bitmap
/// conversion, so this is the one converter all three image surfaces share.
///
/// Deliberately fails soft: a missing or unreadable file (deleted from the
/// cache after the row already resolved, corrupt download, race with an
/// eviction) yields null - the Image renders nothing - rather than a binding
/// exception that would take the row down with it.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
