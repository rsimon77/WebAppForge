using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WebAppForge.Desktop.Localization;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>The desktop UI texts: the localizer, the UI language option and the guard against hard-coded text.</summary>
public class LocalizationTests
{
    // The same attribute list as scripts/extract-desktop-strings.py.
    private static readonly Regex Literal = new(
        "(?<=\\s)(Text|Header|Content|ToolTip\\.Tip|PlaceholderText|Watermark|Title|AutomationProperties\\.Name|AutomationProperties\\.HelpText)=\"([^\"{][^\"]*)\"");

    private static readonly Regex TabTag = new("<TabItem\\b[^>]*?\\sTag=\"([^\"{][^\"]*)\"");
    private static readonly Regex TrKey = new("\\{l:Tr ([A-Za-z0-9_]+)\\}");
    // Literal keys only: a key built at run time ("Tab_" + id) is checked by its own test.
    private static readonly Regex CodeKey = new("Localizer\\.Instance(?:\\[|\\.Text\\(|\\.Format\\()\"([A-Za-z0-9_]+)\"[\\]),]|\\btr\\.(?:Text|Format)\\(\"([A-Za-z0-9_]+)\"[),]");

    // ---- Localizer ----

    [Fact]
    public void AKeyResolvesInTheCurrentLanguageAndFallsBackToEnglishThenToTheKey()
    {
        var localizer = new Localizer(new FakeResources(), CultureInfo.GetCultureInfo("en"));
        Assert.Equal("Open", localizer["Open"]);

        localizer.SetCulture(CultureInfo.GetCultureInfo("hu"));
        Assert.Equal("Megnyitás", localizer["Open"]);
        Assert.Equal("Only English", localizer["EnglishOnly"]);
        Assert.Equal("Missing_Key", localizer["Missing_Key"]);
    }

    [Fact]
    public void SwitchingTheLanguageRefreshesBoundTextsAndRaisesCultureChanged()
    {
        var localizer = new Localizer(new FakeResources(), CultureInfo.GetCultureInfo("en"));
        var text = localizer.Text("Open");
        Assert.Same(text, localizer.Text("Open"));
        var changed = new List<string?>();
        ((INotifyPropertyChanged)text).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var raised = 0;
        localizer.CultureChanged += (_, _) => raised++;

        localizer.SetCulture(CultureInfo.GetCultureInfo("hu"));
        localizer.SetCulture(CultureInfo.GetCultureInfo("hu"));

        Assert.Equal("Megnyitás", text.Value);
        Assert.Equal([nameof(LocalizedText.Value)], changed);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void FormatFillsInTheArgumentsWithTheLanguagesNumberFormat()
    {
        var localizer = new Localizer(new FakeResources(), CultureInfo.GetCultureInfo("hu"));
        Assert.Equal("1,5 tábla", localizer.Format("Tables", 1.5));
    }

    [Fact]
    public void ResolveUsesTheSettingOrTheSystemLanguage()
    {
        Assert.Equal("hu", Localizer.Resolve("hu").Name);
        Assert.Equal(CultureInfo.InstalledUICulture, Localizer.Resolve(null));
        Assert.Equal(CultureInfo.InstalledUICulture, Localizer.Resolve("  "));
    }

    [Fact]
    public void EnglishIsAlwaysAvailableAndListedFirst()
        => Assert.Equal("en", Localizer.AvailableCultures()[0].Name);

    // ---- UI language option ----

    [Fact]
    public void TheUiLanguageOptionStartsAtTheSystemLanguageAndIsSaved()
    {
        var options = new OptionsViewModel(new AppSettings(), _ => { }, new FakeDialogService());
        Assert.Null(options.UiLanguage!.Name);
        Assert.Null(options.ToSettings().UiLanguage);

        options.UiLanguage = options.UiLanguages.Single(l => l.Name == "en");
        Assert.Equal("en", options.ToSettings().UiLanguage);

        var reopened = new OptionsViewModel(options.ToSettings(), _ => { }, new FakeDialogService());
        Assert.Equal("en", reopened.UiLanguage!.Name);
    }

    [Fact]
    public void AnUnknownStoredLanguageShowsAsTheSystemLanguage()
    {
        var options = new OptionsViewModel(new AppSettings { UiLanguage = "xx-unknown" }, _ => { }, new FakeDialogService());
        Assert.Null(options.UiLanguage!.Name);
    }

    [Fact]
    public void ABlankStoredLanguageIsNormalizedToTheSystemLanguage()
        => Assert.Null(new AppSettings { UiLanguage = " " }.Normalized().UiLanguage);

    // ---- Guard: no hard-coded text, no missing keys ----

    [Fact]
    public void TheViewsHaveNoHardCodedText()
    {
        var ignored = File.ReadAllLines(Path.Combine(RepositoryRoot(), "scripts", "desktop-strings-ignore.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
        var found = new List<string>();
        foreach (var file in ViewFiles())
        {
            var source = File.ReadAllText(file);
            var values = Literal.Matches(source).Select(m => m.Groups[2].Value)
                .Concat(TabTag.Matches(source).Select(m => m.Groups[1].Value))
                .Select(v => System.Net.WebUtility.HtmlDecode(v) ?? v)
                .Where(v => v.Any(char.IsAsciiLetter) && !ignored.Contains(v));
            found.AddRange(values.Select(v => $"{Path.GetFileName(file)}: {v}"));
        }

        Assert.True(found.Count == 0,
            "Text in the views must come from Localization/Strings.resx. Run 'python scripts/extract-desktop-strings.py' to move it, "
            + "or add a value that is never translated to scripts/desktop-strings-ignore.txt:\n" + string.Join("\n", found));
    }

    [Fact]
    public void EveryKeyTheAppUsesIsInStringsResx()
    {
        var keys = ResxKeys();
        var missing = new List<string>();
        foreach (var file in ViewFiles())
        {
            missing.AddRange(TrKey.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value)
                .Where(k => !keys.Contains(k)).Select(k => $"{Path.GetFileName(file)}: {k}"));
        }

        foreach (var file in Directory.EnumerateFiles(DesktopFolder(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            missing.AddRange(CodeKey.Matches(File.ReadAllText(file))
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Where(k => !keys.Contains(k)).Select(k => $"{Path.GetFileName(file)}: {k}"));
        }

        Assert.True(missing.Count == 0, "Keys missing from Strings.resx:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EverySectionHasATranslatedTitle()
    {
        var keys = ResxKeys();
        foreach (var tab in MainViewModel.TabNames)
        {
            // A missing key shows as the key itself.
            Assert.False(MainViewModel.TabTitle(tab).StartsWith("Tab_", StringComparison.Ordinal), $"No Tab_ key for the '{tab}' section.");
        }

        Assert.Equal(MainViewModel.TabNames.Count, keys.Count(k => k.StartsWith("Tab_", StringComparison.Ordinal)));
    }

    [Fact]
    public void KeysDifferInMoreThanCase()
    {
        // The resource compiler treats names without case and drops the second of two such keys.
        var clashes = ResxKeys().GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => string.Join(" / ", g)).ToList();
        Assert.True(clashes.Count == 0, "Keys that differ only in case: " + string.Join(", ", clashes));
    }

    [Fact]
    public void EveryFieldDesignerSectionHasATitle()
    {
        var keys = ResxKeys();
        Assert.All(FieldDesignerViewModel.Sections, s => Assert.Contains("FieldDesigner_Section" + s, keys));
    }

    // ---- Hungarian ----

    [Fact]
    public void HungarianHasEveryEnglishKeyAndNoOthers()
    {
        var english = ResxTexts("Strings.resx");
        var hungarian = ResxTexts("Strings.hu.resx");
        var missing = english.Keys.Except(hungarian.Keys).Order().ToList();
        var extra = hungarian.Keys.Except(english.Keys).Order().ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"Strings.hu.resx is out of step with Strings.resx.\nMissing: {string.Join(", ", missing)}\nNot in English: {string.Join(", ", extra)}");
        Assert.DoesNotContain(hungarian, e => string.IsNullOrWhiteSpace(e.Value));
    }

    [Fact]
    public void HungarianKeepsThePlaceholdersOfTheEnglishText()
    {
        var english = ResxTexts("Strings.resx");
        var placeholder = new Regex("\\{[0-9a-z]+\\}");
        var wrong = ResxTexts("Strings.hu.resx")
            .Where(e => !placeholder.Matches(e.Value).Select(m => m.Value).Order().SequenceEqual(placeholder.Matches(english[e.Key]).Select(m => m.Value).Order()))
            .Select(e => e.Key)
            .ToList();
        Assert.True(wrong.Count == 0, "Placeholders differ from English in: " + string.Join(", ", wrong));
    }

    [Fact]
    public void TheHungarianSatelliteIsBuiltAndOffered()
    {
        Assert.Contains(Localizer.AvailableCultures(), c => c.Name == "hu");
        var localizer = new Localizer(
            new ResourceManager("WebAppForge.Desktop.Localization.Strings", typeof(Localizer).Assembly), CultureInfo.GetCultureInfo("hu-HU"));
        Assert.Equal("Táblák", localizer["Tab_Tables"]);
    }

    private static Dictionary<string, string> ResxTexts(string file)
        => XDocument.Load(Path.Combine(DesktopFolder(), "Localization", file)).Root!
            .Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "", StringComparer.Ordinal);

    private static HashSet<string> ResxKeys() => ResxTexts("Strings.resx").Keys.ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> ViewFiles() => Directory.EnumerateFiles(Path.Combine(DesktopFolder(), "Views"), "*.axaml");

    private static string DesktopFolder() => Path.Combine(RepositoryRoot(), "src", "WebAppForge.Desktop");

    private static string RepositoryRoot([CallerFilePath] string? file = null)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    /// <summary>English and Hungarian texts in memory: "Open" in both, "EnglishOnly" in English only.</summary>
    private sealed class FakeResources : ResourceManager
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            [""] = new() { ["Open"] = "Open", ["EnglishOnly"] = "Only English", ["Tables"] = "{0} tables" },
            ["hu"] = new() { ["Open"] = "Megnyitás", ["Tables"] = "{0} tábla" },
        };

        public override string? GetString(string name, CultureInfo? culture)
        {
            var language = culture?.TwoLetterISOLanguageName ?? "";
            return Texts.TryGetValue(language, out var texts) && texts.TryGetValue(name, out var text)
                ? text
                : Texts[""].GetValueOrDefault(name);
        }
    }
}
