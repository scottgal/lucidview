using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MarkdownViewer.Models;

namespace MarkdownViewer.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog()
    {
        InitializeComponent();
        _settings = new AppSettings();
        LoadFontFamilies();
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
        FontFamilyComboBox.Text = _settings.FontFamily;
        SelectFontFamily(_settings.FontFamily);
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
        var fontFamily = FontFamilyComboBox.Text;
        if (string.IsNullOrWhiteSpace(fontFamily))
            fontFamily = FontFamilyComboBox.SelectedItem as string;
        _settings.FontFamily = fontFamily ?? "Inter, Segoe UI, -apple-system, sans-serif";
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
        FontFamilyComboBox.Text = defaults.FontFamily;
        SelectFontFamily(defaults.FontFamily);
        FontSizeBox.Value = (decimal)defaults.FontSize;
        CodeFontBox.Text = defaults.CodeFontFamily;
        WordWrapCheckBox.IsChecked = defaults.WordWrap;
        NavPanelCheckBox.IsChecked = defaults.ShowNavigationPanel;
    }

    private void LoadFontFamilies()
    {
        try
        {
            var fonts = FontManager.Current.SystemFonts;
            var names = fonts
                .Select(font => font.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            FontFamilyComboBox.ItemsSource = names;
        }
        catch
        {
            FontFamilyComboBox.ItemsSource = Array.Empty<string>();
        }
    }

    private void SelectFontFamily(string fontFamily)
    {
        if (FontFamilyComboBox.ItemsSource is not IEnumerable<string> names)
            return;

        var primary = fontFamily.Split(',')[0].Trim();
        var match = names.FirstOrDefault(name =>
            name.Equals(primary, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            FontFamilyComboBox.SelectedItem = match;
    }
}
