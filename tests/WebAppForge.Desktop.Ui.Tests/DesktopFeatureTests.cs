using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Xunit;

namespace WebAppForge.Desktop.Ui.Tests;

/// <summary>
/// Live checks of the newer desktop UI features: Tables page size, Tools > Options, Security, the header breadcrumb and
/// the unsaved-changes prompt. Each test launches its own copy of the app against a copy of Northwind and closes it.
/// </summary>
[Trait("Platform", "Windows")]
[Collection(DesktopSession.CollectionName)]
public class DesktopFeatureTests
{
    private const string ProjectName = "Project1";
    private const string EditedProjectName = "Project1 edited";
    private const string SecuritySignInLabel = "Require sign-in (cookie login against a generated table)";
    private const string UnsavedDialogTitle = "Unsaved changes";
    private static readonly string[] ExpectedPageSizes = ["10", "20", "50", "100"];

    [DesktopUiFact]
    public void Tables_PageSizeCombo_ListsTenTwentyFiftyAndOneHundred()
    {
        using var session = DesktopSession.Launch();
        session.ConnectToNorthwind();
        session.SelectTab("Tables");

        // The page-size combo is enabled only while a table is selected, so select the first one ("Categories" in Northwind).
        var table = session.Require(
            session.Window.FindFirstDescendant(cf => cf.ByName("Categories")),
            "The 'Categories' table row was not found on the Tables tab.");
        table.Click();

        var combo = FindPageSizeCombo(session).AsComboBox();
        combo.Expand();
        var items = combo.Items.Select(i => i.Name).ToArray();
        combo.Collapse();

        var sizes = items.Where(i => ExpectedPageSizes.Contains(i)).ToArray();
        if (!sizes.SequenceEqual(ExpectedPageSizes))
        {
            session.Fail($"The page-size combo listed [{string.Join(", ", items)}]; expected 10, 20, 50 and 100.");
        }
    }

    [DesktopUiFact]
    public void Tools_Options_DarkAppearanceIsSavedAndRestoredToSystem()
    {
        var settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebAppForge", "settings.json");
        var hadFile = File.Exists(settingsFile);
        var original = hadFile ? File.ReadAllBytes(settingsFile) : null;
        try
        {
            using (var session = DesktopSession.Launch())
            {
                session.Require(session.Find(ControlType.MenuItem, "Tools"), "The Tools menu was not found.").Click();
                session.Require(session.WaitFor(ControlType.MenuItem, "Options..."), "The Tools > Options... menu item was not found.").Click();

                var options = session.FindTopLevelWindow("Options", TimeSpan.FromSeconds(10));
                Assert.True(options is not null, $"The Options window did not open.\n{DesktopSession.DumpTree(session.Window)}");

                var categories = options!.FindFirstDescendant(cf => cf.ByAutomationId("CategoryList"));
                Assert.True(categories is not null, $"The category list was not found in the Options window.\n{DesktopSession.DumpTree(options!)}");
                var appearanceCategory = categories!.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName("Appearance")));
                Assert.True(appearanceCategory is not null, $"The Appearance category was not found.\n{DesktopSession.DumpTree(options!)}");
                appearanceCategory!.Click();

                var appearance = Retry.WhileNull(
                    () => options.FindFirstDescendant(cf => cf.ByAutomationId("AppearanceCombo")), TimeSpan.FromSeconds(10)).Result;
                Assert.True(appearance is not null, $"The Appearance mode selector (Light, Dark, System) was not found in the Options window.\n{DesktopSession.DumpTree(options!)}");
                var combo = appearance!.AsComboBox();
                combo.Expand();
                combo.Items.First(i => i.Name == "Dark").Select();

                var ok = options.FindFirstDescendant(cf => cf.ByAutomationId("OkButton"));
                Assert.True(ok is not null, $"The OK button was not found in the Options window.\n{DesktopSession.DumpTree(options!)}");
                ok!.AsButton().Invoke();

                // The window background is not exposed through UI Automation, so the saved setting is the observable result.
                var saved = Retry.WhileFalse(() => SettingsContain(settingsFile, "\"Dark\""), TimeSpan.FromSeconds(10)).Result;
                Assert.True(saved, $"{settingsFile} does not contain \"Dark\" after OK.\n{DesktopSession.DumpTree(session.Window)}");
            }
        }
        finally
        {
            // The app is closed here. Put the settings file back as it was (System when no file existed, the default).
            if (hadFile)
            {
                File.WriteAllBytes(settingsFile, original!);
            }
            else if (File.Exists(settingsFile))
            {
                File.Delete(settingsFile);
            }
        }
    }

    [DesktopUiFact]
    public void Security_SignInCheckbox_ShowsExpandersAfterEnabling()
    {
        using var session = DesktopSession.Launch();
        session.ConnectToNorthwind();
        session.SelectTab("Security");

        var enabled = session.Require(
            session.Window.FindFirstDescendant(cf => cf.ByName(SecuritySignInLabel)),
            $"The '{SecuritySignInLabel}' checkbox was not found on the Security tab.");
        var checkBox = enabled.AsCheckBox();
        if (!checkBox.IsChecked.GetValueOrDefault())
        {
            checkBox.IsChecked = true;
        }

        foreach (var header in new[] { "Login options", "Password options", "Permissions" })
        {
            var expander = Retry.WhileNull(
                () => session.Window.FindFirstDescendant(cf => cf.ByName(header)),
                TimeSpan.FromSeconds(10)).Result;
            if (expander is null)
            {
                session.Fail($"The '{header}' expander was not found on the Security tab after enabling sign-in.");
            }
        }
    }

    [DesktopUiFact]
    public void Header_Breadcrumb_ShowsProjectNameAfterConnect()
    {
        using var session = DesktopSession.Launch();
        session.ConnectToNorthwind();

        var projectBox = session.Require(FindEditWithText(session, ProjectName), $"The project name box does not hold '{ProjectName}'.");
        var expected = $"{projectBox.AsTextBox().Text} > Database";

        var breadcrumb = Retry.WhileNull(
            () => session.Window.FindFirstDescendant(cf => cf.ByName(expected)),
            TimeSpan.FromSeconds(10)).Result;
        if (breadcrumb is null)
        {
            session.Fail($"The header breadcrumb '{expected}' was not found.");
        }
    }

    [DesktopUiFact]
    public void File_New_WithUnsavedChanges_ShowsDialogAndCancelKeepsProject()
    {
        using var session = DesktopSession.Launch();
        session.ConnectToNorthwind();

        // Make the project dirty by renaming it; the project name box is the edit that holds "Project1".
        session.Require(FindEditWithText(session, ProjectName), "The project name box was not found.")
            .AsTextBox().Enter(EditedProjectName);

        session.Require(session.Find(ControlType.Button, "New"), "The New button was not found.").AsButton().Invoke();

        var dialog = session.FindTopLevelWindow(UnsavedDialogTitle, TimeSpan.FromSeconds(10));
        Assert.True(dialog is not null, $"The unsaved-changes dialog did not appear.\n{DesktopSession.DumpTree(session.Window)}");

        foreach (var label in new[] { "Save", "Discard", "Cancel" })
        {
            var button = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(label)));
            Assert.True(button is not null, $"The '{label}' button was not found in the unsaved-changes dialog.\n{DesktopSession.DumpTree(dialog!)}");
        }

        var cancel = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")));
        Assert.True(cancel is not null, $"The Cancel button disappeared from the dialog.\n{DesktopSession.DumpTree(dialog)}");
        cancel!.AsButton().Invoke();

        var closed = Retry.WhileFalse(() => !session.IsWindowOpen(UnsavedDialogTitle), TimeSpan.FromSeconds(10)).Result;
        Assert.True(closed, $"The unsaved-changes dialog did not close after Cancel.\n{session.DescribeWindows()}");

        // Cancel keeps the project: the edited name is still there and the status line is unchanged.
        Assert.True(
            FindEditWithText(session, EditedProjectName) is not null,
            $"The project name was changed or reset after Cancel; expected '{EditedProjectName}'.\n{DesktopSession.DumpTree(session.Window)}");
        Assert.True(
            Retry.WhileNull(() => session.Window.FindFirstDescendant(cf => cf.ByName(DesktopSession.ExpectedStatus)), TimeSpan.FromSeconds(5)).Result is not null,
            $"The status line changed after Cancel.\n{DesktopSession.DumpTree(session.Window)}");
    }

    /// <summary>
    /// The page-size combo. Preference order: AutomationId, the x:Name (PageSizeCombo) exposed as the UIA name, then the combo
    /// beside the "Page size for the selected table" label. The first two are not set in the views, so the label is the fallback.
    /// </summary>
    private static AutomationElement FindPageSizeCombo(DesktopSession session)
    {
        var byId = session.Window.FindFirstDescendant(cf => cf.ByAutomationId("PageSizeCombo"));
        if (byId is not null)
        {
            Console.WriteLine("PageSizeCombo located by AutomationId.");
            return byId;
        }

        var byName = session.Find(ControlType.ComboBox, "PageSizeCombo");
        if (byName is not null)
        {
            Console.WriteLine("PageSizeCombo located by UIA name (x:Name).");
            return byName;
        }

        var label = session.Window.FindFirstDescendant(cf => cf.ByName("Page size for the selected table"));
        var beside = label?.Parent?.FindFirstChild(cf => cf.ByControlType(ControlType.ComboBox));
        Console.WriteLine("PageSizeCombo located beside its label (no AutomationId or name exposed).");
        return session.Require(beside, "The page-size combo was not found (no AutomationId, no name, and no combo beside its label).");
    }

    private static AutomationElement? FindEditWithText(DesktopSession session, string text)
        => session.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
            .FirstOrDefault(e => DesktopSession.Property(() => e.AsTextBox().Text) == text);

    private static bool SettingsContain(string path, string text)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Contains(text, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            // The app may still be writing the file.
            return false;
        }
    }
}
