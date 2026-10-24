using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>The ASP.NET Core Identity user store on the Security tab: mode switch, validation and creating the tables.</summary>
public class IdentitySecurityTests
{
    private sealed class FakeIdentityTables : IIdentityTableService
    {
        public List<string> Missing { get; } = ["AspNetUsers", "AspNetRoles"];

        public int Created { get; private set; }

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<string>> MissingTablesAsync(string provider, string connectionString, string prefix) =>
            Failure is null ? Task.FromResult<IReadOnlyList<string>>(Missing.ToList()) : Task.FromException<IReadOnlyList<string>>(Failure);

        public Task<IReadOnlyList<string>> CreateMissingTablesAsync(string provider, string connectionString, string prefix)
        {
            Created++;
            var missing = Missing.ToList();
            Missing.Clear();
            return Task.FromResult<IReadOnlyList<string>>(missing);
        }

        public Task<IReadOnlyList<TableInfo>> ReadTablesAsync(string provider, string connectionString) =>
            Task.FromResult<IReadOnlyList<TableInfo>>(
            [
                new TableInfo { Name = "Customers", Fields = { new FieldInfo { Name = "Id", Type = FieldType.Integer, IsPrimaryKey = true } } },
                new TableInfo { Name = "AspNetUsers", Fields = { new FieldInfo { Name = "Id", Type = FieldType.Text, IsPrimaryKey = true } } },
                new TableInfo { Name = "AspNetRoles", Fields = { new FieldInfo { Name = "Id", Type = FieldType.Text, IsPrimaryKey = true } } },
            ]);
    }

    private static (MainViewModel Main, FakeDialogService Dialogs) IdentityProject()
    {
        var (main, dialogs) = CustomizationTests.Project();
        // Resolve needs an existing file; the fake service never opens it.
        var dbPath = Path.Combine(Path.GetTempPath(), "WebAppForge.Desktop.Tests", "identity-fake.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        File.WriteAllBytes(dbPath, []);
        main.Current.Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath };
        main.Security.Enabled = true;
        main.Security.UserStoreChoice = SecurityViewModel.AspNetIdentityLabel;
        return (main, dialogs);
    }

    [Fact]
    public void TheIdentityStoreReplacesTheUserTableChecksWithItsOwnRules()
    {
        var (main, _) = CustomizationTests.Project();
        main.Security.Enabled = true;
        Assert.True(main.Security.IsProjectTable);
        Assert.Contains("user table", main.Security.ValidationMessage);

        main.Security.UserStoreChoice = SecurityViewModel.AspNetIdentityLabel;
        Assert.Equal(UserStore.AspNetIdentity, main.Current.Security.UserStore);
        Assert.True(main.Security.IsIdentity);
        Assert.Null(main.Security.ValidationMessage);
        Assert.True(main.Security.CanGenerate);

        // Settings reach the model; bad ones block Generate with the generator's message.
        main.Security.TablePrefix = "Id_";
        main.Security.RequireDigit = true;
        main.Security.MinPasswordLengthText = "12";
        main.Security.AdminLevelText = "50";
        main.Security.RegisteredUserLevelText = "1";
        var identity = main.Current.Security.Identity;
        Assert.Equal("Id_", identity.TablePrefix);
        Assert.True(identity.RequireDigit);
        Assert.Equal(12, identity.MinPasswordLength);
        Assert.Equal(50, identity.AdminLevel);
        Assert.Equal(1, identity.RegisteredUserLevel);
        Assert.Null(main.Security.ValidationMessage);

        main.Security.AdminPasswordVariable = "1BAD";
        Assert.Contains("environment variable", main.Security.ValidationMessage);
        Assert.False(main.Security.CanGenerate);
        main.Security.AdminPasswordVariable = "SITE_ADMIN_PASSWORD";
        main.Security.RegisteredUserLevelText = "50";
        Assert.Contains("lower than the administrator level", main.Security.ValidationMessage);
        main.Security.RegisteredUserLevelText = "-1";
        Assert.NotNull(main.Security.InputMessage);
        Assert.Equal(50, identity.RegisteredUserLevel);
        main.Security.RegisteredUserLevelText = "0";
        Assert.Null(main.Security.ValidationMessage);

        // Project-table settings left over are notes, not problems.
        main.Current.Security.UserTable = "Customers";
        main.Security.Refresh();
        Assert.True(main.Security.HasIdentityNotes);
        Assert.True(main.Security.CanGenerate);

        main.Security.UserStoreChoice = SecurityViewModel.ProjectTableLabel;
        Assert.Equal(UserStore.ProjectTable, main.Current.Security.UserStore);
        Assert.False(main.Security.HasIdentityNotes);
    }

    [Fact]
    public async Task CreatingTheTablesAsksFirstThenAddsThemAsNotGenerated()
    {
        var (main, dialogs) = IdentityProject();
        var fake = new FakeIdentityTables();
        main.IdentityTables = fake;

        dialogs.NextConfirm = false;
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Equal(0, fake.Created);
        Assert.Contains("Nothing was created", main.Security.IdentityTablesMessage);

        dialogs.NextConfirm = true;
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Equal(1, fake.Created);
        Assert.Contains("Created 2 identity table(s)", main.Security.IdentityTablesMessage);
        var users = main.Current.Tables.Single(t => t.Name == "AspNetUsers");
        Assert.False(users.Generate);
        Assert.False(main.Current.Tables.Single(t => t.Name == "AspNetRoles").Generate);
        Assert.True(main.Current.Tables.Single(t => t.Name == "Customers").Generate);

        // Nothing missing: no question, a message only.
        var asked = dialogs.ConfirmCount;
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Equal(asked, dialogs.ConfirmCount);
        Assert.Contains("identity tables exist", main.Security.IdentityTablesMessage);

        fake.Failure = new InvalidOperationException("disk full");
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Contains("disk full", main.Security.IdentityTablesMessage);
    }

    [Fact]
    public async Task WithoutADatabaseOrWithABadPrefixNothingIsCreated()
    {
        var (main, _) = IdentityProject();
        var fake = new FakeIdentityTables();
        main.IdentityTables = fake;

        main.Security.TablePrefix = "9bad";
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Contains("not a valid identity table prefix", main.Security.IdentityTablesMessage);

        main.Security.TablePrefix = "AspNet";
        main.Current.Database = new DatabaseSettings { Provider = "sqlite", Path = "relative/app.db" };
        await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
        Assert.Equal(0, fake.Created);
        Assert.NotNull(main.Security.IdentityTablesMessage);
        Assert.DoesNotContain("Created", main.Security.IdentityTablesMessage);
    }

    [Fact]
    public async Task TheRealServiceCreatesTheTablesInASqliteDatabase()
    {
        using var folder = new TempFolder();
        var dbPath = folder.File("app.db");
        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE notes (id INTEGER PRIMARY KEY, owner TEXT, body TEXT)";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var dialogs = new FakeDialogService { NextConfirm = true };
        var main = new MainViewModel(dialogs);
        main.Current.Database = new DatabaseSettings { Provider = "sqlite", Path = dbPath };
        main.Security.Enabled = true;
        main.Security.UserStoreChoice = SecurityViewModel.AspNetIdentityLabel;
        try
        {
            await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
            Assert.Contains("Created 7 identity table(s)", main.Security.IdentityTablesMessage);
            Assert.Contains(main.Current.Tables, t => t.Name == "notes" && t.Generate);
            var identityTables = main.Current.Tables.Where(t => t.Name.StartsWith("AspNet", StringComparison.Ordinal)).ToList();
            Assert.Equal(7, identityTables.Count);
            Assert.All(identityTables, t => Assert.False(t.Generate));

            await main.Security.CreateIdentityTablesCommand.ExecuteAsync(null);
            Assert.Contains("All 7 identity tables exist", main.Security.IdentityTablesMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    [AvaloniaFact]
    public void TheSecurityTabShowsTheIdentitySettingsOnlyForTheIdentityStore()
    {
        var (main, _) = CustomizationTests.Project();
        main.Security.Enabled = true;
        var window = new MainWindow { DataContext = main };
        window.Show();
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = MainViewModel.TabNames.ToList().IndexOf("Security");
        void Settle()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.UpdateLayout();
            }
        }

        Settle();
        T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>()
            .First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id);

        Assert.False(Find<Expander>("IdentityExpander").IsVisible);
        Find<ComboBox>("UserStoreComboBox").SelectedItem = SecurityViewModel.AspNetIdentityLabel;
        Settle();

        Assert.Equal(UserStore.AspNetIdentity, main.Current.Security.UserStore);
        Assert.True(Find<Expander>("IdentityExpander").IsEffectivelyVisible);
        Assert.True(Find<Button>("CreateIdentityTablesButton").IsEffectivelyEnabled);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Expander>(), e => e.Header as string == "Login options" && e.IsEffectivelyVisible);
    }
}
