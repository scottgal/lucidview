using System.Windows.Input;

namespace MarkdownViewer.Views;

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public RelayCommand(Func<Task> executeAsync)
    {
        _executeAsync = executeAsync;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            if (_executeAsync != null)
                await _executeAsync();
            else
                _execute?.Invoke();
        }
        catch (Exception ex)
        {
            // Avoids async-void exceptions crashing the process via the
            // SynchronizationContext when a command body throws.
            Console.Error.WriteLine($"[RelayCommand] {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;

    public RelayCommand(Action<T?> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute((T?)parameter);
}
