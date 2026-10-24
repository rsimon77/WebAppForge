using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>
/// Drives the real MainWindow on the headless platform. Controls are found in the visual tree (no x:Name needed);
/// the view model is used for state the grid does not expose. Tabs: 0 Database, 1 Tables, 2 Fields, 3 ASP.NET, 4 Security, 5 Generate.
/// </summary>
public class HeadlessUiTests
{
    private const string LevelError = "View level must be a whole number, 0 or more. The last valid value was kept.";

    /// <summary>Users (UserName, PasswordHash text; Level integer) and Logins (UserName, PasswordHash text only). Both generated.</summary>
    private static MainViewModel ProjectWithLogins()
    {
        var main = new MainViewModel(new FakeDialogService());

        var users = new TableInfo { Name = "Users", Generate = true };
        users.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
        users.Fields.Add(new FieldInfo { Name = "Level", Type = FieldType.Integer });
        main.Current.Tables.Add(users);

        var logins = new TableInfo { Name = "Logins", Generate = true };
        logins.Fields.Add(new FieldInfo { Name = "UserName", Type = FieldType.Text });
        logins.Fields.Add(new FieldInfo { Name = "PasswordHash", Type = FieldType.Text });
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
        // Rows of a DataGrid are realized over a few passes, so the layout is run more than once.
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

    /// <summary>The ComboBoxes of the Security tab in XAML order: user table, user name, password, level.</summary>
    private static ComboBox[] SecurityCombos(Window window)
    {
        var view = window.GetVisualDescendants().OfType<SecurityView>().Single();
        // The user store picker comes first; these tests use the project-table pickers after it.
        return view.GetVisualDescendants().OfType<ComboBox>().Where(c => AutomationProperties.GetAutomationId(c) != "UserStoreComboBox").ToArray();
    }

    private static DataGrid TablesGrid(Window window) => window.GetVisualDescendants().OfType<DataGrid>().Single();

    /// <summary>The owner picker of the Tables inspector, which shows the selected table.</summary>
    private static ComboBox OwnerCombo(Window window, TableInfo table)
        => window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => AutomationProperties.GetAutomationId(c) == "TableOwnerFieldCombo" && c.DataContext == table);

    /// <summary>The View level box of the Tables inspector, which shows the selected table.</summary>
    private static TextBox ViewLevelBox(Window window, TableInfo table)
        => window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => AutomationProperties.GetAutomationId(t) == "TableViewLevelBox" && t.DataContext == table);

    private static void EnableLoginThroughControls(Window window)
    {
        window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => (c.Content as string)?.StartsWith("Require sign-in", StringComparison.Ordinal) == true)
            .IsChecked = true;
        Settle(window);

        var combos = SecurityCombos(window);
        combos[0].SelectedItem = "Users";
        Settle(window);
        combos = SecurityCombos(window);
        combos[1].SelectedItem = "UserName";
        combos[2].SelectedItem = "PasswordHash";
        Settle(window);
    }

    [AvaloniaFact]
    public void SecurityTab_InvalidLevelChoiceShowsValidationText()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Security"));

        EnableLoginThroughControls(window);
        Assert.Null(main.Security.ValidationMessage);

        // The level picker lists "(none)" then the Integer fields of the chosen table; pick Level through the control.
        var levelCombo = SecurityCombos(window)[3];
        Assert.Equal(new[] { "(none)", "Level" }, levelCombo.ItemsSource!.Cast<object>().Select(o => o.ToString()));
        levelCombo.SelectedItem = "Level";
        Settle(window);
        Assert.Equal("Level", main.Current.Security.UserLevelField);
        Assert.Null(main.Security.ValidationMessage);

        // A level the picker does not offer (a Text field) is an invalid choice. It is set on the view model, because
        // the picker's own list cannot produce it; the validation text must then appear on the Security tab.
        main.Security.LevelField = "UserName";
        Settle(window);

        const string expected = "The user level field 'UserName' in table 'Users' must be an integer column, but it is Text.";
        Assert.Equal(expected, main.Security.ValidationMessage);
        Assert.True(HasVisibleText(window, expected), "The validation text is not visible in the Security tab.");
    }

    [AvaloniaFact]
    public void TablesTab_TypingAbcShowsStatusLineAndKeepsLevel()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Tables"));
        var users = main.Current.Tables[0];
        var box = ViewLevelBox(window, users);

        window.Activate();
        Assert.True(box.Focus(), "The View level box could not take focus.");
        window.KeyTextInput("abc");
        Settle(window);

        Assert.Equal(0, users.ViewMinLevel);
        Assert.Equal(LevelError, main.Status);
        Assert.True(HasVisibleText(window, LevelError), "The status line does not show the message.");
    }

    [AvaloniaFact]
    public void TablesTab_TypingTwoSetsViewMinLevelToTwo()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Tables"));
        var users = main.Current.Tables[0];
        var box = ViewLevelBox(window, users);

        window.Activate();
        Assert.True(box.Focus(), "The View level box could not take focus.");
        box.Text = string.Empty;
        window.KeyTextInput("2");
        Settle(window);

        Assert.Equal(2, users.ViewMinLevel);
    }

    [AvaloniaFact]
    public void TablesTab_OwnerFieldListsNoneAndTextFieldsOnly()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Tables"));
        var users = main.Current.Tables[0];

        var combo = OwnerCombo(window, users);
        var items = ((IEnumerable)combo.ItemsSource!).Cast<object>().Select(o => o.ToString()).ToList();
        Assert.Equal(new[] { "(none)", "UserName", "PasswordHash" }, items);

        combo.SelectedItem = "PasswordHash";
        Settle(window);
        Assert.Equal("PasswordHash", users.OwnerField);
    }

    [AvaloniaFact]
    public void GenerateIsBlockedWhileOwnerFieldIsInvalid()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        var users = main.Current.Tables[0];

        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Security"));
        EnableLoginThroughControls(window);
        Assert.True(main.Security.CanGenerate);

        // The owner picker never offers Integer fields, so the invalid owner is set on the model directly.
        main.Tables.SetOwnerField(users, "Level");
        Settle(window);

        Assert.False(main.Security.CanGenerate);
        Assert.Contains("must be a text column", main.Security.ValidationMessage);

        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Generate"));
        var generate = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Generate");
        Assert.False(generate.IsEnabled);
    }

    [AvaloniaFact]
    public void SecurityTab_SavesScreenshot()
    {
        var main = ProjectWithLogins();
        var window = ShowWindow(main);
        SelectTab(window, MainViewModel.TabNames.ToList().IndexOf("Security"));
        EnableLoginThroughControls(window);
        Settle(window);

        var folder = Path.Combine(TestsFolder(), "artifacts");
        Directory.CreateDirectory(folder);
        var path = Path.GetFullPath(Path.Combine(folder, "security-tab.png"));

        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using (var stream = File.Create(path))
        {
#pragma warning disable CS0618 // The BitmapEncoderOptions overload is not available for streams in this Avalonia version.
            frame!.Save(stream);
#pragma warning restore CS0618
        }
        Assert.True(new FileInfo(path).Length > 0);
    }

    /// <summary>The Tests project folder, found from this source file's location at compile time.</summary>
    private static string TestsFolder([CallerFilePath] string? sourceFile = null)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, ".."));
}
