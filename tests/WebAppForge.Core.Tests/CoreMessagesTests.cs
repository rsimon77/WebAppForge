using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>
/// Runs the tests in English whatever the machine's UI language: the tests compare English messages.
/// </summary>
internal static class EnglishMessages
{
    [ModuleInitializer]
    internal static void Pin() => CoreMessages.Culture = CultureInfo.GetCultureInfo("en");
}

/// <summary>Tests that switch the language of the Core messages run alone, so no other test sees the other language.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MessageLanguageCollection
{
    public const string Name = "Message language";
}

/// <summary>The messages Core gives the editor: every key exists in English and Hungarian, with the same placeholders.</summary>
[Collection(MessageLanguageCollection.Name)]
public class CoreMessagesTests
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu");

    // Literal keys of CoreMessages.Text/Format (also through the alias M). Keys built at run time are checked below.
    private static readonly Regex CodeKey = new("\\b(?:CoreMessages|M)\\.(?:Text|Format)\\(\"([A-Za-z0-9_]+)\"[),]");

    [Fact]
    public void EveryKeyTheCodeUsesIsInMessagesResx()
    {
        var keys = Texts("Messages.resx").Keys.ToHashSet(StringComparer.Ordinal);
        var missing = Directory.EnumerateFiles(CoreFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => CodeKey.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(k => !keys.Contains(k.Key))
            .Select(k => $"{k.File}: {k.Key}")
            .ToList();
        Assert.True(missing.Count == 0, "Keys missing from Messages.resx:\n" + string.Join("\n", missing));

        // Keys built from enum names and setting names.
        var built = Enum.GetNames<AggregateFunction>().Select(n => "Function_" + n)
            .Concat(Enum.GetValues<DateInterval>().Where(i => i != DateInterval.None).Select(i => "Interval_" + i))
            .Concat(new[] { "Favicon", "Logo" }.Select(n => "Generator_Asset" + n))
            .Concat(new[] { "DateRange", "Filter", "Group", "DetailColumn", "Row", "Column", "Category", "Series", "Date", "EndDate", "Title", "Value" }.Select(n => "ReportField_" + n));
        Assert.All(built, k => Assert.Contains(k, keys));
        Assert.All(new[] { "x", "table.a", "field.a.b", "report.a", "site.title", "menu.a", "page.a", "fieldui.a", "group.a", "formpage.a", "detail.a" },
            k => Assert.DoesNotContain("TextKind_", LocalizationKeys.KindOf(k), StringComparison.Ordinal));
    }

    [Fact]
    public void HungarianHasEveryEnglishKeyWithTheSamePlaceholders()
    {
        var english = Texts("Messages.resx");
        var hungarian = Texts("Messages.hu.resx");
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), hungarian.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(hungarian, e => string.IsNullOrWhiteSpace(e.Value));
        Assert.DoesNotContain(english.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase), g => g.Count() > 1);

        var placeholder = new Regex("\\{[0-9a-z]+\\}");
        var wrong = hungarian
            .Where(e => !placeholder.Matches(e.Value).Select(m => m.Value).Order().SequenceEqual(placeholder.Matches(english[e.Key]).Select(m => m.Value).Order()))
            .Select(e => e.Key)
            .ToList();
        Assert.True(wrong.Count == 0, "Placeholders differ from English in: " + string.Join(", ", wrong));
    }

    [Fact]
    public void MessagesFollowTheChosenLanguage()
    {
        var reports = new List<ReportInfo> { new() { Name = "Sales", Kind = ReportKind.Summary } };
        Assert.Equal("Report Sales: choose the table it reads.", ReportValidator.Problems(reports, []).Single());
        try
        {
            CoreMessages.Culture = Hungarian;
            Assert.Equal("„Sales” jelentés: válassza ki a táblát, amelyből a jelentés olvas.", ReportValidator.Problems(reports, []).Single());
            Assert.Equal("Tábla", LocalizationKeys.KindOf("table.items"));

            // A key that a language lacks falls back to English; a key missing everywhere shows as the key.
            CoreMessages.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Table", LocalizationKeys.KindOf("table.items"));
            Assert.Equal("No_Such_Key", CoreMessages.Text("No_Such_Key"));
        }
        finally
        {
            EnglishMessages.Pin();
        }
    }

    private static Dictionary<string, string> Texts(string file)
        => XDocument.Load(Path.Combine(CoreFolder(), "Localization", file)).Root!
            .Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "", StringComparer.Ordinal);

    private static string CoreFolder([CallerFilePath] string? file = null)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "src", "WebAppForge.Core"));
}
