using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>
/// Regression for the Fields tab bug: in a window that is not maximized the star-width Field and Caption columns collapsed
/// to a few pixels. Each tab is opened at several window widths with a long field name; the name columns must stay readable
/// and the grid must offer a horizontal scrollbar when its columns are wider than the viewport.
/// </summary>
public class GridLayoutTests
{
    private const string LongName = "customer_billing_address";

    public static IEnumerable<object[]> Widths() => new[] { 900, 1000, 1100, 1400 }.Select(w => new object[] { w });

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void FieldsTabKeepsFieldAndCaptionReadable(int width)
    {
        var window = Open(width, "Fields", out _);
        var grid = Find<DataGrid>(window, "FieldList");
        AssertReadable(grid, width, "Field", "Caption");
        AssertScrollsWhenWider(grid);
        // The pane must not be squeezed below a usable size by the splitter either.
        Assert.True(grid.Bounds.Width >= 300, $"Field list is only {grid.Bounds.Width}px wide.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void TablesTabKeepsTableAndCaptionReadable(int width)
    {
        var window = Open(width, "Tables", out _);
        var grid = Find<DataGrid>(window, "TablesGrid");
        AssertReadable(grid, width, "Table", "Caption");
        AssertScrollsWhenWider(grid);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void RelationsTabKeepsKeyFieldColumnsReadable(int width)
    {
        var window = Open(width, "Relations", out var main);
        main.Relations.AddLinkCommand.Execute(null);
        Settle(window);
        ExpandAll(window);
        var grid = Find<DataGrid>(window, "KeyPairsGrid");
        AssertReadable(grid, width, "Master field", "Detail field");
        AssertScrollsWhenWider(grid);
        AssertReadable(Find<DataGrid>(window, "DetailColumnsGrid"), width, "Field");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void PagesTabKeepsNameAndFieldColumnsReadable(int width)
    {
        var window = Open(width, "Pages", out var main);
        ExpandAll(window);
        Find<Button>(window, "AddFormPageButton").Command!.Execute(null);
        Settle(window);
        ExpandAll(window);
        AssertReadable(Find<DataGrid>(window, "FormPagesGrid"), width, "Name", "Caption");
        AssertReadable(Find<DataGrid>(window, "FieldPagesGrid"), width, "Field");
        AssertScrollsWhenWider(Find<DataGrid>(window, "FormPagesGrid"));
        Assert.NotNull(main);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void LanguagesTabKeepsKeyAndSourceColumnsReadable(int width)
    {
        var rig = LanguagesTests.Setup();
        var window = new MainWindow { DataContext = rig.Main, Width = width, Height = 800 };
        window.Show();
        Settle(window);
        SelectTab(window, "Languages");
        var grid = Find<DataGrid>(window, "TranslationsGrid");
        AssertReadable(grid, width, "Key", "Source text");
        AssertScrollsWhenWider(grid);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void ReportsTabKeepsItsGridColumnsReadableAndScrollable(int width)
    {
        var (main, _) = ReportsTests.Rig();
        var window = new MainWindow { DataContext = main, Width = width, Height = 800 };
        window.Show();
        Settle(window);
        SelectTab(window, "Reports");

        // (report type, grids and the headers that must stay readable)
        var cases = new (ReportType Type, (string Grid, string[] Headers)[] Grids)[]
        {
            (ReportType.Summary, [("SummaryGroupsGrid", ["Field", "Heading"]), ("SummaryAggregatesGrid", ["Function", "Field", "Caption"]), ("SummaryFiltersGrid", ["Field", "Caption"]), ("SummaryDetailFieldsGrid", ["Field"])]),
            (ReportType.Crosstab, [("CrosstabFiltersGrid", ["Field", "Caption"])]),
            (ReportType.Chart, [("ChartSeriesGrid", ["Function", "Field", "Caption", "Colour", "Kind"]), ("ChartFiltersGrid", ["Field", "Caption"])]),
            (ReportType.Calendar, [("CalendarColorRulesGrid", ["Operator", "Value", "Colour or style"]), ("CalendarPopoverFieldsGrid", ["Field"]), ("CalendarFiltersGrid", ["Field", "Caption"])]),
        };
        foreach (var (type, grids) in cases)
        {
            main.Reports.AddReport(type);
            Settle(window);
            ExpandAll(window);
            foreach (var (name, headers) in grids)
            {
                var grid = Find<DataGrid>(window, name);
                AssertReadable(grid, width, headers);
                AssertScrollsWhenWider(grid);
            }
        }

        // The preview result grid: every column has a minimum width and the grid scrolls sideways.
        var preview = new ReportsTests.FakePreview
        {
            Handler = (_, _) => Task.FromResult(new ReportPreviewResult(
                Enumerable.Range(0, 12).Select(i => "Column" + i).ToArray(),
                [Enumerable.Range(0, 12).Select(i => (object?)("value " + i)).ToArray()],
                null)),
        };
        main.ReportPreview = preview;
        main.Reports.RunPreviewAsync().GetAwaiter().GetResult();
        Settle(window);
        var result = Find<DataGrid>(window, "PreviewGrid");
        Assert.Equal(12, result.Columns.Count);
        AssertReadable(result, width, "Column0", "Column11");
        AssertScrollsWhenWider(result);
    }

    [AvaloniaFact]
    public void FieldsTabSavesScreenshotAtANormalWindowSize()
    {
        var window = Open(1000, "Fields", out var main);
        main.Tables.SelectedField = main.Current.Tables[0].Fields.Single(f => f.Name == LongName);
        Settle(window);
        var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SourceFile())!, "..", "artifacts"));
        System.IO.Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, "fields-tab-1000.png"));
#pragma warning disable CS0618
        frame!.Save(stream);
#pragma warning restore CS0618
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string? file = null) => file!;

    private static Window Open(int width, string tab, out MainViewModel main)
    {
        (main, _) = CustomizationTests.Project();
        var customers = main.Current.Tables[0];
        customers.Fields.Add(new FieldInfo { Name = LongName, Type = FieldType.Text, Caption = "Customer billing address (invoice)" });
        main.Tables.Reload();
        var window = new MainWindow { DataContext = main, Width = width, Height = 800 };
        window.Show();
        Settle(window);
        SelectTab(window, tab);
        main.Tables.SelectedTable = main.Current.Tables[0];
        Settle(window);
        return window;
    }

    private static void AssertReadable(DataGrid grid, int width, params string[] headers)
    {
        var min = width <= 900 ? 80 : 100;
        foreach (var header in headers)
        {
            var column = grid.Columns.Single(c => c.Header?.ToString() == header);
            Assert.True(column.ActualWidth >= min, $"{grid.Name}: column '{header}' is {column.ActualWidth}px wide at window width {width} (need {min}).");
        }
    }

    private static void AssertScrollsWhenWider(DataGrid grid)
    {
        var total = grid.Columns.Where(c => c.IsVisible).Sum(c => c.ActualWidth);
        var bar = grid.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Name == "PART_HorizontalScrollbar");
        if (total > grid.Bounds.Width)
        {
            Assert.True(bar.IsVisible && bar.Maximum > 0, $"{grid.Name}: columns are {total}px in a {grid.Bounds.Width}px grid but there is no horizontal scrollbar.");
        }
    }

    private static T Find<T>(Window window, string id) where T : Control
        => window.GetVisualDescendants().OfType<T>().First(c => AutomationProperties.GetAutomationId(c) == id);

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void SelectTab(Window window, string name)
    {
        window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = MainViewModel.TabNames.ToList().IndexOf(name);
        Settle(window);
    }

    private static void ExpandAll(Window window)
    {
        foreach (var expander in window.GetVisualDescendants().OfType<Expander>().Where(e => e.IsEffectivelyVisible))
        {
            expander.IsExpanded = true;
        }

        Settle(window);
    }
}
