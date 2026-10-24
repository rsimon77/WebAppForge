using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests;

/// <summary>The provider picker of the Database tab, the server connection settings and the checks before connecting or generating.</summary>
public class DatabaseTabTests
{
    private static ProviderChoice Choice(string key) => DatabaseViewModel.Providers.Single(p => p.Key == key);

    [Fact]
    public void TheProviderPickerListsEveryDatabaseAndWritesTheProject()
    {
        var main = new MainViewModel(new FakeDialogService());
        var database = main.Database;
        Assert.Equal(["SQLite", "SQL Server", "Azure SQL", "MySQL"], database.ProviderChoices.Select(p => p.Name));
        Assert.True(database.IsSqlite);

        database.SelectedProvider = Choice(DatabaseProviders.AzureSql);
        database.ConnectionString = "Server=tcp:x.database.windows.net;Database=d;Authentication=Active Directory Default";
        database.ConnectionStringVariable = "SHOP_DB";
        Assert.True(database.IsServer);
        Assert.Equal("azuresql", main.Current.Database.Provider);
        Assert.Equal("SHOP_DB", main.Current.Database.ConnectionStringVariable);
        Assert.Contains("Active Directory Default", database.ConnectionHint);
        Assert.Contains("database.windows.net", database.ConnectionPlaceholder);

        var other = new Project();
        other.Database = new DatabaseSettings { Provider = "mysql", ConnectionString = "Server=m" };
        database.LoadFrom(other);
        Assert.Equal("MySQL", database.SelectedProvider.Name);
        Assert.Equal("Server=m", database.ConnectionString);
        Assert.Equal(string.Empty, database.ConnectionStringVariable);
        Assert.Equal("azuresql", main.Current.Database.Provider);
    }

    [Fact]
    public void APasswordInTheConnectionStringIsWarnedAboutUnlessAVariableHoldsIt()
    {
        var database = new MainViewModel(new FakeDialogService()).Database;
        database.SelectedProvider = Choice(DatabaseProviders.MySql);
        database.ConnectionString = "Server=db;User ID=app;Password=secret";
        Assert.Contains("plain text", database.PasswordWarning);
        database.ConnectionString = "Server=db;User ID=app;Pwd=secret";
        Assert.NotNull(database.PasswordWarning);
        database.ConnectionString = "Server=db;Integrated Security=True";
        Assert.Null(database.PasswordWarning);

        database.ConnectionString = "Server=db;Password=secret";
        database.ConnectionStringVariable = "WEBAPPFORGE_UNSET_" + Guid.NewGuid().ToString("N")[..6];
        Assert.Null(database.PasswordWarning);
        Assert.Contains("is not set in the editor's environment", database.VariableState);
    }

    [Fact]
    public void ResolveNamesWhatIsMissingBeforeConnecting()
    {
        var main = new MainViewModel(new FakeDialogService());
        var database = main.Database;
        Assert.Equal("Enter the SQLite file path or use Browse first.", database.Resolve().Problem);
        database.DatabasePath = "data/app.db";
        Assert.StartsWith("Save the project first.", database.Resolve().Problem);

        database.SelectedProvider = Choice(DatabaseProviders.SqlServer);
        Assert.Equal("Enter the connection string on the Database section first.", database.Resolve().Problem);
        database.ConnectionString = "Server=s;Database=d";
        Assert.Equal(("Server=s;Database=d", (string?)null), database.Resolve());
        database.ConnectionStringVariable = "WEBAPPFORGE_UNSET_X";
        Assert.Equal("The environment variable WEBAPPFORGE_UNSET_X is not set in the editor's environment.", database.Resolve().Problem);
    }

    [Fact]
    public async Task GenerateNeedsAConnectionForAServerDatabase()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder);
        main.Database.SelectedProvider = Choice(DatabaseProviders.SqlServer);
        await main.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal("Generation blocked: enter the connection string or its environment variable on the Database section.", main.Status);
        Assert.False(Directory.Exists(folder.File("generated")));
    }

    [Fact]
    public async Task TestConnectionReportsTheDriversMessage()
    {
        var main = new MainViewModel(new FakeDialogService());
        main.Database.SelectedProvider = Choice(DatabaseProviders.MySql);
        main.Database.ConnectionString = "Server=127.0.0.1;Port=1;Database=none;User ID=x;Password=y;Connection Timeout=2";
        await main.Database.TestCommand.ExecuteAsync(null);
        Assert.StartsWith("Connection failed: ", main.Status);
    }

    /// <summary>Connects to the servers named by WEBAPPFORGE_SQLSERVER and WEBAPPFORGE_MYSQL, when set, and reads their system-free table list.</summary>
    [Theory]
    [InlineData(DatabaseProviders.SqlServer, "WEBAPPFORGE_SQLSERVER")]
    [InlineData(DatabaseProviders.MySql, "WEBAPPFORGE_MYSQL")]
    public async Task ConnectReadsTheTablesOfAServer(string provider, string variable)
    {
        if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 })
        {
            return;
        }

        var main = new MainViewModel(new FakeDialogService());
        main.Database.SelectedProvider = Choice(provider);
        main.Database.ConnectionStringVariable = variable;
        await main.Database.TestCommand.ExecuteAsync(null);
        Assert.Equal($"Connected to {DatabaseProviders.DisplayName(provider)}.", main.Status);
        await main.Database.ConnectCommand.ExecuteAsync(null);
        Assert.StartsWith("Connected: ", main.Status);
    }

    [AvaloniaFact]
    public void TheTabShowsTheFileOrTheConnectionStringOfTheChosenProvider()
    {
        var main = new MainViewModel(new FakeDialogService());
        var window = new MainWindow { DataContext = main };
        window.Show();
        main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Database");
        Settle(window);
        T? Find<T>(string id) where T : Control =>
            window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);

        Assert.NotNull(Find<TextBox>("SqlitePathBox"));
        Assert.Null(Find<TextBox>("ConnectionStringBox"));
        Assert.NotNull(Find<Button>("TestConnectionButton"));

        Find<ListBox>("ProviderSelector")!.SelectedItem = Choice(DatabaseProviders.SqlServer);
        Settle(window);
        Assert.Null(Find<TextBox>("SqlitePathBox"));
        var box = Find<TextBox>("ConnectionStringBox")!;
        Assert.Contains("Integrated Security=True", box.PlaceholderText);
        box.Text = "Server=s;Password=p";
        Settle(window);
        Assert.Equal("sqlserver", main.Current.Database.Provider);
        Assert.NotNull(Find<TextBlock>("PasswordWarningText"));
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        Dispatcher.UIThread.RunJobs();
    }
}
