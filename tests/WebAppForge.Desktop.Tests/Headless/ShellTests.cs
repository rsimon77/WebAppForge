using System;
using System.Collections;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>Shell features on the real MainWindow: menu-era commands, the breadcrumb, the idle notice, row colours, page size and styles.</summary>
public class ShellTests
{
    private const string IdleNotice = "Session idle. Save now?";

    [AvaloniaFact]
    public void PageSizeBoxWritesThePageSizeOfTheSelectedTable()
    {
        var main = ProjectWithTables();
        var window = ShowWindow(main);
        SelectTab(window, 1);
        var users = main.Current.Tables[0];
        main.Tables.SelectedTable = users;
        Settle(window);

        var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PageSizeCombo");
        var choices = ((IEnumerable)combo.ItemsSource!).Cast<object>().Select(o => (int)o).ToArray();
        Assert.Equal(new[] { 10, 20, 50, 100 }, choices);

        combo.SelectedItem = 50;
        Settle(window);

        Assert.Equal(50, users.Pages.PageSize);
    }

    [AvaloniaFact]
    public void BreadcrumbHeaderUpdatesWhenTheTabOrTheRowChanges()
    {
        var main = ProjectWithTables();
        var window = ShowWindow(main);
        Assert.True(HasVisibleText(window, "Project1 > Database"));

        SelectTab(window, 1);
        Assert.True(HasVisibleText(window, "Project1 > Tables > Users"));

        var logins = main.Current.Tables[1];
        TablesGrid(window).SelectedItem = logins;
        Settle(window);

        Assert.True(HasVisibleText(window, "Project1 > Tables > Logins"), "The header did not follow the selected row.");
    }

    [AvaloniaFact]
    public void IdleNoticeShowsAfterTimeoutAndMouseInputHidesIt()
    {
        var main = ProjectWithTables();
        var window = ShowWindow(main);
        Assert.False(HasVisibleText(window, IdleNotice));

        main.CheckSession(DateTime.UtcNow.AddMinutes(31));
        Settle(window);
        Assert.True(HasVisibleText(window, IdleNotice), "The idle notice is not visible in the status bar.");

        window.MouseMove(new Point(40, 40));
        Settle(window);
        Assert.False(HasVisibleText(window, IdleNotice), "Input did not reset the session timer.");
    }

    [AvaloniaFact]
    public void TableRowsUseMutedAndWarningClassesFromTheRule()
    {
        var main = ProjectWithTables();
        var audit = new TableInfo { Name = "Audit", Generate = false };
        audit.Fields.Add(new FieldInfo { Name = "Id", IsPrimaryKey = true });
        main.Current.Tables.Add(audit);
        main.Tables.Reload();

        var window = ShowWindow(main);
        SelectTab(window, 1);

        var rows = TablesGrid(window).GetVisualDescendants().OfType<DataGridRow>().ToList();
        var auditRow = rows.Single(r => r.DataContext == audit);
        var usersRow = rows.Single(r => r.DataContext == main.Current.Tables[0]);

        Assert.Contains("row-muted", auditRow.Classes);
        Assert.DoesNotContain("row-warning", auditRow.Classes);
        Assert.Contains("row-warning", usersRow.Classes); // A generated table without a key is warned.
        Assert.DoesNotContain("row-muted", usersRow.Classes);
    }

    [AvaloniaFact]
    public void AccentPaletteAndDarkModeAreAppliedAndCanBeRestored()
    {
        var app = Application.Current!;
        var appearance = new AvaloniaAppearance(app);
        try
        {
            Assert.Null(appearance.Apply(new AppSettings { ThemeName = "Red (danger)", Appearance = AppearanceMode.Dark }));

            Assert.Equal(Color.Parse("#DC3545"), AccentColor(app));
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
        }
        finally
        {
            appearance.Apply(new AppSettings());
        }

        Assert.Equal(ThemePalettes.Default.Color, AccentColor(app));
        Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void ValidCustomStylesFileOverridesAResourceAndIsRemovedAgain()
    {
        using var folder = new TempFolder();
        var path = folder.File("brand.axaml");
        File.WriteAllText(path, """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="AppAccentBrush">#00FF00</SolidColorBrush>
            </ResourceDictionary>
            """);
        var app = Application.Current!;
        var appearance = new AvaloniaAppearance(app);
        try
        {
            Assert.Null(appearance.Apply(new AppSettings { CustomStylesPath = path }));
            Assert.True(app.TryFindResource("AppAccentBrush", ThemeVariant.Default, out var brush));
            Assert.Equal(Color.Parse("#00FF00"), ((ISolidColorBrush)brush!).Color);
        }
        finally
        {
            appearance.Apply(new AppSettings());
        }

        Assert.Equal(ThemePalettes.Default.Color, AccentColor(app));
    }

    [AvaloniaFact]
    public void InvalidCustomStylesFileReportsWhyItWasNotLoaded()
    {
        using var folder = new TempFolder();
        var path = folder.File("broken.axaml");
        File.WriteAllText(path, """<Window xmlns="https://github.com/avaloniaui" />""");
        var appearance = new AvaloniaAppearance(Application.Current!);
        try
        {
            var message = appearance.Apply(new AppSettings { CustomStylesPath = path });

            Assert.NotNull(message);
            Assert.Contains("Custom styles were not loaded from broken.axaml", message);
            Assert.Contains("ResourceDictionary", message);
        }
        finally
        {
            appearance.Apply(new AppSettings());
        }
    }

    [AvaloniaFact]
    public void CustomStylesWithCodeBehindAreRefused()
    {
        using var folder = new TempFolder();
        var path = folder.File("code.axaml");
        File.WriteAllText(path, """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                x:Class="Evil.Code" />
            """);

        var ex = Assert.Throws<System.IO.InvalidDataException>(() => CustomStyleLoader.Load(path));
        Assert.Contains("x:Class", ex.Message);
    }

    [AvaloniaFact]
    public void MissingCustomStylesFileIsIgnoredQuietly()
    {
        using var folder = new TempFolder();
        var appearance = new AvaloniaAppearance(Application.Current!);
        try
        {
            Assert.Null(appearance.Apply(new AppSettings { CustomStylesPath = folder.File("gone.axaml") }));
        }
        finally
        {
            appearance.Apply(new AppSettings());
        }
    }

    private static Color AccentColor(Application app)
    {
        Assert.True(app.TryFindResource(AvaloniaAppearance.AccentBrushKey, ThemeVariant.Default, out var brush));
        return ((ISolidColorBrush)brush!).Color;
    }

    private static MainViewModel ProjectWithTables()
    {
        var main = new MainViewModel(new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        main.Current.Tables.Add(users);

        var logins = new TableInfo { Name = "Logins", Generate = true };
        logins.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        main.Current.Tables.Add(logins);

        main.Tables.Reload();
        return main;
    }

    private static Window ShowWindow(MainViewModel main)
    {
        var window = new MainWindow { DataContext = main };
        window.Show();
        Settle(window);
        return window;
    }

    /// <summary>Runs queued jobs, a render tick (which lays out and draws the window) and jobs again.</summary>
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

    private static void SelectTab(Window window, int index)
    {
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = index;
        Settle(window);
    }

    private static bool HasVisibleText(Window window, string text)
        => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible);

    private static DataGrid TablesGrid(Window window) => window.GetVisualDescendants().OfType<DataGrid>().Single();
}
