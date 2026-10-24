using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>The Languages tab view model: language list, settings, grid editing, filters, translation with a fake provider and CSV.</summary>
public class LanguagesTests
{
    public sealed record Rig(
        MainViewModel Main, FakeTranslationProvider Provider, FakeProviderFactory Factory, FakeDialogService Dialogs, FakeApiKeys Keys)
    {
        public LanguagesViewModel Languages => Main.Languages;

        public LocalizationSettings Settings => Main.Current.Localization;
    }

    /// <summary>English project with two tables, languages de, hu and ar, automatic translation on with Google and a key present.</summary>
    public static Rig Setup(bool autoTranslate = true, TranslationProviderKind kind = TranslationProviderKind.Google)
    {
        var dialogs = new FakeDialogService { NextConfirm = true };
        var provider = new FakeTranslationProvider();
        var factory = new FakeProviderFactory(provider);
        var keys = new FakeApiKeys();
        var main = new MainViewModel(dialogs) { Tools = new ToolServices { TranslationProviders = factory, ApiKeys = keys } };

        var customers = new TableInfo { Name = "Customers", Caption = "Customer", Generate = true };
        customers.Fields.Add(new FieldInfo { Name = "Name", Caption = "Customer name" });
        customers.Fields.Add(new FieldInfo { Name = "Email" });
        main.Current.Tables.Add(customers);
        var orders = new TableInfo { Name = "Orders", Generate = true };
        orders.Fields.Add(new FieldInfo { Name = "Total", Caption = "Order total" });
        main.Current.Tables.Add(orders);

        var settings = main.Current.Localization;
        settings.Enabled = true;
        settings.Languages.AddRange(["de", "hu", "ar"]);
        settings.AutoTranslate = autoTranslate;
        settings.TranslationProvider = kind;
        main.Languages.Reload();
        return new Rig(main, provider, factory, dialogs, keys);
    }

    private static int KeyCount(Rig rig) => LocalizationKeys.Enumerate(rig.Main.Current).Count;

    private static TranslationRow Row(Rig rig, string key) => rig.Languages.Rows.Single(r => r.Key == key);

    // ---- Language list ----

    [Fact]
    public void LanguageListHasTheDefaultFirstAndMarksRightToLeftLanguages()
    {
        var rig = Setup();

        Assert.Equal(["en", "de", "hu", "ar"], rig.Languages.Languages.Select(l => l.Code));
        Assert.True(rig.Languages.Languages[0].IsDefault);
        Assert.False(rig.Languages.Languages[0].CanRemove);
        var arabic = rig.Languages.Languages.Single(l => l.Code == "ar");
        Assert.True(arabic.IsRightToLeft);
        Assert.Equal("RTL", arabic.RightToLeftMarker);
        Assert.Equal(string.Empty, rig.Languages.Languages.Single(l => l.Code == "hu").RightToLeftMarker);
        Assert.Equal("Arabic", arabic.EnglishName);
        Assert.True(LanguageCatalog.IsRightToLeft("ar-EG"));
        Assert.True(LanguageCatalog.IsRightToLeft("he"));
        Assert.False(LanguageCatalog.IsRightToLeft("de"));
    }

    [Fact]
    public void AddLanguageNormalizesTheCodeAndRefusesBadOrRepeatedCodes()
    {
        var rig = Setup();

        rig.Languages.NewLanguageText = "pt_br";
        rig.Languages.AddLanguageCommand.Execute(null);
        Assert.Contains("pt-BR", rig.Settings.Languages);
        Assert.Equal(string.Empty, rig.Languages.NewLanguageText);
        Assert.Contains("pt-BR", rig.Languages.LanguageCodes);

        Assert.False(rig.Languages.AddLanguageCode("DE"));
        Assert.Contains("already", rig.Languages.Message);
        Assert.False(rig.Languages.AddLanguageCode("not a code!"));
        Assert.True(rig.Languages.HasMessage);
        Assert.False(rig.Languages.AddLanguageCode("  "));
        Assert.Equal(["de", "hu", "ar", "pt-BR"], rig.Settings.Languages);

        Assert.True(rig.Languages.AddLanguageCode("zh-hans"));
        Assert.Contains("zh-Hans", rig.Settings.Languages);
        Assert.False(rig.Languages.HasMessage);
    }

    [Fact]
    public async Task PickLanguageOffersCatalogLanguagesNotYetUsedWithNativeNames()
    {
        var rig = Setup();
        rig.Dialogs.NextChoice = "fr - French (français)";

        await rig.Languages.PickLanguageCommand.ExecuteAsync(null);

        Assert.Contains("fr", rig.Settings.Languages);
        Assert.DoesNotContain(rig.Dialogs.LastChoicesOffered, c => c.StartsWith("de - "));
        Assert.Contains("ja - Japanese (日本語)", rig.Dialogs.LastChoicesOffered);

        rig.Dialogs.NextChoice = null;
        var before = rig.Settings.Languages.Count;
        await rig.Languages.PickLanguageCommand.ExecuteAsync(null);
        Assert.Equal(before, rig.Settings.Languages.Count);
    }

    [Fact]
    public void RemoveLanguageRefusesTheDefaultAndKeepsStoredTranslations()
    {
        var rig = Setup();
        rig.Settings.SetManual("ui.add", "hu", "Hozzáadás");
        rig.Languages.Reload();

        rig.Languages.RemoveLanguageCommand.Execute(rig.Languages.Languages[0]);
        Assert.Contains("default language cannot be removed", rig.Languages.Message);
        Assert.Equal("en", rig.Settings.DefaultLanguage);

        rig.Languages.RemoveLanguageCommand.Execute(rig.Languages.Languages.Single(l => l.Code == "hu"));
        Assert.DoesNotContain("hu", rig.Settings.Languages);
        Assert.DoesNotContain("hu", rig.Languages.LanguageCodes);
        Assert.False(rig.Languages.HasMessage);
        Assert.NotNull(rig.Settings.Find("ui.add", "hu"));
        Assert.All(rig.Languages.Rows, r => Assert.Null(r.CellFor("hu")));
    }

    [Fact]
    public void ChangingTheDefaultLanguageKeepsTheOldOneAsALanguageOfTheSite()
    {
        var rig = Setup();

        rig.Languages.DefaultLanguage = "de";

        Assert.Equal("de", rig.Settings.DefaultLanguage);
        Assert.Equal(["de", "hu", "ar", "en"], rig.Settings.AllLanguages);
        Assert.True(rig.Languages.Languages.Single(l => l.Code == "de").IsDefault);
        Assert.False(rig.Languages.Languages.Single(l => l.Code == "en").IsDefault);

        // A null from a combo box that rebuilds its list is not a choice.
        rig.Languages.DefaultLanguage = null!;
        Assert.Equal("de", rig.Settings.DefaultLanguage);

        // Captions are now German; the fixed UI strings stay English, so the English column of captions is editable.
        var caption = Row(rig, "table.Customers");
        Assert.Equal("de", caption.SourceLanguage);
        Assert.False(caption.CellFor("en")!.IsSource);
        Assert.True(Row(rig, "ui.add").CellFor("en")!.IsSource);
    }

    [Fact]
    public async Task EditingAnySettingMarksTheProjectDirty()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        var rig = new Rig(main, new FakeTranslationProvider(), null!, null!, new FakeApiKeys());
        await main.SaveProjectCommand.ExecuteAsync(null);
        Assert.False(main.IsDirty);

        var edits = new (string Name, System.Action Edit)[]
        {
            ("enabled", () => rig.Languages.Enabled = true),
            ("auto", () => rig.Languages.AutoTranslate = true),
            ("provider", () => rig.Languages.Provider = TranslationProviderKind.Claude),
            ("notes", () => rig.Languages.TranslationNotes = "Formal tone"),
            ("add", () => rig.Languages.AddLanguageCode("fr")),
            ("default", () => rig.Languages.DefaultLanguage = "fr"),
            ("cell", () => Row(rig, "ui.add").CellFor("fr")!.Text = "Ajouter"),
        };

        foreach (var (name, edit) in edits)
        {
            await main.SaveProjectCommand.ExecuteAsync(null);
            Assert.False(main.IsDirty, name);
            edit();
            Assert.True(main.IsDirty, name);
        }

        await main.SaveProjectCommand.ExecuteAsync(null);
        rig.Languages.RemoveLanguage(rig.Languages.Languages.Single(l => l.Code == "en"));
        Assert.True(main.IsDirty, "remove");
        Assert.DoesNotContain("en", rig.Settings.AllLanguages);
    }

    [Fact]
    public void ProviderStatusNamesTheEnvironmentVariableButNeverShowsTheKey()
    {
        var rig = Setup(kind: TranslationProviderKind.None);
        Assert.Contains("No translation service", rig.Languages.ProviderStatus);
        Assert.False(rig.Languages.IsProviderReady);

        rig.Languages.Provider = TranslationProviderKind.Google;
        Assert.Contains(EnvironmentApiKeyProvider.GoogleVariable, rig.Languages.ProviderStatus);
        Assert.Contains("is set", rig.Languages.ProviderStatus);
        Assert.DoesNotContain(FakeApiKeys.SecretValue, rig.Languages.ProviderStatus);
        Assert.True(rig.Languages.IsProviderReady);

        rig.Keys.Set.Remove(TranslationProviderKind.Claude);
        rig.Languages.Provider = TranslationProviderKind.Claude;
        Assert.Contains(EnvironmentApiKeyProvider.ClaudeVariable, rig.Languages.ProviderStatus);
        Assert.Contains("is not set", rig.Languages.ProviderStatus);
        Assert.False(rig.Languages.IsProviderReady);
        Assert.False(rig.Languages.CanTranslate);
    }

    // ---- Grid ----

    [Fact]
    public void GridListsCaptionsAndFixedUiStringsWithTheirSourceText()
    {
        var rig = Setup();

        Assert.Equal(KeyCount(rig), rig.Languages.Rows.Count);
        Assert.Equal("Customer", Row(rig, "table.Customers").SourceText);
        Assert.Equal("Orders", Row(rig, "table.Orders").SourceText);
        Assert.Equal("Customer name", Row(rig, "field.Customers.Name").SourceText);
        Assert.Equal("Email", Row(rig, "field.Customers.Email").SourceText);
        Assert.Equal("Add", Row(rig, "ui.add").SourceText);
        Assert.Equal("UI", Row(rig, "ui.add").Kind);
        Assert.Equal("Table", Row(rig, "table.Orders").Kind);
        Assert.Equal("Field", Row(rig, "field.Orders.Total").Kind);
        Assert.Equal(["en", "de", "hu", "ar"], rig.Languages.VisibleLanguages);
        Assert.True(Row(rig, "ui.add").CellFor("en")!.IsSource);
    }

    [Fact]
    public void TheProjectTextsAppearWithTheirKind()
    {
        var rig = Setup();
        var project = rig.Main.Current;
        project.AspNet.Title = "Order desk";
        var customers = project.Tables.Single(t => t.Name == "Customers");
        customers.Ui.MenuGroup = "Sales";
        customers.Designs.List.Title = "Our customers";
        var name = customers.Fields.Single(f => f.Name == "Name");
        name.Ui.HelpText = "The full name.";
        name.Ui.Group = "Identity";
        customers.FormPages.Add(new FormPageDef { Name = "Main", Caption = "Main data" });
        rig.Languages.Reload();

        Assert.Equal(KeyCount(rig), rig.Languages.Rows.Count);
        Assert.Equal(("Site", "Order desk"), (Row(rig, LocalizationKeys.SiteTitle).Kind, Row(rig, LocalizationKeys.SiteTitle).SourceText));
        Assert.Equal("Menu", Row(rig, LocalizationKeys.MenuGroup("Sales")).Kind);
        Assert.Equal(("Page", "Our customers"), (Row(rig, LocalizationKeys.Page("Customers", "list", "title")).Kind, Row(rig, LocalizationKeys.Page("Customers", "list", "title")).SourceText));
        Assert.Equal("Field setting", Row(rig, LocalizationKeys.FieldUi("Customers", "Name", "help")).Kind);
        Assert.Equal("Field group", Row(rig, LocalizationKeys.FieldGroup("Customers", "Identity")).Kind);
        Assert.Equal("Form page", Row(rig, LocalizationKeys.FormPage("Customers", "Main", "caption")).Kind);
        Assert.True(Row(rig, LocalizationKeys.SiteTitle).CellFor("en")!.IsSource);
    }

    [Fact]
    public void EditingACellStoresAManualReviewedEntryWithTheSourceHash()
    {
        var rig = Setup();
        var cell = Row(rig, "table.Customers").CellFor("de")!;
        Assert.True(cell.IsMissing);
        Assert.False(cell.HasEntry);

        cell.Text = "Kunde";

        var entry = rig.Settings.Find("table.Customers", "de")!;
        Assert.Equal("Kunde", entry.Text);
        Assert.Equal(TranslationSource.Manual, entry.Source);
        Assert.True(entry.Reviewed);
        Assert.Equal(TranslationEntry.HashOf("Customer"), entry.SourceHash);
        Assert.True(cell.Reviewed);
        Assert.False(cell.IsMissing);
        Assert.False(cell.IsStale);
        Assert.Equal("Manual", cell.SourceLabel);

        // The source language column is not editable.
        var source = Row(rig, "table.Customers").CellFor("en")!;
        source.Text = "changed";
        Assert.Null(rig.Settings.Find("table.Customers", "en"));
        Assert.Equal("Customer", source.Text);

        cell.Text = string.Empty;
        Assert.Null(rig.Settings.Find("table.Customers", "de"));
        Assert.True(cell.IsMissing);
    }

    [Fact]
    public void EditingAMachineTranslationTurnsItIntoAManualReviewedEntry()
    {
        var rig = Setup();
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.save", Language = "de", Text = "Speichern?", Source = TranslationSource.Google,
            Reviewed = false, SourceHash = TranslationEntry.HashOf("Save"),
        });
        rig.Languages.Reload();
        var cell = Row(rig, "ui.save").CellFor("de")!;
        Assert.True(cell.IsUnreviewed);
        Assert.Equal("Google", cell.SourceLabel);

        cell.Text = "Speichern";

        var entry = rig.Settings.Find("ui.save", "de")!;
        Assert.Equal(TranslationSource.Manual, entry.Source);
        Assert.True(entry.Reviewed);
        Assert.False(cell.IsUnreviewed);
    }

    [Fact]
    public void ReviewedCheckboxApprovesAndWithdrawsAnApprovalButNeedsAnEntry()
    {
        var rig = Setup();
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.save", Language = "de", Text = "Speichern", Source = TranslationSource.Google,
            SourceHash = TranslationEntry.HashOf("Save"),
        });
        rig.Languages.Reload();

        var cell = Row(rig, "ui.save").CellFor("de")!;
        Assert.False(cell.Reviewed);
        cell.Reviewed = true;
        Assert.True(rig.Settings.Find("ui.save", "de")!.Reviewed);
        Assert.Equal(TranslationSource.Google, rig.Settings.Find("ui.save", "de")!.Source);
        cell.Reviewed = false;
        Assert.False(rig.Settings.Find("ui.save", "de")!.Reviewed);

        var empty = Row(rig, "ui.add").CellFor("de")!;
        empty.Reviewed = true;
        Assert.Null(rig.Settings.Find("ui.add", "de"));
        Assert.False(empty.Reviewed);
    }

    [Fact]
    public void StaleEntriesAreFlaggedWhenTheSourceTextChanged()
    {
        var rig = Setup();
        var customers = rig.Main.Current.Tables[0];
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "table.Customers", Language = "de", Text = "Kunde", Source = TranslationSource.Google,
            SourceHash = TranslationEntry.HashOf("Customer"),
        });
        rig.Languages.Reload();
        Assert.False(Row(rig, "table.Customers").CellFor("de")!.IsStale);

        customers.Caption = "Client";
        rig.Languages.Refresh();

        var cell = Row(rig, "table.Customers").CellFor("de")!;
        Assert.True(cell.IsStale);
        Assert.False(Row(rig, "ui.add").CellFor("de")!.IsStale);

        rig.Languages.OnlyStale = true;
        Assert.Equal(["table.Customers"], rig.Languages.Rows.Select(r => r.Key));

        // Typing a new translation records the new source text, so it is current again.
        cell.Text = "Klient";
        Assert.False(cell.IsStale);
    }

    [Fact]
    public void FiltersNarrowTheRowsByLanguageMissingUnreviewedStaleAndText()
    {
        var rig = Setup();
        Row(rig, "ui.add").CellFor("de")!.Text = "Hinzufügen";
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.edit", Language = "de", Text = "Bearbeiten?", Source = TranslationSource.Claude,
            Reviewed = false, SourceHash = TranslationEntry.HashOf("Edit"),
        });
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.delete", Language = "de", Text = "Löschen", Source = TranslationSource.Google,
            Reviewed = false, SourceHash = TranslationEntry.HashOf("Delete (old)"),
        });
        rig.Languages.Reload();
        var all = rig.Languages.Rows.Count;
        Assert.Equal(KeyCount(rig), all);

        rig.Languages.FilterLanguage = "de";
        Assert.Equal(["de"], rig.Languages.VisibleLanguages);
        Assert.Equal("de", rig.Languages.ActiveFilterLanguage);
        rig.Main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Languages");
        Assert.EndsWith("Languages > de", rig.Main.Breadcrumb);

        rig.Languages.OnlyMissing = true;
        Assert.Equal(all - 3, rig.Languages.Rows.Count);
        Assert.DoesNotContain(rig.Languages.Rows, r => r.Key is "ui.add" or "ui.edit" or "ui.delete");
        rig.Languages.OnlyMissing = false;

        rig.Languages.OnlyUnreviewed = true;
        Assert.Equal(["ui.delete", "ui.edit"], rig.Languages.Rows.Select(r => r.Key).Order());
        rig.Languages.OnlyUnreviewed = false;

        rig.Languages.OnlyStale = true;
        Assert.Equal(["ui.delete"], rig.Languages.Rows.Select(r => r.Key));
        rig.Languages.OnlyStale = false;

        rig.Languages.Search = "bearb";
        Assert.Equal(["ui.edit"], rig.Languages.Rows.Select(r => r.Key));
        rig.Languages.Search = "field.customers";
        Assert.Equal(2, rig.Languages.Rows.Count);
        rig.Languages.Search = string.Empty;

        // The filter language also decides which columns exist; the default language has no translatable cells.
        rig.Languages.FilterLanguage = "hu";
        Assert.Equal(["hu"], rig.Languages.VisibleLanguages);
        rig.Languages.OnlyMissing = true;
        Assert.Equal(all, rig.Languages.Rows.Count);
        rig.Languages.FilterLanguage = LanguagesViewModel.AllLanguagesLabel;
        Assert.Null(rig.Languages.ActiveFilterLanguage);
        Assert.Equal(["en", "de", "hu", "ar"], rig.Languages.VisibleLanguages);
    }

    // ---- Translating ----

    [Fact]
    public async Task TranslateAsksForConfirmationOnTheFirstRunOnlyAndListsProviderLanguagesAndCount()
    {
        var rig = Setup();
        var expected = KeyCount(rig) * 3;

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.Equal(1, rig.Dialogs.ConfirmCount);
        var text = rig.Dialogs.LastConfirmMessage!;
        Assert.Contains("Google", text);
        Assert.Contains("de, hu, ar", text);
        Assert.Contains($"Strings to send: {expected}", text);
        Assert.Equal(expected, rig.Provider.TextCount);
        Assert.Equal(expected, rig.Languages.LastTranslated);
        Assert.Equal(expected, rig.Settings.Entries.Count);
        var entry = rig.Settings.Find("ui.add", "hu")!;
        Assert.Equal("[hu] Add", entry.Text);
        Assert.Equal(TranslationSource.Google, entry.Source);
        Assert.False(entry.Reviewed);
        Assert.Equal(TranslationEntry.HashOf("Add"), entry.SourceHash);
        Assert.Contains($"Translated {expected}", rig.Languages.ResultSummary);
        Assert.False(rig.Languages.IsTranslating);

        // Everything is up to date now: nothing is sent and nothing is asked.
        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Equal(1, rig.Dialogs.ConfirmCount);
        Assert.Equal(expected, rig.Provider.TextCount);
        Assert.Contains("Nothing to translate", rig.Languages.ResultSummary);

        // A new language is a new target for the third party, so the user is asked again.
        rig.Languages.AddLanguageCode("fr");
        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Equal(2, rig.Dialogs.ConfirmCount);
        Assert.Contains("Target languages: fr", rig.Dialogs.LastConfirmMessage!);
        Assert.Contains("fr", rig.Provider.Calls.Select(c => c.To));
    }

    [Fact]
    public async Task DecliningTheConfirmationSendsNothing()
    {
        var rig = Setup();
        rig.Dialogs.NextConfirm = false;

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.Equal(1, rig.Dialogs.ConfirmCount);
        Assert.Empty(rig.Provider.Calls);
        Assert.Empty(rig.Factory.Created);
        Assert.Empty(rig.Settings.Entries);
        Assert.Contains("Nothing was sent", rig.Languages.TranslateMessage);

        // Asked again next time, since nothing was confirmed.
        rig.Dialogs.NextConfirm = true;
        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Equal(2, rig.Dialogs.ConfirmCount);
        Assert.NotEmpty(rig.Provider.Calls);
    }

    [Fact]
    public async Task NothingIsSentWhenAutoTranslateIsOffNoProviderIsChosenOrTheKeyIsMissing()
    {
        var off = Setup(autoTranslate: false);
        await off.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Empty(off.Provider.Calls);
        Assert.Equal(0, off.Dialogs.ConfirmCount);
        Assert.Contains("off", off.Languages.TranslateMessage);

        var none = Setup(kind: TranslationProviderKind.None);
        await none.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Empty(none.Provider.Calls);
        Assert.Equal(0, none.Dialogs.ConfirmCount);
        Assert.Contains("Choose a translation service", none.Languages.TranslateMessage);

        var noKey = Setup();
        noKey.Keys.Set.Clear();
        await noKey.Languages.TranslateMissingCommand.ExecuteAsync(null);
        Assert.Empty(noKey.Provider.Calls);
        Assert.Equal(0, noKey.Dialogs.ConfirmCount);
        Assert.Contains(EnvironmentApiKeyProvider.GoogleVariable, noKey.Languages.TranslateMessage);
        Assert.DoesNotContain(FakeApiKeys.SecretValue, noKey.Languages.TranslateMessage);
    }

    [Fact]
    public async Task ReviewedAndTypedTextsAreSkippedAndCounted()
    {
        var rig = Setup();
        Row(rig, "ui.add").CellFor("de")!.Text = "Hinzufügen";
        var edit = rig.Settings.Find("ui.edit", "de");
        Assert.Null(edit);
        rig.Settings.Entries.Add(new TranslationEntry
        {
            Key = "ui.save", Language = "de", Text = "Sichern", Source = TranslationSource.Google,
            Reviewed = true, SourceHash = TranslationEntry.HashOf("Save"),
        });
        rig.Languages.Reload();
        rig.Languages.FilterLanguage = "de";

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.Equal(2, rig.Languages.LastSkippedReviewed);
        Assert.Equal(KeyCount(rig) - 2, rig.Languages.LastTranslated);
        Assert.Equal("Hinzufügen", rig.Settings.Find("ui.add", "de")!.Text);
        Assert.Equal("Sichern", rig.Settings.Find("ui.save", "de")!.Text);
        Assert.Contains("skipped 2", rig.Languages.ResultSummary);
        Assert.All(rig.Provider.Calls, c => Assert.Equal("de", c.To));
        Assert.Null(rig.Settings.Find("ui.add", "hu"));
    }

    [Fact]
    public async Task OneLanguageFailingKeepsTheOthersAndReportsTheErrorMessage()
    {
        var rig = Setup();
        rig.Provider.FailLanguages.Add("hu");
        var perLanguage = KeyCount(rig);

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.Equal(perLanguage * 2, rig.Languages.LastTranslated);
        Assert.Equal(perLanguage, rig.Languages.LastFailed);
        Assert.Single(rig.Languages.LastFailures);
        Assert.Equal("hu: Fake service refused hu: quota exceeded.", rig.Languages.LastFailures[0]);
        Assert.Contains("Fake service refused hu: quota exceeded.", rig.Languages.ResultSummary);
        Assert.Contains($"failed {perLanguage}", rig.Languages.ResultSummary);
        Assert.NotNull(rig.Settings.Find("ui.add", "de"));
        Assert.NotNull(rig.Settings.Find("ui.add", "ar"));
        Assert.Null(rig.Settings.Find("ui.add", "hu"));
        Assert.False(rig.Languages.LastCancelled);
        Assert.False(rig.Languages.IsTranslating);
    }

    [Fact]
    public async Task CancelStopsAfterTheRunningLanguageAndKeepsWhatWasTranslated()
    {
        var rig = Setup();
        var perLanguage = KeyCount(rig);
        rig.Provider.OnCall = language =>
        {
            if (language == "hu")
            {
                rig.Languages.CancelTranslationCommand.Execute(null);
            }
        };

        await rig.Languages.TranslateMissingCommand.ExecuteAsync(null);

        Assert.True(rig.Languages.LastCancelled);
        Assert.Equal(perLanguage, rig.Languages.LastTranslated);
        Assert.Equal(["de", "hu"], rig.Provider.Calls.Select(c => c.To).Distinct());
        Assert.NotNull(rig.Settings.Find("ui.add", "de"));
        Assert.Null(rig.Settings.Find("ui.add", "hu"));
        Assert.Null(rig.Settings.Find("ui.add", "ar"));
        Assert.StartsWith("Cancelled.", rig.Languages.ResultSummary);
        Assert.False(rig.Languages.IsTranslating);
    }

    [Fact]
    public async Task TranslateSelectedRowsTouchesOnlyThoseRows()
    {
        var rig = Setup();
        var stale = new TranslationEntry
        {
            Key = "ui.save", Language = "de", Text = "Old machine text", Source = TranslationSource.Google,
            SourceHash = TranslationEntry.HashOf("Save (older)"),
        };
        rig.Settings.Entries.Add(stale);
        rig.Languages.Reload();

        await rig.Languages.TranslateSelectedCommand.ExecuteAsync(null);
        Assert.Contains("Select one or more rows", rig.Languages.TranslateMessage);
        Assert.Empty(rig.Provider.Calls);

        rig.Languages.SetSelectedRows([Row(rig, "table.Customers"), Row(rig, "ui.add")]);
        await rig.Languages.TranslateSelectedCommand.ExecuteAsync(null);

        Assert.Equal(6, rig.Languages.LastTranslated);
        Assert.Equal(6, rig.Provider.TextCount);
        Assert.NotNull(rig.Settings.Find("table.Customers", "ar"));
        Assert.NotNull(rig.Settings.Find("ui.add", "de"));
        Assert.Null(rig.Settings.Find("ui.edit", "de"));
        Assert.Same(stale, rig.Settings.Find("ui.save", "de"));
        Assert.Equal("Old machine text", stale.Text);
        Assert.Contains("Strings to send: 6", rig.Dialogs.LastConfirmMessage!);
    }

    // ---- CSV ----

    [Fact]
    public void CsvExportAndImportRoundTripTextsWithCommasQuotesAndLineBreaks()
    {
        using var folder = new TempFolder();
        var source = Setup();
        var tricky = "Ja, \"bitte\"\nZeile zwei";
        Row(source, "ui.add").CellFor("de")!.Text = tricky;
        Row(source, "table.Customers").CellFor("hu")!.Text = "Ügyfél";
        Row(source, "field.Customers.Email").CellFor("ar")!.Text = "البريد الإلكتروني";
        var path = folder.File("translations.csv");

        Assert.Equal(3, source.Languages.ExportTo(path));
        Assert.StartsWith(TranslationCsv.Header, File.ReadAllText(path));

        var target = Setup();
        Assert.Equal(3, target.Languages.ImportFrom(path));

        Assert.Equal(tricky, target.Settings.Find("ui.add", "de")!.Text);
        Assert.Equal("Ügyfél", target.Settings.Find("table.Customers", "hu")!.Text);
        Assert.Equal("البريد الإلكتروني", target.Settings.Find("field.Customers.Email", "ar")!.Text);
        var imported = target.Settings.Find("ui.add", "de")!;
        Assert.Equal(TranslationSource.Manual, imported.Source);
        Assert.True(imported.Reviewed);
        Assert.Equal(tricky, Row(target, "ui.add").CellFor("de")!.Text);
    }

    [Fact]
    public async Task CsvCommandsUseTheFilePickersAndImportSkipsUnknownKeysAndLanguages()
    {
        using var folder = new TempFolder();
        var rig = Setup();
        Row(rig, "ui.add").CellFor("de")!.Text = "Hinzufügen";

        rig.Dialogs.NextCsvToSave = null;
        await rig.Languages.ExportCsvCommand.ExecuteAsync(null);
        Assert.False(rig.Languages.HasResult);

        rig.Dialogs.NextCsvToSave = folder.File("out.csv");
        await rig.Languages.ExportCsvCommand.ExecuteAsync(null);
        Assert.True(File.Exists(folder.File("out.csv")));
        Assert.Contains("Exported 1", rig.Languages.ResultSummary);

        var csv = folder.File("in.csv");
        File.WriteAllText(csv, "key,language,text\r\nui.save,hu,Mentés\r\nui.nosuch,hu,x\r\nui.save,xx,y\r\nui.save,en,same\r\nbroken\r\n");
        rig.Dialogs.NextCsvToOpen = csv;
        await rig.Languages.ImportCsvCommand.ExecuteAsync(null);

        Assert.Equal("Mentés", rig.Settings.Find("ui.save", "hu")!.Text);
        Assert.Null(rig.Settings.Find("ui.save", "en"));
        Assert.Contains("Imported 1", rig.Languages.ResultSummary);
        Assert.Contains("1 for unknown keys", rig.Languages.ResultSummary);
        Assert.Contains("1 for languages not in the project", rig.Languages.ResultSummary);

        Assert.Equal(-1, rig.Languages.ImportFrom(folder.File("missing.csv")));
        Assert.StartsWith("Import failed", rig.Languages.ResultSummary);
    }

    [Fact]
    public void CsvParserHandlesQuotedFieldsAndBlankLines()
    {
        var records = TranslationCsv.Parse("﻿key,language,text\r\n\r\na,de,\"x,\"\"y\"\"\"\r\nb,hu,\"line1\nline2\"\r\nc,fr\r\n", out var problems);

        Assert.Equal(2, records.Count);
        Assert.Equal("x,\"y\"", records[0].Text);
        Assert.Equal("line1\nline2", records[1].Text);
        Assert.Single(problems);
    }
}
