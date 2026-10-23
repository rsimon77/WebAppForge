using System.Security.Cryptography;
using System.Text;

namespace WebAppForge.Core.Model;

/// <summary>Which service the optional automatic translation uses.</summary>
public enum TranslationProviderKind
{
    None,
    Google,
    Claude,
}

/// <summary>Where the text of a translation entry came from.</summary>
public enum TranslationSource
{
    Manual,
    Google,
    Claude,
}

/// <summary>
/// Multi-language settings of a generated site. Nothing here is used unless <see cref="Enabled"/> is set, and nothing is
/// ever sent to a translation service unless <see cref="AutoTranslate"/> is set and the caller runs the translation service.
/// API keys are never part of the project; see IApiKeyProvider.
/// </summary>
public sealed class LocalizationSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>BCP-47 code of the language the captions of the project are written in.</summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>Language codes of the generated site. <see cref="AllLanguages"/> always adds <see cref="DefaultLanguage"/>.</summary>
    public List<string> Languages { get; } = [];

    public bool AutoTranslate { get; set; } = false;

    public TranslationProviderKind TranslationProvider { get; set; } = TranslationProviderKind.None;

    /// <summary>Free text (style, glossary) sent to the translation service together with the texts.</summary>
    public string TranslationNotes { get; set; } = string.Empty;

    /// <summary>Stored translations of table captions, field captions and the fixed UI strings.</summary>
    public List<TranslationEntry> Entries { get; } = [];

    /// <summary>The default language first, then the other configured languages without duplicates (compared without case).</summary>
    public IReadOnlyList<string> AllLanguages
    {
        get
        {
            var result = new List<string>();
            foreach (var code in new[] { DefaultLanguage }.Concat(Languages))
            {
                var trimmed = (code ?? string.Empty).Trim();
                if (trimmed.Length > 0 && !result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }
    }

    /// <summary>The entry for a key and language, or null.</summary>
    public TranslationEntry? Find(string key, string language) =>
        Entries.FirstOrDefault(e => e.Key == key && string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sets a manual translation, replacing any existing entry for the key and language. Manual entries are never overwritten by automatic translation.</summary>
    public TranslationEntry SetManual(string key, string language, string text)
    {
        Entries.RemoveAll(e => e.Key == key && string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase));
        var entry = new TranslationEntry { Key = key, Language = language, Text = text, Source = TranslationSource.Manual, Reviewed = true };
        Entries.Add(entry);
        return entry;
    }

    /// <summary>True when the settings hold nothing but defaults, so the project file does not need a localization element.</summary>
    internal bool IsDefault =>
        !Enabled && DefaultLanguage == "en" && Languages.Count == 0 && !AutoTranslate
        && TranslationProvider == TranslationProviderKind.None && TranslationNotes.Length == 0 && Entries.Count == 0;
}

/// <summary>One translated text. Keys are "table.name", "field.table.name" and "ui.id".</summary>
public sealed class TranslationEntry
{
    public string Key { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public TranslationSource Source { get; set; } = TranslationSource.Manual;

    /// <summary>True once a person has approved the text. Reviewed entries are never overwritten by automatic translation.</summary>
    public bool Reviewed { get; set; }

    /// <summary>Hash of the source text this translation was made from. A different hash means the source changed since.</summary>
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Short stable hash of a source text, see <see cref="SourceHash"/>.</summary>
    public static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))[..16].ToLowerInvariant();
}
