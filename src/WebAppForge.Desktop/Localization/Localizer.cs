using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using WebAppForge.Core.Localization;

namespace WebAppForge.Desktop.Localization;

/// <summary>
/// The texts of the desktop app in the chosen UI language. English is in Localization/Strings.resx; another language is a
/// Strings.&lt;culture&gt;.resx next to it, built into a satellite assembly. A key missing in that language falls back to English,
/// and a key missing everywhere shows as the key itself, so it is easy to spot.
/// </summary>
public sealed class Localizer
{
    /// <summary>The language of Strings.resx.</summary>
    public static readonly CultureInfo NeutralCulture = CultureInfo.GetCultureInfo("en");

    private readonly ResourceManager _resources;
    private readonly Dictionary<string, LocalizedText> _texts = new(StringComparer.Ordinal);

    public Localizer(ResourceManager resources, CultureInfo culture)
    {
        _resources = resources;
        Culture = culture;
    }

    /// <summary>
    /// The app-wide instance the views bind to through <see cref="TrExtension"/>. The messages of Core (<see cref="CoreMessages"/>)
    /// follow its language.
    /// </summary>
    public static Localizer Instance { get; } = CreateInstance();

    private static Localizer CreateInstance()
    {
        var localizer = new Localizer(new ResourceManager("WebAppForge.Desktop.Localization.Strings", typeof(Localizer).Assembly), CultureInfo.CurrentUICulture);
        CoreMessages.Culture = localizer.Culture;
        localizer.CultureChanged += (_, _) => CoreMessages.Culture = localizer.Culture;
        return localizer;
    }

    /// <summary>The UI language in effect.</summary>
    public CultureInfo Culture { get; private set; }

    /// <summary>Raised after <see cref="SetCulture"/> changed the language; bound texts have been refreshed by then.</summary>
    public event EventHandler? CultureChanged;

    /// <summary>The text of <paramref name="key"/> in the current language.</summary>
    public string this[string key] => _resources.GetString(key, Culture) ?? key;

    /// <summary>The text of <paramref name="key"/> with {0}, {1}... filled in, formatted for the current language.</summary>
    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    /// <summary>A bindable text for <paramref name="key"/>; its Value changes when the language changes.</summary>
    public LocalizedText Text(string key)
    {
        lock (_texts)
        {
            if (!_texts.TryGetValue(key, out var text))
            {
                text = new LocalizedText(this, key);
                _texts.Add(key, text);
            }

            return text;
        }
    }

    /// <summary>Switches the UI language and refreshes every bound text. Number and date formats are not changed.</summary>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (Equals(culture, Culture))
        {
            return;
        }

        Culture = culture;
        LocalizedText[] texts;
        lock (_texts)
        {
            texts = [.. _texts.Values];
        }

        foreach (var text in texts)
        {
            text.Refresh();
        }

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The UI language for a stored setting: the named culture, or the operating system's UI language when the setting is
    /// empty or not a known culture name.
    /// </summary>
    public static CultureInfo Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return CultureInfo.GetCultureInfo(name.Trim());
            }
            catch (CultureNotFoundException)
            {
                // Fall back to the system language below.
            }
        }

        return CultureInfo.InstalledUICulture;
    }

    /// <summary>English plus every language with a satellite assembly next to the app (a folder such as "hu" holding WebAppForge.Desktop.resources.dll).</summary>
    public static IReadOnlyList<CultureInfo> AvailableCultures()
    {
        var assembly = typeof(Localizer).Assembly;
        var folder = Path.GetDirectoryName(assembly.Location);
        var satellite = assembly.GetName().Name + ".resources.dll";
        var cultures = new List<CultureInfo> { NeutralCulture };
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                if (!File.Exists(Path.Combine(dir, satellite)))
                {
                    continue;
                }

                try
                {
                    cultures.Add(CultureInfo.GetCultureInfo(Path.GetFileName(dir)));
                }
                catch (CultureNotFoundException)
                {
                    // Not a culture folder.
                }
            }
        }

        return cultures.DistinctBy(c => c.Name).OrderBy(c => c.Name == NeutralCulture.Name ? 0 : 1).ThenBy(c => c.NativeName, StringComparer.CurrentCulture).ToList();
    }
}

/// <summary>One translated text the views bind to. <see cref="Value"/> follows the UI language.</summary>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs ValueChanged = new(nameof(Value));
    private readonly Localizer _owner;

    internal LocalizedText(Localizer owner, string key)
    {
        _owner = owner;
        Key = key;
    }

    public string Key { get; }

    public string Value => _owner[Key];

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() => PropertyChanged?.Invoke(this, ValueChanged);

    public override string ToString() => Value;
}
