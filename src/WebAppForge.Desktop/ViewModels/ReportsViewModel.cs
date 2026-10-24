using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One entry of the report list: "Sales (Summary of Orders)".</summary>
public sealed class ReportRow : ObservableObject
{
    public ReportRow(ReportInfo report) => Report = report;

    public ReportInfo Report { get; }

    public string Text
    {
        get
        {
            var name = Report.Name.Trim().Length == 0 ? "(no name)" : Report.Name;
            var source = Report.Kind == ReportKind.Dashboard ? $"{Report.Items.Count} report(s)" : $"of {(Report.SourceTable.Length == 0 ? "?" : Report.SourceTable)}";
            return $"{name} ({Report.Kind} {source}){(Report.Generate ? string.Empty : " - not generated")}";
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(Text));

    public override string ToString() => Text;
}

/// <summary>Base of the editable rows of the selected report: each property writes through to its model and tells the owner.</summary>
public abstract class ReportPartRow : ObservableObject
{
    private readonly Action _changed;

    protected ReportPartRow(Action changed) => _changed = changed;

    /// <summary>Stores a value when it differs. Callers ignore null first: a combo box reports null while its item list is rebuilt.</summary>
    protected void Set<T>(T current, T value, Action<T> store, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        store(value);
        OnPropertyChanged(name);
        _changed();
    }
}

/// <summary>One group level of a summary report.</summary>
public sealed class ReportGroupRow : ReportPartRow
{
    public ReportGroupRow(ReportGroup model, Action changed) : base(changed) => Model = model;

    public ReportGroup Model { get; }

    public string? Field
    {
        get => Model.Field;
        set
        {
            if (value is not null)
            {
                Set(Model.Field, value, v => Model.Field = v);
            }
        }
    }

    public RelationChoice<DateInterval>? Interval
    {
        get => ReportsViewModel.Intervals.First(c => c.Value == Model.Interval);
        set
        {
            if (value is not null)
            {
                Set(Model.Interval, value.Value, v => Model.Interval = v);
            }
        }
    }

    /// <summary>Raised after the field list was rebuilt, so the picker shows the field again.</summary>
    internal void OnFieldsChanged() => OnPropertyChanged(nameof(Field));

    public bool Descending
    {
        get => Model.Descending;
        set => Set(Model.Descending, value, v => Model.Descending = v);
    }
}

/// <summary>One aggregate column of a summary report.</summary>
public sealed class ReportAggregateRow : ReportPartRow
{
    public ReportAggregateRow(ReportAggregate model, Action changed) : base(changed) => Model = model;

    public ReportAggregate Model { get; }

    /// <summary>The field, or <see cref="ReportsViewModel.RowsLabel"/> for counting rows.</summary>
    public string? Field
    {
        get => Model.Field.Length == 0 ? ReportsViewModel.RowsLabel : Model.Field;
        set
        {
            if (value is not null)
            {
                Set(Model.Field.Length == 0 ? ReportsViewModel.RowsLabel : Model.Field, value, v => Model.Field = v == ReportsViewModel.RowsLabel ? string.Empty : v);
            }
        }
    }

    public RelationChoice<AggregateFunction>? Function
    {
        get => ReportsViewModel.Functions.First(c => c.Value == Model.Function);
        set
        {
            if (value is not null)
            {
                Set(Model.Function, value.Value, v => Model.Function = v);
            }
        }
    }

    internal void OnFieldsChanged() => OnPropertyChanged(nameof(Field));

    public string Caption
    {
        get => Model.Caption;
        set => Set(Model.Caption, value ?? string.Empty, v => Model.Caption = v);
    }
}

/// <summary>One field of a list of the selected report (row columns, filters). The owner writes the list back on every change.</summary>
public sealed class ReportFieldRow : ReportPartRow
{
    private string _field;

    public ReportFieldRow(string field, Action changed) : base(changed) => _field = field;

    public string? Field
    {
        get => _field;
        set
        {
            if (value is not null)
            {
                Set(_field, value, v => _field = v);
            }
        }
    }

    internal void OnFieldsChanged() => OnPropertyChanged(nameof(Field));
}

/// <summary>One report on a dashboard.</summary>
public sealed class DashboardItemRow : ReportPartRow
{
    public DashboardItemRow(DashboardItem model, Action changed) : base(changed) => Model = model;

    public DashboardItem Model { get; }

    public string? Report
    {
        get => Model.Report;
        set
        {
            if (value is not null)
            {
                Set(Model.Report, value, v => Model.Report = v);
            }
        }
    }

    public int Width
    {
        get => Model.Width;
        set => Set(Model.Width, Math.Clamp(value, 1, 12), v => Model.Width = v);
    }

    /// <summary><see cref="Width"/> for the number box, which works with decimals.</summary>
    public decimal? WidthValue
    {
        get => Model.Width;
        set
        {
            if (value is { } width)
            {
                Width = (int)width;
                OnPropertyChanged();
            }
        }
    }
}

/// <summary>
/// The Reports tab: the summary, crosstab, chart, calendar and dashboard pages of the project. Every property writes straight into
/// <see cref="Project.Reports"/>, so Save persists it. The problem list uses <see cref="ReportValidator"/>, which the generator also applies.
/// </summary>
public partial class ReportsViewModel : EditorViewModelBase
{
    /// <summary>The field picker entry that counts rows instead of a field.</summary>
    public const string RowsLabel = "(rows)";

    /// <summary>The optional field picker entry for "no field".</summary>
    public const string NoneLabel = "(none)";

    private readonly MainViewModel _main;
    private ReportRow? _selectedRow;
    private ReportGroupRow? _selectedGroup;
    private ReportAggregateRow? _selectedAggregate;
    private DashboardItemRow? _selectedItem;
    private ReportFieldRow? _selectedDetailColumn;
    private ReportFieldRow? _selectedFilterField;

    public ReportsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
            {
                RefreshLists();
            }
        };
    }

    internal static IReadOnlyList<RelationChoice<ReportKind>> Kinds { get; } =
    [
        new(ReportKind.Summary, "Summary"),
        new(ReportKind.Crosstab, "Crosstab"),
        new(ReportKind.Chart, "Chart"),
        new(ReportKind.Calendar, "Calendar"),
        new(ReportKind.Dashboard, "Dashboard"),
    ];

    internal static IReadOnlyList<RelationChoice<DateInterval>> Intervals { get; } =
    [
        new(DateInterval.None, "Value"),
        new(DateInterval.Year, "Year"),
        new(DateInterval.Quarter, "Quarter"),
        new(DateInterval.Month, "Month"),
        new(DateInterval.Day, "Day"),
    ];

    internal static IReadOnlyList<RelationChoice<AggregateFunction>> Functions { get; } =
    [
        new(AggregateFunction.Count, "Count"),
        new(AggregateFunction.Sum, "Sum"),
        new(AggregateFunction.Average, "Average"),
        new(AggregateFunction.Min, "Min"),
        new(AggregateFunction.Max, "Max"),
    ];

    internal static IReadOnlyList<RelationChoice<ChartType>> ChartTypes { get; } =
    [
        new(WebAppForge.Core.Model.ChartType.Bar, "Bar"),
        new(WebAppForge.Core.Model.ChartType.HorizontalBar, "Horizontal bar"),
        new(WebAppForge.Core.Model.ChartType.Line, "Line"),
        new(WebAppForge.Core.Model.ChartType.Area, "Area"),
        new(WebAppForge.Core.Model.ChartType.Pie, "Pie"),
        new(WebAppForge.Core.Model.ChartType.Doughnut, "Doughnut"),
    ];

    internal static IReadOnlyList<RelationChoice<ChartOrder>> ChartOrders { get; } =
    [
        new(WebAppForge.Core.Model.ChartOrder.Category, "By category"),
        new(WebAppForge.Core.Model.ChartOrder.ValueDescending, "By value, largest first"),
    ];

    public IReadOnlyList<RelationChoice<ReportKind>> KindChoices => Kinds;

    public IReadOnlyList<RelationChoice<DateInterval>> IntervalChoices => Intervals;

    public IReadOnlyList<RelationChoice<AggregateFunction>> FunctionChoices => Functions;

    public IReadOnlyList<RelationChoice<ChartType>> ChartTypeChoices => ChartTypes;

    public IReadOnlyList<RelationChoice<ChartOrder>> ChartOrderChoices => ChartOrders;

    public ObservableCollection<ReportRow> Reports { get; } = [];

    public ObservableCollection<ReportGroupRow> Groups { get; } = [];

    public ObservableCollection<ReportAggregateRow> Aggregates { get; } = [];

    public ObservableCollection<DashboardItemRow> Items { get; } = [];

    /// <summary>Names of the generated tables, for the source table picker.</summary>
    public ObservableCollection<string> TableNames { get; } = [];

    /// <summary>Fields of the source table that a report can use (binary fields left out).</summary>
    public ObservableCollection<string> FieldNames { get; } = [];

    /// <summary><see cref="NoneLabel"/> followed by <see cref="FieldNames"/>, for optional fields.</summary>
    public ObservableCollection<string> OptionalFieldNames { get; } = [];

    /// <summary><see cref="NoneLabel"/> followed by the date fields of the source table (number columns that store dates included).</summary>
    public ObservableCollection<string> DateFieldNames { get; } = [];

    /// <summary><see cref="RowsLabel"/> followed by <see cref="FieldNames"/>, for counted values.</summary>
    public ObservableCollection<string> ValueFieldNames { get; } = [];

    /// <summary>Reports a dashboard can show: every other report that is not a dashboard.</summary>
    public ObservableCollection<string> DashboardChoices { get; } = [];

    /// <summary>The problems of every generated report. The generator refuses a project with any of them.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    public string ProblemSummary => Problems.Count == 0 ? "No problems found." : $"{Problems.Count} problem(s). Generate stops at the first one.";

    public ReportRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
            {
                LoadReport();
                _main.RefreshBreadcrumb();
            }
        }
    }

    public ReportInfo? Report => SelectedRow?.Report;

    public bool HasReport => Report is not null;

    public ReportGroupRow? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public ReportAggregateRow? SelectedAggregate
    {
        get => _selectedAggregate;
        set => SetProperty(ref _selectedAggregate, value);
    }

    public DashboardItemRow? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    // ---- kind panels ----------------------------------------------------------------------------

    public bool IsSummary => Report?.Kind == ReportKind.Summary;

    public bool IsCrosstab => Report?.Kind == ReportKind.Crosstab;

    public bool IsChart => Report?.Kind == ReportKind.Chart;

    /// <summary>Crosstab and chart share the row, column and value settings.</summary>
    public bool UsesGrid => IsCrosstab || IsChart;

    public bool IsCalendar => Report?.Kind == ReportKind.Calendar;

    public bool IsDashboard => Report?.Kind == ReportKind.Dashboard;

    public bool HasSource => Report is not null && !IsDashboard;

    public string RowFieldLabel => IsChart ? "Category field" : "Row field";

    public string ColumnFieldLabel => IsChart ? "Series field" : "Column field";

    // ---- common settings ------------------------------------------------------------------------

    public string Name
    {
        get => Report?.Name ?? string.Empty;
        set
        {
            if (Report is null || value is null)
            {
                return;
            }

            var name = value.Trim();
            var error = Identifier.Route(name).Length == 0
                ? "A report name needs at least one letter or digit. The last valid name was kept."
                : _main.Current.Reports.Any(r => !ReferenceEquals(r, Report) && Identifier.Route(r.Name) == Identifier.Route(name))
                    ? $"Another report already uses the URL /reports/{Identifier.Route(name)}. The last valid name was kept."
                    : null;
            var old = Report.Name;
            if (Accept(nameof(Name), value, error, () => Report.Name = name))
            {
                // Dashboards refer to reports by name, so they follow a rename.
                foreach (var item in _main.Current.Reports.SelectMany(r => r.Items).Where(i => string.Equals(i.Report, old, StringComparison.OrdinalIgnoreCase)))
                {
                    item.Report = name;
                }

                OnPropertyChanged(nameof(Route));
            }
        }
    }

    /// <summary>The URL of the report in the generated site.</summary>
    public string Route => Report is null ? string.Empty : "/reports/" + Identifier.Route(Report.Name);

    public string Caption
    {
        get => Report?.Caption ?? string.Empty;
        set => Edit(Report?.Caption, value ?? string.Empty, (r, v) => r.Caption = v);
    }

    public RelationChoice<ReportKind>? Kind
    {
        get => Report is null ? null : Kinds.First(c => c.Value == Report.Kind);
        set
        {
            if (Report is null || value is null || Report.Kind == value.Value)
            {
                return;
            }

            Report.Kind = value.Value;
            OnPropertyChanged();
            RaiseKind();
            RefreshLists();
            Changed();
        }
    }

    public bool Generate
    {
        get => Report?.Generate ?? false;
        set => Edit(Report?.Generate, value, (r, v) => r.Generate = v);
    }

    public string? SourceTable
    {
        get => Report?.SourceTable;
        set
        {
            if (Report is null || value is null || Report.SourceTable == value)
            {
                return;
            }

            Report.SourceTable = value;
            OnPropertyChanged();
            LoadFieldNames();
            RaiseFields();
            Changed();
        }
    }

    public string Filter
    {
        get => Report?.Filter ?? string.Empty;
        set => Edit(Report?.Filter, value ?? string.Empty, (r, v) => r.Filter = v);
    }

    public string Description
    {
        get => Report?.Description ?? string.Empty;
        set => Edit(Report?.Description, value ?? string.Empty, (r, v) => r.Description = v);
    }

    public string MinLevel
    {
        get => Shown(nameof(MinLevel), (Report?.MinLevel ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            if (Report is null)
            {
                return;
            }

            var ok = TryWholeNumber(value, 0, 1_000_000, out var level);
            Accept(nameof(MinLevel), value ?? string.Empty, ok ? null : RangeMessage("The user level", 0, 1_000_000), () => Report.MinLevel = level);
        }
    }

    public bool ShowInMenu
    {
        get => Report?.ShowInMenu ?? false;
        set => Edit(Report?.ShowInMenu, value, (r, v) => r.ShowInMenu = v);
    }

    public string MenuGroup
    {
        get => Report?.MenuGroup ?? string.Empty;
        set => Edit(Report?.MenuGroup, value ?? string.Empty, (r, v) => r.MenuGroup = v);
    }

    // ---- summary --------------------------------------------------------------------------------

    public bool ShowDetails
    {
        get => Report?.ShowDetails ?? false;
        set => Edit(Report?.ShowDetails, value, (r, v) => r.ShowDetails = v);
    }

    public bool ShowGrandTotal
    {
        get => Report?.ShowGrandTotal ?? false;
        set => Edit(Report?.ShowGrandTotal, value, (r, v) => r.ShowGrandTotal = v);
    }

    /// <summary>The columns listed for each row (and on the drill-down page), in order. None: the fields shown in the table's list.</summary>
    public ObservableCollection<ReportFieldRow> DetailColumns { get; } = [];

    public ReportFieldRow? SelectedDetailColumn
    {
        get => _selectedDetailColumn;
        set => SetProperty(ref _selectedDetailColumn, value);
    }

    // ---- filters and drill-down -----------------------------------------------------------------

    /// <summary>The From/To date range field, or <see cref="NoneLabel"/>.</summary>
    public string? DateFilterField
    {
        get => Report is null ? null : Report.DateFilterField.Length == 0 ? NoneLabel : Report.DateFilterField;
        set => Edit(Report?.DateFilterField, value is null ? null : value == NoneLabel ? string.Empty : value, (r, v) => r.DateFilterField = v);
    }

    /// <summary>The fields offered as pick lists above the report, in order.</summary>
    public ObservableCollection<ReportFieldRow> FilterFields { get; } = [];

    public ReportFieldRow? SelectedFilterField
    {
        get => _selectedFilterField;
        set => SetProperty(ref _selectedFilterField, value);
    }

    public bool DrillDown
    {
        get => Report?.DrillDown ?? false;
        set => Edit(Report?.DrillDown, value, (r, v) => r.DrillDown = v);
    }

    /// <summary>Filters apply to every kind that reads a table; the date range to summary, crosstab and chart.</summary>
    public bool HasDateRange => HasSource && !IsCalendar;

    public bool HasDrillDown => IsSummary || UsesGrid;

    // ---- crosstab and chart ---------------------------------------------------------------------

    public string? RowField
    {
        get => Report?.RowField;
        set => Edit(Report?.RowField, value, (r, v) => r.RowField = v);
    }

    public RelationChoice<DateInterval>? RowInterval
    {
        get => Report is null ? null : Intervals.First(c => c.Value == Report.RowInterval);
        set => Edit(Report?.RowInterval, value?.Value, (r, v) => r.RowInterval = v);
    }

    /// <summary>The column (series) field, or <see cref="NoneLabel"/>. A crosstab needs one; a chart may leave it empty.</summary>
    public string? ColumnField
    {
        get => Report is null ? null : Report.ColumnField.Length == 0 ? NoneLabel : Report.ColumnField;
        set => Edit(Report?.ColumnField, value is null ? null : value == NoneLabel ? string.Empty : value, (r, v) => r.ColumnField = v);
    }

    public RelationChoice<DateInterval>? ColumnInterval
    {
        get => Report is null ? null : Intervals.First(c => c.Value == Report.ColumnInterval);
        set => Edit(Report?.ColumnInterval, value?.Value, (r, v) => r.ColumnInterval = v);
    }

    public string? ValueField
    {
        get => Report is null ? null : Report.ValueField.Length == 0 ? RowsLabel : Report.ValueField;
        set => Edit(Report?.ValueField, value is null ? null : value == RowsLabel ? string.Empty : value, (r, v) => r.ValueField = v);
    }

    public RelationChoice<AggregateFunction>? Function
    {
        get => Report is null ? null : Functions.First(c => c.Value == Report.Function);
        set => Edit(Report?.Function, value?.Value, (r, v) => r.Function = v);
    }

    public string MaxColumns
    {
        get => Shown(nameof(MaxColumns), (Report?.MaxColumns ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            if (Report is null)
            {
                return;
            }

            var ok = TryWholeNumber(value, 1, 500, out var count);
            Accept(nameof(MaxColumns), value ?? string.Empty, ok ? null : RangeMessage("The most columns", 1, 500), () => Report.MaxColumns = count);
        }
    }

    // ---- chart ----------------------------------------------------------------------------------

    public RelationChoice<ChartType>? ChartType
    {
        get => Report is null ? null : ChartTypes.First(c => c.Value == Report.ChartType);
        set => Edit(Report?.ChartType, value?.Value, (r, v) => r.ChartType = v);
    }

    public RelationChoice<ChartOrder>? ChartOrder
    {
        get => Report is null ? null : ChartOrders.First(c => c.Value == Report.ChartOrder);
        set => Edit(Report?.ChartOrder, value?.Value, (r, v) => r.ChartOrder = v);
    }

    public string TopCount
    {
        get => Shown(nameof(TopCount), (Report?.TopCount ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            if (Report is null)
            {
                return;
            }

            var ok = TryWholeNumber(value, 0, 1000, out var count);
            Accept(nameof(TopCount), value ?? string.Empty, ok ? null : RangeMessage("The number of categories", 0, 1000), () => Report.TopCount = count);
        }
    }

    public bool ShowDataTable
    {
        get => Report?.ShowDataTable ?? false;
        set => Edit(Report?.ShowDataTable, value, (r, v) => r.ShowDataTable = v);
    }

    // ---- calendar -------------------------------------------------------------------------------

    public string? DateField
    {
        get => Report?.DateField;
        set => Edit(Report?.DateField, value, (r, v) => r.DateField = v);
    }

    public string? EndDateField
    {
        get => Report is null ? null : Report.EndDateField.Length == 0 ? NoneLabel : Report.EndDateField;
        set => Edit(Report?.EndDateField, value is null ? null : value == NoneLabel ? string.Empty : value, (r, v) => r.EndDateField = v);
    }

    public string? TitleField
    {
        get => Report is null ? null : Report.TitleField.Length == 0 ? NoneLabel : Report.TitleField;
        set => Edit(Report?.TitleField, value is null ? null : value == NoneLabel ? string.Empty : value, (r, v) => r.TitleField = v);
    }

    public bool LinkToView
    {
        get => Report?.LinkToView ?? false;
        set => Edit(Report?.LinkToView, value, (r, v) => r.LinkToView = v);
    }

    public bool WeekStartsMonday
    {
        get => Report?.WeekStartsMonday ?? false;
        set => Edit(Report?.WeekStartsMonday, value, (r, v) => r.WeekStartsMonday = v);
    }

    // ---- commands -------------------------------------------------------------------------------

    [RelayCommand]
    private void AddReport()
    {
        var project = _main.Current;
        var number = project.Reports.Count + 1;
        while (project.Reports.Any(r => Identifier.Route(r.Name) == "report" + number))
        {
            number++;
        }

        var report = new ReportInfo
        {
            Name = "report" + number,
            Caption = "Report " + number,
            SourceTable = _main.Tables.SelectedTable?.Generate == true ? _main.Tables.SelectedTable.Name : project.Tables.FirstOrDefault(t => t.Generate)?.Name ?? string.Empty,
        };
        project.Reports.Add(report);
        var row = new ReportRow(report);
        Reports.Add(row);
        SelectedRow = row;
        Changed();
        _main.Status = $"Added report {report.Name}.";
    }

    [RelayCommand]
    private void RemoveReport()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var index = Reports.IndexOf(row);
        _main.Current.Reports.Remove(row.Report);
        Reports.Remove(row);
        SelectedRow = Reports.Count == 0 ? null : Reports[Math.Min(index, Reports.Count - 1)];
        Changed();
        _main.Status = $"Removed report {row.Report.Name}.";
    }

    [RelayCommand]
    private void MoveReportUp() => MoveReport(-1);

    [RelayCommand]
    private void MoveReportDown() => MoveReport(1);

    private void MoveReport(int delta)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var reports = _main.Current.Reports;
        var index = reports.IndexOf(row.Report);
        var target = index + delta;
        if (target < 0 || target >= reports.Count)
        {
            return;
        }

        reports.RemoveAt(index);
        reports.Insert(target, row.Report);
        Reports.Move(index, target);
        Changed();
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (Report is null)
        {
            return;
        }

        var group = new ReportGroup { Field = FieldNames.FirstOrDefault() ?? string.Empty };
        Report.Groups.Add(group);
        var row = new ReportGroupRow(group, Changed);
        Groups.Add(row);
        SelectedGroup = row;
        Changed();
    }

    [RelayCommand]
    private void RemoveGroup()
    {
        if (Report is null || SelectedGroup is not { } row)
        {
            return;
        }

        Report.Groups.Remove(row.Model);
        Groups.Remove(row);
        Changed();
    }

    [RelayCommand]
    private void AddAggregate()
    {
        if (Report is null)
        {
            return;
        }

        var aggregate = new ReportAggregate();
        Report.Aggregates.Add(aggregate);
        var row = new ReportAggregateRow(aggregate, Changed);
        Aggregates.Add(row);
        SelectedAggregate = row;
        Changed();
    }

    [RelayCommand]
    private void RemoveAggregate()
    {
        if (Report is null || SelectedAggregate is not { } row)
        {
            return;
        }

        Report.Aggregates.Remove(row.Model);
        Aggregates.Remove(row);
        Changed();
    }

    [RelayCommand]
    private void AddDetailColumn() => AddFieldRow(DetailColumns, row => SelectedDetailColumn = row);

    [RelayCommand]
    private void RemoveDetailColumn() => RemoveFieldRow(DetailColumns, SelectedDetailColumn);

    [RelayCommand]
    private void MoveDetailColumnUp() => MoveFieldRow(DetailColumns, SelectedDetailColumn, -1);

    [RelayCommand]
    private void MoveDetailColumnDown() => MoveFieldRow(DetailColumns, SelectedDetailColumn, 1);

    [RelayCommand]
    private void AddFilterField() => AddFieldRow(FilterFields, row => SelectedFilterField = row);

    [RelayCommand]
    private void RemoveFilterField() => RemoveFieldRow(FilterFields, SelectedFilterField);

    /// <summary>Adds a row with the first field not listed yet.</summary>
    private void AddFieldRow(ObservableCollection<ReportFieldRow> rows, Action<ReportFieldRow> select)
    {
        if (Report is null)
        {
            return;
        }

        var field = FieldNames.FirstOrDefault(f => rows.All(r => !string.Equals(r.Field, f, StringComparison.OrdinalIgnoreCase))) ?? FieldNames.FirstOrDefault() ?? string.Empty;
        var row = new ReportFieldRow(field, SyncFieldLists);
        rows.Add(row);
        select(row);
        SyncFieldLists();
    }

    private void RemoveFieldRow(ObservableCollection<ReportFieldRow> rows, ReportFieldRow? row)
    {
        if (row is not null && rows.Remove(row))
        {
            SyncFieldLists();
        }
    }

    private void MoveFieldRow(ObservableCollection<ReportFieldRow> rows, ReportFieldRow? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var index = rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= rows.Count)
        {
            return;
        }

        rows.Move(index, target);
        SyncFieldLists();
    }

    /// <summary>Writes the row column and filter lists back to the report.</summary>
    private void SyncFieldLists()
    {
        if (Report is null)
        {
            return;
        }

        Report.DetailColumns.Clear();
        Report.DetailColumns.AddRange(DetailColumns.Select(r => r.Field ?? string.Empty));
        Report.FilterFields.Clear();
        Report.FilterFields.AddRange(FilterFields.Select(r => r.Field ?? string.Empty));
        Changed();
    }

    [RelayCommand]
    private void AddItem()
    {
        if (Report is null)
        {
            return;
        }

        var item = new DashboardItem { Report = DashboardChoices.FirstOrDefault(c => Report.Items.All(i => i.Report != c)) ?? DashboardChoices.FirstOrDefault() ?? string.Empty };
        Report.Items.Add(item);
        var row = new DashboardItemRow(item, Changed);
        Items.Add(row);
        SelectedItem = row;
        Changed();
    }

    [RelayCommand]
    private void RemoveItem()
    {
        if (Report is null || SelectedItem is not { } row)
        {
            return;
        }

        Report.Items.Remove(row.Model);
        Items.Remove(row);
        Changed();
    }

    // ---- loading --------------------------------------------------------------------------------

    /// <summary>Rebuilds the tab from the open project.</summary>
    public void Reload()
    {
        var keep = Report;
        Reports.Clear();
        foreach (var report in _main.Current.Reports)
        {
            Reports.Add(new ReportRow(report));
        }

        _selectedRow = Reports.FirstOrDefault(r => ReferenceEquals(r.Report, keep)) ?? Reports.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRow));
        LoadReport();
    }

    /// <summary>Called by the main view model when the selected report should be named in the breadcrumb.</summary>
    internal string? SelectedName => Report?.Name;

    private void LoadReport()
    {
        ForgetRawText();
        Message = string.Empty;
        Groups.Clear();
        Aggregates.Clear();
        Items.Clear();
        DetailColumns.Clear();
        FilterFields.Clear();
        if (Report is { } report)
        {
            foreach (var group in report.Groups)
            {
                Groups.Add(new ReportGroupRow(group, Changed));
            }

            foreach (var aggregate in report.Aggregates)
            {
                Aggregates.Add(new ReportAggregateRow(aggregate, Changed));
            }

            foreach (var column in report.DetailColumns)
            {
                DetailColumns.Add(new ReportFieldRow(column, SyncFieldLists));
            }

            foreach (var filter in report.FilterFields)
            {
                FilterFields.Add(new ReportFieldRow(filter, SyncFieldLists));
            }

            foreach (var item in report.Items)
            {
                Items.Add(new DashboardItemRow(item, Changed));
            }
        }

        RefreshLists();
        OnPropertyChanged(nameof(Report));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Route));
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Generate));
        OnPropertyChanged(nameof(SourceTable));
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(MinLevel));
        OnPropertyChanged(nameof(ShowInMenu));
        OnPropertyChanged(nameof(MenuGroup));
        OnPropertyChanged(nameof(ShowDetails));
        OnPropertyChanged(nameof(ShowGrandTotal));
        OnPropertyChanged(nameof(DateFilterField));
        OnPropertyChanged(nameof(DrillDown));
        OnPropertyChanged(nameof(MaxColumns));
        OnPropertyChanged(nameof(ChartType));
        OnPropertyChanged(nameof(ChartOrder));
        OnPropertyChanged(nameof(TopCount));
        OnPropertyChanged(nameof(ShowDataTable));
        OnPropertyChanged(nameof(LinkToView));
        OnPropertyChanged(nameof(WeekStartsMonday));
        RaiseKind();
        RaiseFields();
    }

    /// <summary>Refreshes the table, field and dashboard pickers and the problem list.</summary>
    private void RefreshLists()
    {
        Sync(TableNames, _main.Current.Tables.Where(t => t.Generate).Select(t => t.Name));
        Sync(DashboardChoices, _main.Current.Reports.Where(r => r.Kind != ReportKind.Dashboard && !ReferenceEquals(r, Report)).Select(r => r.Name));
        LoadFieldNames();
        RefreshProblems();
    }

    private void LoadFieldNames()
    {
        var table = _main.Current.Tables.FirstOrDefault(t => string.Equals(t.Name, Report?.SourceTable, StringComparison.OrdinalIgnoreCase));
        var names = table?.Fields.Where(f => f.Type != FieldType.Binary).Select(f => f.Name).ToList() ?? [];
        Sync(FieldNames, names);
        Sync(OptionalFieldNames, names.Prepend(NoneLabel));
        Sync(ValueFieldNames, names.Prepend(RowsLabel));
        var dates = table?.Fields.Where(ReportValidator.IsDateField).Select(f => f.Name) ?? [];
        Sync(DateFieldNames, dates.Prepend(NoneLabel));
    }

    /// <summary>Replaces the items only when they differ, so the pickers keep their selection.</summary>
    private static void Sync(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (target.SequenceEqual(list))
        {
            return;
        }

        target.Clear();
        foreach (var value in list)
        {
            target.Add(value);
        }
    }

    private void RefreshProblems()
    {
        var problems = ReportValidator.Problems(_main.Current.Reports, _main.Current.Tables.Where(t => t.Generate).ToList());
        Problems.Clear();
        foreach (var problem in problems)
        {
            Problems.Add(problem);
        }

        OnPropertyChanged(nameof(ProblemSummary));
    }

    private void RaiseKind()
    {
        OnPropertyChanged(nameof(IsSummary));
        OnPropertyChanged(nameof(IsCrosstab));
        OnPropertyChanged(nameof(IsChart));
        OnPropertyChanged(nameof(UsesGrid));
        OnPropertyChanged(nameof(IsCalendar));
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(RowFieldLabel));
        OnPropertyChanged(nameof(ColumnFieldLabel));
        OnPropertyChanged(nameof(HasDateRange));
        OnPropertyChanged(nameof(HasDrillDown));
    }

    private void RaiseFields()
    {
        OnPropertyChanged(nameof(RowField));
        OnPropertyChanged(nameof(RowInterval));
        OnPropertyChanged(nameof(ColumnField));
        OnPropertyChanged(nameof(ColumnInterval));
        OnPropertyChanged(nameof(ValueField));
        OnPropertyChanged(nameof(Function));
        OnPropertyChanged(nameof(DateField));
        OnPropertyChanged(nameof(EndDateField));
        OnPropertyChanged(nameof(TitleField));
        OnPropertyChanged(nameof(DateFilterField));
        foreach (var row in DetailColumns.Concat(FilterFields))
        {
            row.OnFieldsChanged();
        }

        foreach (var group in Groups)
        {
            group.OnFieldsChanged();
        }

        foreach (var aggregate in Aggregates)
        {
            aggregate.OnFieldsChanged();
        }
    }

    protected override void Changed()
    {
        SelectedRow?.Refresh();
        RefreshProblems();
    }

    /// <summary>Writes a text of the selected report when it differs. A null value (a picker being rebuilt) is ignored.</summary>
    private void Edit(string? current, string? value, Action<ReportInfo, string> store, [CallerMemberName] string? name = null)
    {
        if (Report is null || value is null || current == value)
        {
            return;
        }

        store(Report, value);
        OnPropertyChanged(name);
        Changed();
    }

    /// <summary>Writes a flag or choice of the selected report when it differs. A null value (a picker being rebuilt) is ignored.</summary>
    private void Edit<T>(T? current, T? value, Action<ReportInfo, T> store, [CallerMemberName] string? name = null)
        where T : struct
    {
        if (Report is null || value is not { } chosen || EqualityComparer<T?>.Default.Equals(current, chosen))
        {
            return;
        }

        store(Report, chosen);
        OnPropertyChanged(name);
        Changed();
    }
}
