using Avalonia.Controls;
using Avalonia.Interactivity;
using MarkdownViewer.Models;

namespace MarkdownViewer.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = new AppSettings();
    }

    public SettingsDialog(AppSettings settings) : this()
    {
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Theme
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Light => 0,
            AppTheme.Dark => 1,
            AppTheme.VSCode => 2,
            AppTheme.GitHub => 3,
            _ => 1
        };

        // Typography
        FontFamilyBox.Text = _settings.FontFamily;
        FontSizeBox.Value = (decimal)_settings.FontSize;
        CodeFontBox.Text = _settings.CodeFontFamily;

        // Layout
        WordWrapCheckBox.IsChecked = _settings.WordWrap;
        NavPanelCheckBox.IsChecked = _settings.ShowNavigationPanel;
    }

    private void SaveSettings()
    {
        // Theme
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
                _settings.Theme = theme;

        // Typography
        _settings.FontFamily = FontFamilyBox.Text ?? "Inter, Segoe UI, -apple-system, sans-serif";
        _settings.FontSize = (double)(FontSizeBox.Value ?? 15);
        _settings.CodeFontFamily = CodeFontBox.Text ?? "Cascadia Code, JetBrains Mono, Consolas, monospace";

        // Layout
        _settings.WordWrap = WordWrapCheckBox.IsChecked == true;
        _settings.ShowNavigationPanel = NavPanelCheckBox.IsChecked == true;

        _settings.Save();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        SaveSettings();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnResetDefaults(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();

        ThemeComboBox.SelectedIndex = 1; // Dark
        FontFamilyBox.Text = defaults.FontFamily;
        FontSizeBox.Value = (decimal)defaults.FontSize;
        CodeFontBox.Text = defaults.CodeFontFamily;
        WordWrapCheckBox.IsChecked = defaults.WordWrap;
        NavPanelCheckBox.IsChecked = defaults.ShowNavigationPanel;
    }
}