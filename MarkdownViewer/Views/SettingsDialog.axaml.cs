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
        // Theme
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Auto => 0,
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            AppTheme.VSCode => 3,
            AppTheme.GitHub => 4,
            AppTheme.MostlyLucidDark => 5,
            AppTheme.MostlyLucidLight => 6,
            _ => 0
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

        ThemeComboBox.SelectedIndex = 0; // Auto (system)
        SelectFontFamily(FontFamilyComboBox, defaults.FontFamily);
        FontSizeBox.Value = (decimal)defaults.FontSize;
        SelectFontFamily(CodeFontComboBox, defaults.CodeFontFamily);
        WordWrapCheckBox.IsChecked = defaults.WordWrap;
        NavPanelCheckBox.IsChecked = defaults.ShowNavigationPanel;
        _isLoading = false;
        ApplySettingsLive();
    }

    /// <summary>
    /// The bundled Raleway entry — shown at the top of the font lists so users
    /// can pick it without having to know the avares URI. Saved settings still
    /// store the full FontFamily string with system fallbacks.
    /// </summary>
    private const string RalewayDisplayName = "Raleway (bundled)";
    private const string RalewayFamilyValue = "avares://lucidVIEW/Assets/Raleway-Regular.ttf#Raleway, Segoe UI, Inter, Arial, sans-serif";

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
            // Bundled fonts first
            _fontNames.Add(RalewayDisplayName);
            foreach (var name in names)
                _fontNames.Add(name);
        }
        catch
        {
            _fontNames.Clear();
            _fontNames.Add(RalewayDisplayName);
        }

        FontFamilyComboBox.ItemsSource = _fontNames;
        CodeFontComboBox.ItemsSource = _fontNames;
    }

    /// <summary>
    /// Extracts a human-friendly font name from a FontFamily string. Handles:
    /// - Plain names: "Cascadia Code" → "Cascadia Code"
    /// - Comma-separated lists: "Inter, Segoe UI, sans-serif" → "Inter"
    /// - avares URIs: "avares://lucidVIEW/Assets/Raleway-Regular.ttf#Raleway, ..." → the bundled Raleway display name
    /// </summary>
    private static string ExtractDisplayName(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)) return string.Empty;
        var primary = fontFamily.Split(',')[0].Trim();

        // avares://...#FontName — pull the family name from the fragment.
        if (primary.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            // The bundled Raleway entry maps to its display name regardless of fragment.
            if (primary.Contains("Raleway", StringComparison.OrdinalIgnoreCase))
                return RalewayDisplayName;
            var hashIdx = primary.LastIndexOf('#');
            if (hashIdx >= 0 && hashIdx < primary.Length - 1)
                return primary.Substring(hashIdx + 1);
            return primary;
        }

        return primary;
    }

    private void SelectFontFamily(ComboBox comboBox, string fontFamily)
    {
        var displayName = ExtractDisplayName(fontFamily);
        if (string.IsNullOrWhiteSpace(displayName)) return;

        var match = _fontNames.FirstOrDefault(name =>
            name.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        // If the user has a font configured that isn't installed, add it to the
        // top of the list so they can see it's selected. We never insert raw
        // avares:// URIs — ExtractDisplayName has already cleaned that up.
        if (match == null)
        {
            _fontNames.Insert(0, displayName);
            match = displayName;
        }

        comboBox.SelectedItem = match;
    }

    private static string ReadComboValue(ComboBox comboBox, string fallback)
    {
        var value = comboBox.SelectedItem as string ?? comboBox.Text;
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        // Map the bundled Raleway display name back to its full avares URI + fallbacks
        // so the saved setting works the same as before.
        if (value == RalewayDisplayName) return RalewayFamilyValue;
        return value;
    }
}
