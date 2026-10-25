using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Localization;

/// <summary>One text of a built-in translation pack and the hash of the English text it was translated from.</summary>
public sealed record PackEntry(string Text, string SourceHash)
{
    /// <summary>True when the English text changed after this translation was made; a stale entry is not used.</summary>
    public bool IsStaleFor(string english) => SourceHash != TranslationEntry.HashOf(english);
}

/// <summary>
/// Built-in translations of the fixed UI strings (<see cref="UiStrings"/>), shipped with the generator as embedded
/// Localization/Packs/&lt;language&gt;.json files. A project's own translation entry always wins over a pack entry, and a pack
/// entry whose English source changed since (see <see cref="PackEntry.IsStaleFor"/>) is ignored, so the page falls back to English.
/// </summary>
public static class TranslationPacks
{
    private const string ResourcePrefix = "WebAppForge.Core.Localization.Packs.";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, PackEntry>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Codes of the languages that have a pack, in order.</summary>
    public static IReadOnlyList<string> Languages { get; } = typeof(TranslationPacks).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
        .Select(n => n[ResourcePrefix.Length..^".json".Length])
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The pack of a language, keyed by UI string id: the exact code first, then its neutral language ("hu-HU" uses "hu").
    /// Empty when there is no pack.
    /// </summary>
    public static IReadOnlyDictionary<string, PackEntry> For(string language)
    {
        var code = (language ?? string.Empty).Trim();
        if (code.Length == 0)
        {
            return Empty;
        }

        var exact = Languages.FirstOrDefault(l => string.Equals(l, code, StringComparison.OrdinalIgnoreCase));
        var neutral = Languages.FirstOrDefault(l => string.Equals(l, code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        return exact is not null ? Cache.GetOrAdd(exact, Load) : neutral is not null ? Cache.GetOrAdd(neutral, Load) : Empty;
    }

    /// <summary>The pack text for a translation key ("ui." + id) in a language when it exists and is not stale, otherwise null.</summary>
    public static string? Find(string key, string language)
    {
        if (!key.StartsWith(UiStrings.KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = key[UiStrings.KeyPrefix.Length..];
        return For(language).TryGetValue(id, out var entry) && UiStrings.TryEnglish(id, out var english) && !entry.IsStaleFor(english)
            ? entry.Text
            : null;
    }

    /// <summary>Ids of a language's pack whose English source changed since they were translated.</summary>
    public static IReadOnlyList<string> StaleIds(string language) =>
        For(language).Where(p => UiStrings.TryEnglish(p.Key, out var english) && p.Value.IsStaleFor(english)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();

    private static readonly IReadOnlyDictionary<string, PackEntry> Empty = new Dictionary<string, PackEntry>();

    private static IReadOnlyDictionary<string, PackEntry> Load(string language)
    {
        using var stream = typeof(TranslationPacks).Assembly.GetManifestResourceStream(ResourcePrefix + language + ".json")
            ?? throw new InvalidOperationException($"The translation pack '{language}' is missing.");
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, PackEntry>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.GetProperty("entries").EnumerateObject())
        {
            result[item.Name] = new PackEntry(item.Value.GetProperty("text").GetString() ?? string.Empty, item.Value.GetProperty("source").GetString() ?? string.Empty);
        }

        return result;
    }
}
