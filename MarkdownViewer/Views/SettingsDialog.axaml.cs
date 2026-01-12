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
        DarkModeCheckBox.IsChecked = _settings.IsDarkMode;
        FontFamilyBox.Text = _settings.FontFamily;
        FontSizeBox.Value = (decimal)_settings.FontSize;
        CodeFontBox.Text = _settings.CodeFontFamily;
        WordWrapCheckBox.IsChecked = _settings.WordWrap;
    }

    private void SaveSettings()
    {
        _settings.IsDarkMode = DarkModeCheckBox.IsChecked == true;
        _settings.FontFamily = FontFamilyBox.Text ?? "Inter, Segoe UI, sans-serif";
        _settings.FontSize = (double)(FontSizeBox.Value ?? 14);
        _settings.CodeFontFamily = CodeFontBox.Text ?? "Cascadia Code, Consolas, monospace";
        _settings.WordWrap = WordWrapCheckBox.IsChecked == true;
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
        DarkModeCheckBox.IsChecked = defaults.IsDarkMode;
        FontFamilyBox.Text = defaults.FontFamily;
        FontSizeBox.Value = (decimal)defaults.FontSize;
        CodeFontBox.Text = defaults.CodeFontFamily;
        WordWrapCheckBox.IsChecked = defaults.WordWrap;
    }
}
