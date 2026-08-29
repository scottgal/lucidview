using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LucidReader.Views.Controls;

/// <summary>
/// The hover actions shown on an item row: mark read/unread, star/unstar,
/// open original. Raises events rather than calling ReaderServices directly
/// so MainWindow stays the only thing that touches the engine (see the
/// class-level remark on the mark-as-read dwell and SafeLinkOpener).
/// </summary>
public partial class RowActions : UserControl
{
    public RowActions()
    {
        InitializeComponent();
    }

    public event EventHandler? MarkRead;
    public event EventHandler? ToggleStar;
    public event EventHandler? OpenOriginal;

    private void OnMarkReadClick(object? sender, RoutedEventArgs e) => MarkRead?.Invoke(this, EventArgs.Empty);
    private void OnToggleStarClick(object? sender, RoutedEventArgs e) => ToggleStar?.Invoke(this, EventArgs.Empty);
    private void OnOpenOriginalClick(object? sender, RoutedEventArgs e) => OpenOriginal?.Invoke(this, EventArgs.Empty);
}
