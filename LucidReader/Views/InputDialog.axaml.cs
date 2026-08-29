using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LucidReader.Views;

public partial class InputDialog : Window
{
    public string? Result { get; private set; }

    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string initialValue) : this()
    {
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = InputBox.Text;
        Close(Result);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(null);
    }
}
