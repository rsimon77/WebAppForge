using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of <see cref="LocalizationSettings"/>:
/// &lt;localization enabled defaultLanguage languages autoTranslate provider notes&gt;&lt;entry key language source reviewed sourceHash&gt;text&lt;/entry&gt;&lt;/localization&gt;.
/// The element is left out when everything is at its default. API keys are never written.
/// </summary>
internal static class LocalizationXml
{
    public static void Append(XElement root, LocalizationSettings settings)
    {
        if (settings.IsDefault)
        {
            return;
        }

        var element = new XElement("localization",
            new XAttribute("enabled", settings.Enabled),
            new XAttribute("defaultLanguage", settings.DefaultLanguage),
            new XAttribute("languages", string.Join(",", settings.AllLanguages)),
            new XAttribute("autoTranslate", settings.AutoTranslate),
            new XAttribute("provider", settings.TranslationProvider.ToString()),
            new XAttribute("notes", settings.TranslationNotes));
        foreach (var entry in settings.Entries)
        {
            element.Add(new XElement("entry",
                new XAttribute("key", entry.Key),
                new XAttribute("language", entry.Language),
                new XAttribute("source", entry.Source.ToString()),
                new XAttribute("reviewed", entry.Reviewed),
                string.IsNullOrEmpty(entry.SourceHash) ? null : new XAttribute("sourceHash", entry.SourceHash),
                entry.Text));
        }

        root.Add(element);
    }

    public static void Read(XElement root, Project project)
    {
        // Older project files have no <localization> element; the defaults keep localization off.
        if (root.Element("localization") is not { } element)
        {
            return;
        }

        var settings = new LocalizationSettings
        {
            Enabled = Bool(element, "enabled", false),
            DefaultLanguage = Text(element, "defaultLanguage", "en"),
            AutoTranslate = Bool(element, "autoTranslate", false),
            TranslationProvider = Enum.TryParse<TranslationProviderKind>(element.Attribute("provider")?.Value, out var provider)
                ? provider
                : TranslationProviderKind.None,
            TranslationNotes = Text(element, "notes", string.Empty),
        };
        foreach (var code in Text(element, "languages", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!settings.Languages.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                settings.Languages.Add(code);
            }
        }

        foreach (var entry in element.Elements("entry"))
        {
            settings.Entries.Add(new TranslationEntry
            {
                Key = entry.Attribute("key")?.Value ?? throw new InvalidDataException(Localization.CoreMessages.Format("ProjectFile_MissingAttribute", "entry", "key")),
                Language = entry.Attribute("language")?.Value ?? throw new InvalidDataException(Localization.CoreMessages.Format("ProjectFile_MissingAttribute", "entry", "language")),
                Source = Enum.TryParse<TranslationSource>(entry.Attribute("source")?.Value, out var source) ? source : TranslationSource.Manual,
                Reviewed = Bool(entry, "reviewed", false),
                SourceHash = Text(entry, "sourceHash", string.Empty),
                Text = entry.Value,
            });
        }

        project.Localization = settings;
    }

    private static string Text(XElement element, string name, string fallback) => element.Attribute(name)?.Value ?? fallback;

    private static bool Bool(XElement element, string name, bool fallback) =>
        bool.TryParse(element.Attribute(name)?.Value, out var value) ? value : fallback;
}
