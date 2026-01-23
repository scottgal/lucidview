using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MarkdownViewer.Models;

namespace MarkdownViewer.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<string> _fontNames = new();
    private bool _isLoading = true;

    /// <summary>
    /// Event fired when settings change - allows instant preview
    /// </summary>
    public event Action? SettingsChanged;

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
        _isLoading = false;

        // Wire up change events for instant preview
        ThemeComboBox.SelectionChanged += OnSettingChanged;
        FontFamilyComboBox.SelectionChanged += OnSettingChanged;
        FontSizeBox.ValueChanged += OnSettingChanged;
        CodeFontComboBox.SelectionChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        ApplySettingsLive();
    }

    private void ApplySettingsLive()
    {
        // Apply settings immediately for live preview
        if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
            if (Enum.TryParse<AppTheme>(themeName, out var theme))
                _settings.Theme = theme;

        _settings.FontFamily = ReadComboValue(FontFamilyComboBox,
            "Inter, Segoe UI, -apple-system, sans-serif");
        _settings.FontSize = (double)(FontSizeBox.Value ?? 15);
        _settings.CodeFontFamily = ReadComboValue(CodeFontComboBox,
            "Cascadia Code, JetBrains Mono, Consolas, monospace");

        SettingsChanged?.Invoke();
    }

    private void LoadSettings()
    {
        // Theme - map to index including new mostlylucid themes
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Light => 0,
            AppTheme.Dark => 1,
            AppTheme.VSCode => 2,
            AppTheme.GitHub => 3,
            AppTheme.MostlyLucidDark => 4,
            AppTheme.MostlyLucidLight => 5,
            _ => 1
        };

        // Typography
        SelectFontFamily(FontFamilyComboBox, _settings.FontFamily);
        FontSizeBox.Value = (decimal)_settings.FontSize;
        SelectFontFamily(CodeFontComboBox, _settings.CodeFontFamily);

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
        _settings.FontFamily = ReadComboValue(FontFamilyComboBox,
            "Inter, Segoe UI, -apple-system, sans-serif");
        _settings.FontSize = (double)(FontSizeBox.Value ?? 15);
        _settings.CodeFontFamily = ReadComboValue(CodeFontComboBox,
            "Cascadia Code, JetBrains Mono, Consolas, monospace");

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
        // Reload original settings to undo live preview changes
        var original = AppSettings.Load();
        _settings.Theme = original.Theme;
        _settings.FontFamily = original.FontFamily;
        _settings.FontSize = original.FontSize;
        _settings.CodeFontFamily = original.CodeFontFamily;
        SettingsChanged?.Invoke();
        Close();
    }

    private void OnResetDefaults(object? sender, RoutedEventArgs e)
    {
        _isLoading = true;
        var defaults = new AppSettings();

        ThemeComboBox.SelectedIndex = 1; // Dark
        SelectFontFamily(FontFamilyComboBox, defaults.FontFamily);
        FontSizeBox.Value = (decimal)defaults.FontSize;
        SelectFontFamily(CodeFontComboBox, defaults.CodeFontFamily);
        WordWrapCheckBox.IsChecked = defaults.WordWrap;
        NavPanelCheckBox.IsChecked = defaults.ShowNavigationPanel;
        _isLoading = false;
        ApplySettingsLive();
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
            _fontNames.Clear();
            foreach (var name in names)
                _fontNames.Add(name);
        }
        catch
        {
            _fontNames.Clear();
        }

        FontFamilyComboBox.ItemsSource = _fontNames;
        CodeFontComboBox.ItemsSource = _fontNames;
    }

    private void SelectFontFamily(ComboBox comboBox, string fontFamily)
    {
        var primary = fontFamily.Split(',')[0].Trim();
        if (_fontNames.Count == 0)
        {
            comboBox.Text = primary;
            return;
        }

        var match = _fontNames.FirstOrDefault(name =>
            name.Equals(primary, StringComparison.OrdinalIgnoreCase));
        if (match == null && !string.IsNullOrWhiteSpace(primary))
        {
            _fontNames.Insert(0, primary);
            match = primary;
        }

        if (!string.IsNullOrWhiteSpace(match))
        {
            comboBox.SelectedItem = match;
            comboBox.Text = match;
        }
    }

    private static string ReadComboValue(ComboBox comboBox, string fallback)
    {
        var value = comboBox.SelectedItem as string ?? comboBox.Text;
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
