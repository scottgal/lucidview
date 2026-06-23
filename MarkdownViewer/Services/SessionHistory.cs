namespace MarkdownViewer.Services;

public sealed record NavigationEntry(string Url, string Title);

public sealed class SessionHistory
{
    private const int MaxEntries = 200;
    private readonly List<NavigationEntry> _stack = new();
    private int _currentIndex = -1;

    public bool CanGoBack => _currentIndex > 0;
    public bool CanGoForward => _currentIndex >= 0 && _currentIndex < _stack.Count - 1;
    public NavigationEntry? Current => _currentIndex >= 0 && _currentIndex < _stack.Count
        ? _stack[_currentIndex]
        : null;

    public void Push(NavigationEntry entry)
    {
        // Drop any forward stack — new navigation truncates redo history.
        if (_currentIndex < _stack.Count - 1)
            _stack.RemoveRange(_currentIndex + 1, _stack.Count - (_currentIndex + 1));

        // Collapse consecutive duplicates so reload-style navigations don't grow the stack.
        if (_stack.Count > 0 && string.Equals(_stack[^1].Url, entry.Url, StringComparison.Ordinal))
        {
            _stack[^1] = entry;
            _currentIndex = _stack.Count - 1;
            return;
        }

        _stack.Add(entry);
        if (_stack.Count > MaxEntries)
        {
            _stack.RemoveRange(0, _stack.Count - MaxEntries);
        }
        _currentIndex = _stack.Count - 1;
    }

    public NavigationEntry? Back()
    {
        if (!CanGoBack) return null;
        _currentIndex--;
        return _stack[_currentIndex];
    }

    public NavigationEntry? Forward()
    {
        if (!CanGoForward) return null;
        _currentIndex++;
        return _stack[_currentIndex];
    }

    public void Clear()
    {
        _stack.Clear();
        _currentIndex = -1;
    }
}
