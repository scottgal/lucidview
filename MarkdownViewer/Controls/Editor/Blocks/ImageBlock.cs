using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MarkdownViewer.Controls.Editor.Blocks;

public sealed class ImageBlock : UserControl, IEditorBlock
{
    private readonly TextBox _altEditor;
    private readonly TextBox _urlEditor;
    private readonly Avalonia.Controls.Image _imagePreview;
    private readonly TextBlock _altLabel;
    private readonly StackPanel _editorPanel;
    private readonly StackPanel _previewPanel;
    private bool _isEditing;
    private string _altText;
    private string _url;
    private readonly string _imageBasePath;

    public string BlockTypeLabel => "Image";
    public EditorBlockType BlockType => EditorBlockType.Image;
    public bool IsEditing => _isEditing;

    public event EventHandler? ContentChanged;
    public event EventHandler? DeleteRequested;
    public event EventHandler<BlockSplitEventArgs>? SplitRequested;
    public event EventHandler? FocusPreviousRequested;
    public event EventHandler? FocusNextRequested;

    Control IEditorBlock.View => this;

    public ImageBlock(MarkdownBlock descriptor, string imageBasePath)
    {
        _altText = descriptor.Content ?? "Image";
        _url = descriptor.Url ?? string.Empty;
        _imageBasePath = imageBasePath;

        _imagePreview = new Avalonia.Controls.Image
        {
            MaxWidth = 400,
            MaxHeight = 300,
            Stretch = Stretch.Uniform
        };

        _altLabel = new TextBlock
        {
            FontSize = 12,
            FontStyle = FontStyle.Italic,
            TextAlignment = TextAlignment.Center
        };

        TryLoadImage(_url);

        _previewPanel = new StackPanel { Spacing = 4 };
        _previewPanel.Children.Add(_imagePreview);
        _previewPanel.Children.Add(_altLabel);

        _altEditor = new TextBox { Watermark = "Alt text", FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
        _urlEditor = new TextBox
        {
            Watermark = "Image URL or path",
            FontSize = 13,
            FontFamily = new FontFamily("Cascadia Code, JetBrains Mono, Consolas, monospace")
        };

        var doneBtn = new Button
        {
            Content = "Done",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        doneBtn.Click += (_, _) => CommitEditing();

        _editorPanel = new StackPanel { Spacing = 2 };
        _editorPanel.Children.Add(new TextBlock { Text = "Edit Image", FontSize = 11 });
        _editorPanel.Children.Add(_altEditor);
        _editorPanel.Children.Add(_urlEditor);
        _editorPanel.Children.Add(doneBtn);

        ApplyTheme();
        Content = _previewPanel;

        _altEditor.TextChanging += (_, _) =>
        {
            _altText = _altEditor.Text ?? string.Empty;
            _altLabel.Text = _altText;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };
        _urlEditor.TextChanging += (_, _) =>
        {
            _url = _urlEditor.Text ?? string.Empty;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void RefreshTheme() => ApplyTheme();

    private void ApplyTheme()
    {
        _altLabel.Foreground = EditorTheme.TextSecondary;
        _altEditor.Foreground = EditorTheme.Text;
        _urlEditor.Foreground = EditorTheme.Text;
    }

    public void ActivateEditing()
    {
        _altEditor.Text = _altText;
        _urlEditor.Text = _url;
        Content = _editorPanel;
        _isEditing = true;
        _altEditor.Focus();
        _altEditor.CaretIndex = _altEditor.Text?.Length ?? 0;
    }

    public void CommitEditing()
    {
        _altText = _altEditor.Text ?? string.Empty;
        _url = _urlEditor.Text ?? string.Empty;
        TryLoadImage(_url);
        Content = _previewPanel;
        _isEditing = false;
    }

    public string ToMarkdown() => $"![{_altText}]({_url})";

    private void TryLoadImage(string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) { ShowPlaceholder(); return; }
            var path = url;
            if (!Path.IsPathRooted(path) && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(_imageBasePath, path);
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { ShowPlaceholder(); return; }
            if (File.Exists(path))
                _imagePreview.Source = new Avalonia.Media.Imaging.Bitmap(path);
            else
                ShowPlaceholder();
        }
        catch { ShowPlaceholder(); }
    }

    private void ShowPlaceholder()
    {
        _imagePreview.Source = null;
        _imagePreview.Width = 200;
        _imagePreview.Height = 120;
    }
}
