using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.Services;

/// <summary>
/// Applies the appearance settings to the application: the UI language, the accent colour, the light or dark variant, and the custom
/// styles. The accent and the custom styles are resource dictionaries merged into the application resources, in that
/// order, so a custom file can override any accent key. Each call replaces both.
/// </summary>
public sealed class AvaloniaAppearance(Application application) : IAppearance
{
    /// <summary>Accent brush used by the app's own controls (the open tab pill, the session notice).</summary>
    public const string AccentBrushKey = "AppAccentBrush";

    /// <summary>The Fluent accent colour key; its light and dark shades are derived from the same colour.</summary>
    public const string AccentColorKey = "SystemAccentColor";

    /// <summary>The Fluent theme resource that sets the base font size of the window and its controls.</summary>
    public const string FontSizeKey = "ControlContentThemeFontSize";

    /// <summary>Row height used for DataGrids when "compact tables" is on.</summary>
    public const double CompactRowHeight = 24;

    /// <summary>A culture name (such as "en") that overrides the UI language setting. The desktop UI tests set it.</summary>
    public const string UiLanguageVariable = "WEBAPPFORGE_UI_LANGUAGE";

    private ResourceDictionary? _accent;
    private Style? _compactTables;
    private ResourceDictionary? _customStyles;

    public string? Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The environment variable wins over the setting, so UI tests always run in one language.
        var language = Environment.GetEnvironmentVariable(UiLanguageVariable);
        Localizer.Instance.SetCulture(Localizer.Resolve(string.IsNullOrWhiteSpace(language) ? settings.UiLanguage : language));

        application.RequestedThemeVariant = settings.Appearance switch
        {
            AppearanceMode.Light => ThemeVariant.Light,
            AppearanceMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        var resources = (ResourceDictionary)application.Resources;
        Remove(resources, _customStyles);
        Remove(resources, _accent);

        _accent = AccentDictionary(ThemePalettes.Find(settings.ThemeName).Color);
        _accent[FontSizeKey] = (double)Math.Clamp(settings.UiFontSize, AppSettings.MinUiFontSize, AppSettings.MaxUiFontSize);
        resources.MergedDictionaries.Add(_accent);

        if (_compactTables is not null)
        {
            application.Styles.Remove(_compactTables);
            _compactTables = null;
        }

        if (settings.CompactTables)
        {
            _compactTables = new Style(x => x.OfType<DataGrid>());
            _compactTables.Setters.Add(new Setter(DataGrid.RowHeightProperty, CompactRowHeight));
            application.Styles.Add(_compactTables);
        }

        var (customStyles, message) = LoadCustomStyles(settings.CustomStylesPath);
        if (customStyles is not null)
        {
            resources.MergedDictionaries.Add(customStyles);
        }

        _customStyles = customStyles;
        return message;
    }

    private static void Remove(ResourceDictionary resources, ResourceDictionary? dictionary)
    {
        if (dictionary is not null)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }
    }

    private static ResourceDictionary AccentDictionary(Color accent)
    {
        return new ResourceDictionary
        {
            [AccentColorKey] = accent,
            [$"{AccentColorKey}Light1"] = Mix(accent, Colors.White, 0.2),
            [$"{AccentColorKey}Light2"] = Mix(accent, Colors.White, 0.4),
            [$"{AccentColorKey}Light3"] = Mix(accent, Colors.White, 0.6),
            [$"{AccentColorKey}Dark1"] = Mix(accent, Colors.Black, 0.2),
            [$"{AccentColorKey}Dark2"] = Mix(accent, Colors.Black, 0.4),
            [$"{AccentColorKey}Dark3"] = Mix(accent, Colors.Black, 0.6),
            [AccentBrushKey] = new ImmutableSolidColorBrush(accent),
            ["AccentHoverBrush"] = new ImmutableSolidColorBrush(Mix(accent, Colors.Black, 0.15)),
            ["AccentPressedBrush"] = new ImmutableSolidColorBrush(Mix(accent, Colors.Black, 0.3)),
            ["AccentSubtleBrush"] = new ImmutableSolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B)),
            ["AccentSubtleHoverBrush"] = new ImmutableSolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)),
        };
    }

    /// <summary>
    /// Loads the custom styles file. A missing file is ignored quietly. A file that exists but does not load is
    /// reported in the returned message.
    /// </summary>
    private static (ResourceDictionary? Dictionary, string? Message) LoadCustomStyles(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            return (CustomStyleLoader.Load(path), null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, $"Custom styles were not loaded from {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        byte Blend(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return new Color(from.A, Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B));
    }
}
