using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>The Audit trail tab: view model edits reach the project, and the real tab shows the controls and the table grid.</summary>
public class AuditViewTests
{
    [Fact]
    public void TheAuditTabSitsAfterSecurityAndEditsWriteIntoTheProject()
    {
        var names = MainViewModel.TabNames.ToList();
        Assert.Equal(names.IndexOf("Security") + 1, names.IndexOf("Audit trail"));

        var (main, _) = CustomizationTests.Project();
        main.Audit.Refresh();
        Assert.Equal(["Customers", "Orders"], main.Audit.Rows.Select(r => r.Name).ToArray());

        main.Audit.Enabled = true;
        main.Audit.Values = AuditValues.All;
        main.Audit.LogPageMinLevelText = "2";
        var row = main.Audit.Rows.Single(r => r.Name == "Customers");
        row.Enabled = true;
        row.Deletes = false;
        row.ExcludedText = "Notes, Email";

        var audit = main.Current.Audit;
        Assert.True(audit.Enabled);
        Assert.Equal(AuditValues.All, audit.Values);
        Assert.Equal(2, audit.LogPageMinLevel);
        var table = main.Current.Tables.Single(t => t.Name == "Customers").Audit;
        Assert.True(table.Enabled);
        Assert.False(table.Deletes);
        Assert.Equal(["Notes", "Email"], table.ExcludedFields);
        Assert.Null(main.Audit.ValidationMessage);

        // A field the table does not have blocks generating, and an empty log table name does too.
        row.ExcludedText = "Missing";
        Assert.Contains("no such field", main.Audit.ValidationMessage);
        Assert.Equal(main.Audit.ValidationMessage, main.Security.ValidationMessage);
        row.ExcludedText = "Notes";
        main.Audit.LogTable = " ";
        Assert.Contains("name of its log table", main.Audit.ValidationMessage);
    }

    [AvaloniaFact]
    public void TheAuditTabShowsItsControlsAndOneGridRowPerTable()
    {
        var (main, _) = CustomizationTests.Project();
        main.Audit.Refresh();
        var window = new MainWindow { DataContext = main };
        window.Show();
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = MainViewModel.TabNames.ToList().IndexOf("Audit trail");
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>()
            .First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id);

        Assert.False(Find<TextBox>("AuditLogTableBox").IsEffectivelyEnabled);
        Find<CheckBox>("AuditEnabledCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Find<TextBox>("AuditLogTableBox").IsEffectivelyEnabled);
        Assert.True(main.Current.Audit.Enabled);
        Assert.True(Find<ComboBox>("AuditValuesPicker").IsEffectivelyVisible);
        Assert.Equal(2, Find<DataGrid>("AuditTablesGrid").ItemsSource!.Cast<object>().Count());
    }
}
