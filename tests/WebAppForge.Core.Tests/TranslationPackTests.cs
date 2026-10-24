using System.Text.Json;
using System.Text.RegularExpressions;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

/// <summary>The built-in translation packs of the fixed UI strings, and their place between the project's entries and English.</summary>
public class TranslationPackTests
{
    public static IEnumerable<object[]> Packs() => TranslationPacks.Languages.Select(l => new object[] { l });

    [Fact]
    public void HungarianShipsAsABuiltInPack()
    {
        Assert.Contains("hu", TranslationPacks.Languages);
        Assert.Equal("Mentés", TranslationPacks.Find("ui.save", "hu"));
        Assert.Equal("Mentés", TranslationPacks.Find("ui.save", "hu-HU"));
        Assert.Equal("Mentés", TranslationPacks.Find("ui.save", "HU"));
        Assert.Null(TranslationPacks.Find("ui.save", "de"));
        Assert.Null(TranslationPacks.Find("table.customers", "hu"));
        Assert.Null(TranslationPacks.Find("ui.noSuchText", "hu"));
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void APackTranslatesEveryFixedTextAndKeepsItsPlaceholdersAndTags(string language)
    {
        var pack = TranslationPacks.For(language);
        var ids = UiStrings.Everything.Select(s => s.Id).ToHashSet();
        Assert.Empty(ids.Except(pack.Keys));
        Assert.Empty(pack.Keys.Except(ids));
        Assert.Empty(TranslationPacks.StaleIds(language));
        foreach (var ui in UiStrings.Everything)
        {
            var text = pack[ui.Id].Text;
            Assert.False(string.IsNullOrWhiteSpace(text), ui.Id);
            Assert.True(Shape(ui.English, "\\{\\d+\\}").SequenceEqual(Shape(text, "\\{\\d+\\}")), $"{language}/{ui.Id}: placeholders differ: '{text}'");
            Assert.True(Shape(ui.English, "<[^>]+>").SequenceEqual(Shape(text, "<[^>]+>")), $"{language}/{ui.Id}: HTML differs: '{text}'");
        }
    }

    private static IEnumerable<string> Shape(string text, string pattern) => Regex.Matches(text, pattern).Select(m => m.Value).Order(StringComparer.Ordinal);

    [Fact]
    public void AnEntryMadeFromAnotherEnglishTextIsStale()
    {
        var entry = new PackEntry("Mentés", TranslationEntry.HashOf("Save"));
        Assert.False(entry.IsStaleFor("Save"));
        Assert.True(entry.IsStaleFor("Save all"));
    }

    [Fact]
    public void TheProjectsOwnEntryWinsOverThePackAndThePackOverEnglish()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = MdSupport.SampleProject(Path.Combine(root, "md.db"));
            project.Localization.Enabled = true;
            project.Localization.Languages.AddRange(["hu", "de"]);
            project.Localization.SetManual("ui.save", "hu", "Rögzítés");
            project.Localization.Entries.Add(new TranslationEntry { Key = "ui.cancel", Language = "hu", Text = "Mégsem (gép)", Source = TranslationSource.Google });
            var files = MdSupport.GenerateText(project, root);

            var hu = JsonDocument.Parse(files["Localization/strings.hu.json"]).RootElement;
            Assert.Equal("Rögzítés", hu.GetProperty("ui.save").GetString());
            Assert.Equal("Mégsem (gép)", hu.GetProperty("ui.cancel").GetString());
            Assert.Equal("Szerkesztés", hu.GetProperty("ui.edit").GetString());
            // Captions are project texts: no pack, so they fall back to the default language.
            Assert.False(hu.TryGetProperty("table.customers", out _));
            // No German pack: the page falls back to the English text.
            Assert.False(JsonDocument.Parse(files["Localization/strings.de.json"]).RootElement.TryGetProperty("ui.edit", out _));
            // The pack is not written into the English file.
            Assert.Equal("Edit", JsonDocument.Parse(files["Localization/strings.en.json"]).RootElement.GetProperty("ui.edit").GetString());
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void AHungarianDefaultLanguageUsesThePackForTheFixedTexts()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var project = MdSupport.SampleProject(Path.Combine(root, "md.db"));
            project.Localization.Enabled = true;
            project.Localization.DefaultLanguage = "hu";
            var hu = JsonDocument.Parse(MdSupport.GenerateText(project, root)["Localization/strings.hu.json"]).RootElement;
            Assert.Equal("Keresés", hu.GetProperty("ui.search").GetString());
            Assert.Equal("customers", hu.GetProperty("table.customers").GetString());
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }

    [Fact]
    public void SitesWithoutLocalizationDoNotUseThePacks()
    {
        var root = MdSupport.TempFolder();
        Directory.CreateDirectory(root);
        try
        {
            var files = MdSupport.GenerateText(MdSupport.SampleProject(Path.Combine(root, "md.db")), root);
            Assert.DoesNotContain(files.Keys, k => k.StartsWith("Localization/", StringComparison.Ordinal));
            Assert.DoesNotContain(files.Values, v => v.Contains("Szerkesztés", StringComparison.Ordinal));
        }
        finally
        {
            MdSupport.TryDelete(root);
        }
    }
}
