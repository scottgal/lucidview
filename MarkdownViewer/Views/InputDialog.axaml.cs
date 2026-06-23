using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MarkdownViewer.Views;

public partial class InputDialog : Window
{
    public InputDialog()
    {
        InitializeComponent();
    }

    public InputDialog(string title, string prompt, string? watermark = null) : this()
    {
        Title = title;
        PromptText.Text = prompt;
        if (watermark is not null)
            InputBox.Watermark = watermark;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close(InputBox.Text);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}