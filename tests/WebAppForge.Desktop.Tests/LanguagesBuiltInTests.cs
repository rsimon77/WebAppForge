using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.Tests;

/// <summary>The Languages tab with Hungarian, which has a built-in translation pack for the fixed UI strings.</summary>
public class LanguagesBuiltInTests
{
    private static LanguagesTests.Rig Hungarian()
    {
        var rig = LanguagesTests.Setup();
        rig.Settings.Languages.Clear();
        rig.Settings.Languages.Add("hu");
        rig.Main.Languages.Reload();
        return rig;
    }

    [Fact]
    public void AFixedTextShowsTheBuiltInTranslationAndCountsAsTranslated()
    {
        var rig = Hungarian();
        var cell = rig.Languages.Rows.Single(r => r.Key == "ui.save").CellFor("hu")!;

        Assert.True(cell.IsBuiltIn);
        Assert.False(cell.IsMissing);
        Assert.False(cell.HasEntry);
        Assert.Equal("Mentés", cell.Text);
        Assert.Equal("Built-in", cell.SourceLabel);

        // Captions have no pack: still missing.
        var caption = rig.Languages.Rows.Single(r => r.Key == LocalizationKeys.Table("Customers")).CellFor("hu")!;
        Assert.True(caption.IsMissing);
        Assert.False(caption.IsBuiltIn);

        rig.Languages.OnlyMissing = true;
        Assert.DoesNotContain(rig.Languages.Rows, r => r.Key == "ui.save");
    }

    [Fact]
    public void EditingABuiltInTextStoresAnOverrideAndClearingItBringsTheBuiltInTextBack()
    {
        var rig = Hungarian();
        var cell = rig.Languages.Rows.Single(r => r.Key == "ui.save").CellFor("hu")!;

        cell.Text = "Mentés";
        Assert.Null(rig.Settings.Find("ui.save", "hu"));

        cell.Text = "Rögzítés";
        var entry = rig.Settings.Find("ui.save", "hu")!;
        Assert.Equal("Rögzítés", entry.Text);
        Assert.Equal(TranslationSource.Manual, entry.Source);
        Assert.False(cell.IsBuiltIn);
        Assert.Equal("Manual", cell.SourceLabel);

        cell.Text = string.Empty;
        Assert.Null(rig.Settings.Find("ui.save", "hu"));
        Assert.True(cell.IsBuiltIn);
        Assert.Equal("Mentés", cell.Text);
    }

    [Fact]
    public async Task AutomaticTranslationSkipsTheTextsTheBuiltInPackCovers()
    {
        var rig = Hungarian();
        var captions = LocalizationKeys.Enumerate(rig.Main.Current).Count(s => !s.Key.StartsWith(UiStrings.KeyPrefix, System.StringComparison.Ordinal));

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.Equal(captions, rig.Provider.TextCount);
        Assert.Null(rig.Settings.Find("ui.save", "hu"));
        Assert.DoesNotContain(rig.Settings.Entries, e => e.Key.StartsWith(UiStrings.KeyPrefix, System.StringComparison.Ordinal));
        Assert.Equal("[hu] Customer", rig.Settings.Find(LocalizationKeys.Table("Customers"), "hu")!.Text);

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Contains("covered by the built-in translation", rig.Languages.ResultSummary);
    }
}
