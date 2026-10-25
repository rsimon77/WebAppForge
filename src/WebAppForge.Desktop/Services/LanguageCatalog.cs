using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.Services;

/// <summary>A language the Languages tab offers: BCP-47 code, English name, native name, and the writing direction.</summary>
public sealed record LanguageInfo(string Code, string EnglishName, string NativeName, bool IsRightToLeft)
{
    /// <summary>"hu - Hungarian (magyar)".</summary>
    public string Display => $"{Code} - {EnglishName} ({NativeName})";
}

/// <summary>Common languages with native names, BCP-47 code normalisation and the right-to-left marker.</summary>
public static class LanguageCatalog
{
    private static readonly Regex CodePattern = new(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Primary language subtags that are written right to left.</summary>
    private static readonly HashSet<string> RightToLeftPrimary = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "he", "iw", "fa", "ur", "yi", "ps", "sd", "ug", "dv", "ckb", "syr", "ks", "ku-Arab",
    };

    public static IReadOnlyList<LanguageInfo> Common { get; } =
    [
        new("ar", "Arabic", "العربية", true),
        new("bg", "Bulgarian", "български", false),
        new("cs", "Czech", "čeština", false),
        new("da", "Danish", "dansk", false),
        new("de", "German", "Deutsch", false),
        new("el", "Greek", "Ελληνικά", false),
        new("en", "English", "English", false),
        new("en-GB", "English (United Kingdom)", "English (United Kingdom)", false),
        new("en-US", "English (United States)", "English (United States)", false),
        new("es", "Spanish", "español", false),
        new("et", "Estonian", "eesti", false),
        new("fa", "Persian", "فارسی", true),
        new("fi", "Finnish", "suomi", false),
        new("fr", "French", "français", false),
        new("he", "Hebrew", "עברית", true),
        new("hi", "Hindi", "हिन्दी", false),
        new("hr", "Croatian", "hrvatski", false),
        new("hu", "Hungarian", "magyar", false),
        new("id", "Indonesian", "Bahasa Indonesia", false),
        new("it", "Italian", "italiano", false),
        new("ja", "Japanese", "日本語", false),
        new("ko", "Korean", "한국어", false),
        new("lt", "Lithuanian", "lietuvių", false),
        new("lv", "Latvian", "latviešu", false),
        new("nb", "Norwegian Bokmål", "norsk bokmål", false),
        new("nl", "Dutch", "Nederlands", false),
        new("pl", "Polish", "polski", false),
        new("pt", "Portuguese", "português", false),
        new("pt-BR", "Portuguese (Brazil)", "português (Brasil)", false),
        new("ro", "Romanian", "română", false),
        new("ru", "Russian", "русский", false),
        new("sk", "Slovak", "slovenčina", false),
        new("sl", "Slovenian", "slovenščina", false),
        new("sr", "Serbian", "српски", false),
        new("sv", "Swedish", "svenska", false),
        new("th", "Thai", "ไทย", false),
        new("tr", "Turkish", "Türkçe", false),
        new("uk", "Ukrainian", "українська", false),
        new("ur", "Urdu", "اردو", true),
        new("vi", "Vietnamese", "Tiếng Việt", false),
        new("zh-Hans", "Chinese (Simplified)", "简体中文", false),
        new("zh-Hant", "Chinese (Traditional)", "繁體中文", false),
    ];

    /// <summary>The catalog entry for a code (compared without case), or null for a code that is not listed.</summary>
    public static LanguageInfo? Find(string? code)
        => Common.FirstOrDefault(l => string.Equals(l.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True for languages written right to left (Arabic, Hebrew, Persian, Urdu and a few more), also for regional codes such as ar-EG.</summary>
    public static bool IsRightToLeft(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalized = code.Trim().Replace('_', '-');
        var primary = normalized.Split('-')[0];
        return RightToLeftPrimary.Contains(primary);
    }

    /// <summary>
    /// Normalises a typed BCP-47 code (en-us becomes en-US, zh-hans becomes zh-Hans) and returns false with a reason when
    /// it cannot be a language code.
    /// </summary>
    public static bool TryNormalize(string? text, out string code, out string error)
    {
        code = string.Empty;
        var trimmed = (text ?? string.Empty).Trim().Replace('_', '-');
        if (trimmed.Length == 0)
        {
            error = Localizer.Instance["LanguageCatalog_EnterALanguageCodeSuchAs"];
            return false;
        }

        if (!CodePattern.IsMatch(trimmed))
        {
            error = Localizer.Instance.Format("LanguageCatalog_IsNotALanguageCodeUse", trimmed);
            return false;
        }

        var parts = trimmed.Split('-');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = i == 0 ? parts[i].ToLowerInvariant()
                : parts[i].Length == 4 && parts[i].All(char.IsLetter) ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant()
                : parts[i].Length == 2 && parts[i].All(char.IsLetter) ? parts[i].ToUpperInvariant()
                : parts[i].ToLowerInvariant();
        }

        code = string.Join('-', parts);
        error = string.Empty;
        return true;
    }
}
