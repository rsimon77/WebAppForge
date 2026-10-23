using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class SettingsTests
{
    [Fact]
    public void SettingsFileRoundTripKeepsTimeoutThemeDarkModeAndCustomStylesPath()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var saved = new AppSettings
        {
            SessionTimeoutMinutes = 45,
            ThemeName = "Green (success)",
            Appearance = AppearanceMode.Dark,
            CustomStylesPath = @"C:\styles\brand.axaml",
        };

        Assert.True(new SettingsStore(path).Save(saved));
        var loaded = new SettingsStore(path).Load();

        Assert.Equal(45, loaded.SessionTimeoutMinutes);
        Assert.Equal("Green (success)", loaded.ThemeName);
        Assert.Equal(AppearanceMode.Dark, loaded.Appearance);
        Assert.Equal(@"C:\styles\brand.axaml", loaded.CustomStylesPath);
    }

    [Fact]
    public void MissingSettingsFileGivesDefaults()
    {
        using var folder = new TempFolder();

        var loaded = new SettingsStore(folder.File("missing.json")).Load();

        Assert.Equal(30, loaded.SessionTimeoutMinutes);
        Assert.Equal("Blue (primary)", loaded.ThemeName);
        Assert.Equal(AppearanceMode.System, loaded.Appearance);
        Assert.Null(loaded.CustomStylesPath);
    }

    [Fact]
    public void UnreadableSettingsFileGivesDefaultsWithoutThrowing()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, "{ this is not json");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.DefaultTimeoutMinutes, loaded.SessionTimeoutMinutes);
    }

    [Fact]
    public void LoadedValuesOutOfRangeAreCorrected()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """{ "SessionTimeoutMinutes": 0, "ThemeName": "Not a palette" }""");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.MinTimeoutMinutes, loaded.SessionTimeoutMinutes);
        Assert.Equal(ThemePalettes.Default.Name, loaded.ThemeName);
    }

    [Fact]
    public void SaveCreatesMissingFolder()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "nested", "app", "settings.json");

        Assert.True(new SettingsStore(path).Save(new AppSettings()));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveReportsFailureWithoutThrowing()
    {
        using var folder = new TempFolder();

        // A folder cannot be written as a file, so the save fails and reports it.
        Assert.False(new SettingsStore(folder.Path).Save(new AppSettings()));
    }

    [Fact]
    public void SettingsAppliedThroughViewModelAreStoredInTheFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var main = new MainViewModel(new FakeDialogService(), new SettingsStore(path), appearance: null);

        main.ApplySettings(new AppSettings { SessionTimeoutMinutes = 5, Appearance = AppearanceMode.Light }, "Settings saved.");

        Assert.Equal("Settings saved.", main.Status);
        var reloaded = new SettingsStore(path).Load();
        Assert.Equal(5, reloaded.SessionTimeoutMinutes);
        Assert.Equal(AppearanceMode.Light, reloaded.Appearance);
    }

    [Fact]
    public void ThemePalettesHaveNineteenNamedColours()
    {
        Assert.Equal(19, ThemePalettes.All.Count);
        Assert.Equal(19, ThemePalettes.All.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Blue (primary)", ThemePalettes.Find("blue (PRIMARY)").Name);
        Assert.Equal(ThemePalettes.Default.Name, ThemePalettes.Find("unknown").Name);
    }

    [Fact]
    public void TableRowRuleMutesNotGeneratedAndWarnsTablesWithoutKey()
    {
        var notGenerated = new TableInfo { Name = "Audit", Generate = false };
        notGenerated.Fields.Add(new FieldInfo { Name = "Id", IsPrimaryKey = true });

        var noKey = new TableInfo { Name = "Log", Generate = true };
        noKey.Fields.Add(new FieldInfo { Name = "Message" });

        var keyed = new TableInfo { Name = "Orders", Generate = true };
        keyed.Fields.Add(new FieldInfo { Name = "OrderID", IsPrimaryKey = true });

        Assert.Equal(TableRowCategory.NotGenerated, TableRowRules.Categorize(notGenerated));
        Assert.Equal(TableRowCategory.NoPrimaryKey, TableRowRules.Categorize(noKey));
        Assert.Equal(TableRowCategory.Normal, TableRowRules.Categorize(keyed));
    }

    [Fact]
    public void TableRowRuleMutesNotGeneratedEvenWithoutKey()
    {
        var table = new TableInfo { Name = "Scratch", Generate = false };

        Assert.Equal(TableRowCategory.NotGenerated, TableRowRules.Categorize(table));
    }

    [Fact]
    public void PageSizeChoiceWritesPageSizeOfTheSelectedTable()
    {
        var main = ProjectWithTables();
        var users = main.Current.Tables[0];
        main.Tables.SelectedTable = users;

        Assert.True(main.Tables.SetPageSize(users, 50));
        Assert.Equal(50, users.Pages.PageSize);

        // The page-size box writes through the same property.
        main.Tables.SelectedPageSize = 100;
        Assert.Equal(100, users.Pages.PageSize);
    }

    [Fact]
    public void PageSizeOutsideTheOfferedChoicesIsRefused()
    {
        var main = ProjectWithTables();
        var users = main.Current.Tables[0];
        var before = users.Pages.PageSize;

        Assert.False(main.Tables.SetPageSize(users, 25));

        Assert.Equal(before, users.Pages.PageSize);
        Assert.Equal("Page size must be 10, 20, 50 or 100. The last valid value was kept.", main.Status);
    }

    [Fact]
    public void BreadcrumbFollowsTheTabAndTheSelection()
    {
        var main = ProjectWithTables();
        var users = main.Current.Tables[0];
        Assert.Equal("Project1 > Database", main.Breadcrumb);

        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Tables");
        main.Tables.SelectedTable = null;
        Assert.Equal("Project1 > Tables", main.Breadcrumb);

        main.Tables.SelectedTable = users;
        Assert.Equal("Project1 > Tables > Users", main.Breadcrumb);

        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Fields");
        main.Tables.SelectedField = users.Fields[0];
        Assert.Equal("Project1 > Fields > Users > UserName", main.Breadcrumb);

        main.Tables.SelectedTable = main.Current.Tables[1];
        Assert.Equal("Project1 > Fields > Logins", main.Breadcrumb);
    }

    [Fact]
    public void BreadcrumbShowsTheProjectName()
    {
        var main = ProjectWithTables();
        main.ActiveTabIndex = 0;

        main.ProjectName = "Shop";

        Assert.Equal("Shop > Database", main.Breadcrumb);
        Assert.Equal("Shop", main.Current.Name);
    }

    [Fact]
    public async Task UnsavedChangesDialogServiceReturnsTheChosenAction()
    {
        var dialogs = new FakeDialogService();

        foreach (var choice in new[] { UnsavedChangesChoice.Save, UnsavedChangesChoice.Discard, UnsavedChangesChoice.Cancel })
        {
            dialogs.NextUnsavedChoice = choice;
            Assert.Equal(choice, await dialogs.ConfirmUnsavedChangesAsync());
        }
    }

    [Fact]
    public async Task CancelKeepsTheUnsavedProject()
    {
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Cancel, NextOpenProject = "unused.xml" };
        var main = ProjectWithTables(dialogs);
        main.Current.Name = "Edited";
        main.ProjectName = "Edited";

        await main.OpenProjectCommand.ExecuteAsync(null);
        await main.NewProjectCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.UnsavedPromptCount);
        Assert.Equal("Edited", main.Current.Name);
        Assert.Equal(2, main.Current.Tables.Count);
    }

    [Fact]
    public async Task DiscardReplacesTheUnsavedProject()
    {
        using var folder = new TempFolder();
        var savedPath = folder.File("other.xml");
        ProjectFile.Save(new Project { Name = "Other" }, savedPath);

        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Discard, NextOpenProject = savedPath };
        var main = ProjectWithTables(dialogs);

        await main.OpenProjectCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.UnsavedPromptCount);
        Assert.Equal("Other", main.Current.Name);
        Assert.Empty(main.Current.Tables);
        Assert.False(main.IsDirty);
    }

    [Fact]
    public async Task SaveChoiceSavesThenReplacesTheProject()
    {
        using var folder = new TempFolder();
        var target = folder.File("project.xml");
        var dialogs = new FakeDialogService
        {
            NextUnsavedChoice = UnsavedChangesChoice.Save,
            NextSaveProject = target,
        };
        var main = ProjectWithTables(dialogs);

        await main.NewProjectCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal(2, ProjectFile.Load(target).Tables.Count);
        Assert.Empty(main.Current.Tables);
    }

    [Fact]
    public async Task CleanProjectIsReplacedWithoutAsking()
    {
        var dialogs = new FakeDialogService { NextOpenProject = null };
        var main = new MainViewModel(dialogs);

        await main.NewProjectCommand.ExecuteAsync(null);

        Assert.Equal(0, dialogs.UnsavedPromptCount);
    }

    [Fact]
    public void SessionIdleNoticeAppearsAfterTheTimeoutAndInputClearsIt()
    {
        var main = new MainViewModel(new FakeDialogService());
        var start = DateTime.UtcNow;

        main.CheckSession(start.AddMinutes(29));
        Assert.False(main.IsSessionIdle);

        main.CheckSession(start.AddMinutes(31));
        Assert.True(main.IsSessionIdle);

        main.NoteActivity(start.AddMinutes(31));
        Assert.False(main.IsSessionIdle);
    }

    [Fact]
    public void SessionTimeoutChangedInSettingsAppliesToTheTimer()
    {
        var main = new MainViewModel(new FakeDialogService());
        var start = DateTime.UtcNow;
        main.ApplySettings(new AppSettings { SessionTimeoutMinutes = 5 }, "ok");

        main.CheckSession(start.AddMinutes(6));

        Assert.True(main.IsSessionIdle);
    }

    /// <summary>A project with Users (UserName, PasswordHash) and Logins (UserName) tables, marked as not dirty only after a save.</summary>
    private static MainViewModel ProjectWithTables(FakeDialogService? dialogs = null)
    {
        var main = new MainViewModel(dialogs ?? new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
        main.Current.Tables.Add(users);

        var logins = new TableInfo { Name = "Logins", Generate = true };
        logins.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        main.Current.Tables.Add(logins);

        main.Tables.Reload();
        return main;
    }
}
