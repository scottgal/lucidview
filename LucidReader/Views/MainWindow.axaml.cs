using System.ComponentModel;
using Avalonia.Controls;

namespace LucidReader.Views;

/// <summary>
/// The window is self-bound (DataContext = this) rather than backed by a
/// separate view model, matching lucidVIEW's MainWindow. Avalonia's
/// AvaloniaObject already implements INotifyPropertyChanged, so a `new` event
/// here hides rather than overrides that base implementation. Verified
/// through the Mostlylucid.Avalonia.UITesting harness (--ux-repl), not a
/// unit test that constructs a Window: see task-1-report.md for the
/// transcript. If that verification ever regresses, the fix is not to fight
/// the base class further: move mutable state onto a plain non-Avalonia
/// DataContext object, or use Avalonia's own styled/direct properties
/// instead.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ReaderServices _services;
    private string _statusText = "Ready";

    public new event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(ReaderServices services)
    {
        _services = services;
        DataContext = this;
        InitializeComponent();
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    protected void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
