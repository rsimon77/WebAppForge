using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>The Import/Export tab: view model edits reach the project, and the real tab shows the controls and the table grid.</summary>
public class ImportExportViewTests
{
    [Fact]
    public void TheTabSitsAfterTheAuditTrailAndEditsWriteIntoTheProject()
    {
        var names = MainViewModel.TabNames.ToList();
        Assert.Equal(names.IndexOf("Audit trail") + 1, names.IndexOf("Import/Export"));

        var (main, _) = CustomizationTests.Project();
        var vm = main.ImportExport;
        vm.Refresh();
        Assert.Equal(["Customers", "Orders"], vm.Rows.Select(r => r.Name).ToArray());

        vm.Enabled = true;
        vm.Formats.Single(f => f.Format == DataFormats.Word).Export = true;
        vm.Formats.Single(f => f.Format == DataFormats.Json).Import = false;
        vm.MaxUploadText = "25";
        vm.ImportMinLevelText = "3";
        vm.HeaderStyle = HeaderStyle.FieldName;
        var row = vm.Rows.Single(r => r.Name == "Customers");
        row.Import = false;
        row.ExportMaxRowsText = "500";
        row.ExcludedText = "Notes";

        var settings = main.Current.ImportExport;
        Assert.True(settings.Enabled);
        Assert.True(settings.ExportFormats.HasFlag(DataFormats.Word));
        Assert.False(settings.ImportFormats.HasFlag(DataFormats.Json));
        Assert.Equal(25, settings.MaxUploadMb);
        Assert.Equal(3, settings.ImportMinLevel);
        Assert.Equal(HeaderStyle.FieldName, settings.HeaderStyle);
        var table = main.Current.Tables.Single(t => t.Name == "Customers").ImportExport;
        Assert.False(table.Import);
        Assert.Equal(500, table.ExportMaxRows);
        Assert.Equal(["Notes"], table.ExcludedFields);
        Assert.Null(vm.ValidationMessage);

        // A rejected number keeps the last valid value; a bad separator or excluded field blocks generating.
        vm.MaxUploadText = "500";
        Assert.Equal(25, settings.MaxUploadMb);
        Assert.NotNull(vm.InputMessage);
        vm.CsvDelimiter = "ab";
        Assert.Contains("CSV separator", vm.ValidationMessage);
        Assert.Equal(vm.ValidationMessage, main.Security.ValidationMessage);
        vm.CsvDelimiter = ";";
        row.ExcludedText = "Missing";
        Assert.Contains("does not exist", vm.ValidationMessage);
    }

    [AvaloniaFact]
    public void TheTabShowsItsControlsAndOneGridRowPerTable()
    {
        var (main, _) = CustomizationTests.Project();
        main.ImportExport.Refresh();
        var window = new MainWindow { DataContext = main };
        window.Show();
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = MainViewModel.TabNames.ToList().IndexOf("Import/Export");
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>()
            .First(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == id);

        Assert.False(Find<TextBox>("ImportExportDelimiterBox").IsEffectivelyEnabled);
        Find<CheckBox>("ImportExportEnabledCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Find<TextBox>("ImportExportDelimiterBox").IsEffectivelyEnabled);
        Assert.True(main.Current.ImportExport.Enabled);
        Assert.Equal(5, Find<ItemsControl>("ImportExportFormats").ItemCount);
        Assert.Equal(2, Find<DataGrid>("ImportExportTablesGrid").ItemsSource!.Cast<object>().Count());
    }
}
