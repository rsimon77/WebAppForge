using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One line of the report list: icon, caption, type and source table. It follows the report it wraps.</summary>
public sealed class ReportRow : ObservableObject
{
    public ReportRow(ReportDef model) => Model = model;

    public ReportDef Model { get; }

    public string Caption => string.IsNullOrWhiteSpace(Model.Caption) ? Model.Name : Model.Caption;

    public string TypeText => Model.Type.ToString();

    /// <summary>The source table, or "(layout)" for a dashboard, which reads through its items.</summary>
    public string TableText => Model.Type == ReportType.Dashboard ? "(dashboard)" : string.IsNullOrEmpty(Model.Table) ? "(no table)" : Model.Table;

    /// <summary>The Bootstrap Icons name of the report, shown next to the type icon.</summary>
    public string IconName => Model.Icon;

    public Geometry TypeIcon => Geometry.Parse(ReportTypeInfo.Of(Model.Type).IconPath);

    internal void Refresh() => OnPropertyChanged(string.Empty);

    public override string ToString() => $"{Caption} ({TypeText}, {TableText})";
}

/// <summary>One row of the preview result grid.</summary>
public sealed record PreviewRow(IReadOnlyList<string> Cells);

/// <summary>
/// The Reports tab: the report list, the settings every report has, the designer panel of the selected report's type, the
/// schematic preview and the "Run preview" result grid. Every property writes straight into <see cref="Project.Reports"/>, so
/// Save persists it and the dirty tracking (the customization fingerprint) sees it. Validation findings come from
/// <see cref="IReportValidationService"/>; errors disable Generate through <see cref="SecurityViewModel.CanGenerate"/>.
/// </summary>
public partial class ReportsViewModel : EditorViewModelBase
{
    /// <summary>Rows of the preview grid are capped here, so a large result cannot freeze the window.</summary>
    public const int MaxPreviewRows = 200;

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly MainViewModel _main;
    private ReportRow? _selectedReport;
    private ReportDesignerBase? _designer;
    private ReportSchematic? _schematic;
    private string _searchText = string.Empty;
    private CancellationTokenSource? _previewCts;
    private bool _isPreviewRunning;
    private string _previewError = string.Empty;
    private string _previewSummary = string.Empty;
    private IReadOnlyList<string> _previewColumns = [];

    public ReportsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex) && MainViewModel.TabNames[Math.Clamp(_main.ActiveTabIndex, 0, MainViewModel.TabNames.Count - 1)] == "Reports")
            {
                Reload();
            }
        };
    }

    // ---- project access ----

    public Project Project => _main.Current;

    private List<ReportDef> All => _main.Current.Reports;

    public TableInfo? TableByName(string? name)
        => string.IsNullOrEmpty(name) ? null : _main.Current.Tables.FirstOrDefault(t => Names.Equals(t.Name, name));

    public ReportDef? ReportByName(string? name)
        => string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(r => Names.Equals(r.Name, name));

    /// <summary>The names of the reports a dashboard may embed: every report except dashboards and the dashboard itself.</summary>
    public IEnumerable<string> DashboardReportNames(ReportDef dashboard)
        => All.Where(r => r.Type != ReportType.Dashboard && !ReferenceEquals(r, dashboard)).Select(r => r.Name);

    public IEnumerable<string> AllReportNames(ReportDef except)
        => All.Where(r => !ReferenceEquals(r, except)).Select(r => r.Name);

    /// <summary>The names of all tables of the project (a live view; refreshed whenever the tab opens).</summary>
    public ObservableCollection<string> TableNames { get; } = [];

    public ObservableCollection<ReportRow> Reports { get; } = [];

    public IReadOnlyList<ReportTypeInfo> TypeInfos => ReportTypeInfo.All;

    public IReadOnlyList<string> IconChoices => CustomizationChoices.Icons;

    // ---- selection ----

    public ReportRow? SelectedReport
    {
        get => _selectedReport;
        set
        {
            // A list box reports null while its items are rebuilt (search, reload); that is not a choice.
            if (_filtering && value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedReport, value))
            {
                Message = string.Empty;
                ForgetRawText();
                ClearPreview();
                BuildDesigner();
                OnPropertyChanged(string.Empty);
                _main.RefreshBreadcrumb();
            }
        }
    }

    private ReportDef? Def => _selectedReport?.Model;

    public bool HasReport => Def is not null;

    public bool IsDashboard => Def?.Type == ReportType.Dashboard;

    /// <summary>The source table picker is hidden for dashboards, which read through their items.</summary>
    public bool ShowTable => HasReport && !IsDashboard;

    public string HeaderText => Def is null ? "Select a report, or add one." : $"{ReportTypeInfo.Of(Def.Type).Type} report";

    public string TypeDescription => Def is null ? string.Empty : ReportTypeInfo.Of(Def.Type).Description;

    /// <summary>The designer panel of the selected report's type (a view model the matching view is built for).</summary>
    public ReportDesignerBase? Designer
    {
        get => _designer;
        private set => SetProperty(ref _designer, value);
    }

    /// <summary>The schematic of the selected report, redrawn after every change.</summary>
    public ReportSchematic? Schematic
    {
        get => _schematic;
        private set => SetProperty(ref _schematic, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    // ---- loading ----

    /// <summary>Rebuilds the list from the project, keeping the selection when the report still exists.</summary>
    public void Reload()
    {
        var previous = Def;
        RefreshTables();
        _rows.Clear();
        ApplyFilter();
        SelectedReport = Reports.FirstOrDefault(r => ReferenceEquals(r.Model, previous)) ?? Reports.FirstOrDefault();
        _main.Security.Refresh();
        OnPropertyChanged(string.Empty);
    }

    private void RefreshTables() => ReportRowBase.SetItems(TableNames, _main.Current.Tables.Select(t => t.Name));

    private readonly Dictionary<ReportDef, ReportRow> _rows = new(ReferenceEqualityComparer.Instance);
    private bool _filtering;

    private ReportRow RowOf(ReportDef def)
    {
        if (!_rows.TryGetValue(def, out var row))
        {
            _rows[def] = row = new ReportRow(def);
        }

        return row;
    }

    /// <summary>Shows the reports that match the search text (name, caption, table or type). The row objects stay the same, so the selection holds.</summary>
    private void ApplyFilter()
    {
        var text = SearchText.Trim();
        foreach (var gone in _rows.Keys.Where(d => !All.Contains(d)).ToList())
        {
            _rows.Remove(gone);
        }

        var wanted = All
            .Where(r => text.Length == 0
                        || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || r.Caption.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || r.Table.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || r.Type.ToString().Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(RowOf)
            .ToList();

        _filtering = true;
        try
        {
            Reports.Clear();
            foreach (var row in wanted)
            {
                Reports.Add(row);
            }
        }
        finally
        {
            _filtering = false;
        }

        OnPropertyChanged(nameof(SelectedReport));
    }

    // ---- the list commands ----

    /// <summary>Asks for the report type (with a one-line description of each) and adds a report of that type.</summary>
    [RelayCommand]
    public async Task AddReportAsync()
    {
        var choice = await _main.Dialogs.ChooseOneAsync(
            "New report",
            "Choose the type of report to add.",
            ReportTypeInfo.All.Select(i => i.Label).ToList());
        if (ReportTypeInfo.FromChoice(choice) is { } type)
        {
            AddReport(type);
        }
    }

    /// <summary>Adds a report of <paramref name="type"/> (appended to the list and selected) on the table of the selected report or the selected table.</summary>
    public ReportRow AddReport(ReportType type)
    {
        RefreshTables();
        var table = TableByName(Def?.Table) ?? _main.Tables.SelectedTable ?? _main.Current.Tables.FirstOrDefault();
        var name = ReportNames.Suggest(ReportDefaults.CaptionFor(type), All.Select(r => r.Name));
        var def = ReportDefaults.Create(type, name, table);
        All.Add(def);
        var row = RowOf(def);
        SearchText = string.Empty;
        ApplyFilter();
        SelectedReport = row;
        NotifyChanged();
        return row;
    }

    [RelayCommand]
    public void DuplicateReport()
    {
        if (Def is not { } source)
        {
            return;
        }

        var copy = (ReportDef)DeepCopy.Clone(source)!;
        copy.Caption = string.IsNullOrWhiteSpace(source.Caption) ? source.Name : source.Caption + " (copy)";
        copy.Name = ReportNames.Suggest(source.Name, All.Select(r => r.Name));
        All.Insert(All.IndexOf(source) + 1, copy);
        var row = RowOf(copy);
        SearchText = string.Empty;
        ApplyFilter();
        SelectedReport = row;
        NotifyChanged();
    }

    [RelayCommand]
    public async Task DeleteReportAsync()
    {
        if (Def is not { } def)
        {
            return;
        }

        var users = All.Count(r => !ReferenceEquals(r, def)
            && (Names.Equals(r.DrillDownReport, def.Name) || r.Dashboard.Items.Any(i => i.Kind == DashboardItemKind.Report && Names.Equals(i.Report, def.Name))));
        var message = $"Delete the report '{def.Name}'? This cannot be undone."
                      + (users > 0 ? $" {users} other report(s) refer to it (as a drill-down or a dashboard item)." : string.Empty);
        if (!await _main.Dialogs.ConfirmAsync("Delete report", message, "Delete"))
        {
            return;
        }

        var visibleIndex = Reports.IndexOf(SelectedReport!);
        foreach (var other in All.Where(r => Names.Equals(r.DrillDownReport, def.Name)))
        {
            other.DrillDownReport = string.Empty;
        }

        All.Remove(def);
        ApplyFilter();
        SelectedReport = Reports.Count == 0 ? null : Reports[Math.Clamp(visibleIndex, 0, Reports.Count - 1)];
        NotifyChanged();
    }

    [RelayCommand]
    public void MoveReportUp() => MoveReport(-1);

    [RelayCommand]
    public void MoveReportDown() => MoveReport(1);

    /// <summary>Swaps the selected report with its neighbour in the visible list (with a search active, the next report that matches).</summary>
    private void MoveReport(int delta)
    {
        if (SelectedReport is not { } row)
        {
            return;
        }

        var from = Reports.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Reports.Count)
        {
            return;
        }

        var a = All.IndexOf(row.Model);
        var b = All.IndexOf(Reports[to].Model);
        (All[a], All[b]) = (All[b], All[a]);
        Reports.Move(from, to);
        NotifyChanged();
    }

    // ---- settings every report has ----

    private string[] OtherNames(ReportDef def) => All.Where(r => !ReferenceEquals(r, def)).Select(r => r.Name).ToArray();

    public string Name
    {
        get => Shown(nameof(Name), Def?.Name ?? string.Empty);
        set
        {
            if (Def is not { } def)
            {
                return;
            }

            var text = (value ?? string.Empty).Trim();
            Accept(nameof(Name), text, ReportNames.Check(text, OtherNames(def)), () => Rename(def, text));
        }
    }

    /// <summary>Renames a report and follows the references to it (drill-downs, dashboard items, card links).</summary>
    private void Rename(ReportDef def, string name)
    {
        var old = def.Name;
        if (old == name)
        {
            return;
        }

        def.Name = name;
        foreach (var other in All)
        {
            if (Names.Equals(other.DrillDownReport, old))
            {
                other.DrillDownReport = name;
            }

            foreach (var item in other.Dashboard.Items)
            {
                if (item.Kind == DashboardItemKind.Report && Names.Equals(item.Report, old))
                {
                    item.Report = name;
                }

                if (Names.Equals(item.LinkTo, old))
                {
                    item.LinkTo = name;
                }
            }
        }
    }

    public string Caption
    {
        get => Def?.Caption ?? string.Empty;
        set
        {
            if (Def is not { } def || value is null || def.Caption == value)
            {
                return;
            }

            // The name follows the caption while it is still the suggestion for the old caption.
            var others = OtherNames(def);
            var followed = def.Name.Length == 0 || def.Name == ReportNames.Suggest(def.Caption, others);
            def.Caption = value;
            if (followed)
            {
                Rename(def, ReportNames.Suggest(value, others));
                ForgetRawText();
                OnPropertyChanged(nameof(Name));
            }

            OnPropertyChanged(nameof(Caption));
            Message = string.Empty;
            Changed();
        }
    }

    /// <summary>The source table (hidden for dashboards). Changing it clears the field choices that the new table does not have.</summary>
    public string TableName
    {
        get => Def?.Table ?? string.Empty;
        set
        {
            if (Def is not { } def || value is null || def.Table == value)
            {
                return;
            }

            def.Table = value;
            ReportDefaults.PruneFields(def, TableByName(value));
            if (ReportByName(def.DrillDownReport) is { } drill && !Names.Equals(drill.Table, value))
            {
                def.DrillDownReport = string.Empty;
            }

            BuildDesigner();
            OnPropertyChanged(nameof(TableName));
            OnPropertyChanged(nameof(DrillDownChoice));
            Changed();
        }
    }

    public string Filter
    {
        get => Def?.Filter ?? string.Empty;
        set => Set(value ?? string.Empty, r => r.Filter, (r, v) => r.Filter = v);
    }

    public string Description
    {
        get => Def?.Description ?? string.Empty;
        set => Set(value ?? string.Empty, r => r.Description, (r, v) => r.Description = v);
    }

    public bool ShowInMenu
    {
        get => Def?.ShowInMenu ?? false;
        set => Set(value, r => r.ShowInMenu, (r, v) => r.ShowInMenu = v);
    }

    public string MenuGroup
    {
        get => Def?.MenuGroup ?? string.Empty;
        set => Set(value ?? string.Empty, r => r.MenuGroup, (r, v) => r.MenuGroup = v);
    }

    public string MenuOrderText
    {
        get => Shown(nameof(MenuOrderText), (Def?.MenuOrder ?? 0).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 0, 9999, out var order);
            Accept(nameof(MenuOrderText), text, ok ? null : RangeMessage("Menu order", 0, 9999), () =>
            {
                if (Def is { } def)
                {
                    def.MenuOrder = order;
                }
            });
        }
    }

    /// <summary>Bootstrap Icons name; pick one from the list or type any.</summary>
    public string Icon
    {
        get => Def?.Icon ?? string.Empty;
        set => Set((value ?? string.Empty).Trim(), r => r.Icon, (r, v) => r.Icon = v);
    }

    public string MinLevelText
    {
        get => Shown(nameof(MinLevelText), (Def?.MinLevel ?? 0).ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = TryWholeNumber(text, 0, 9999, out var level);
            Accept(nameof(MinLevelText), text, ok ? null : RangeMessage("Minimum level", 0, 9999), () =>
            {
                if (Def is { } def)
                {
                    def.MinLevel = level;
                }
            });
        }
    }

    /// <summary>The table's list page, then the Summary, Crosstab and Chart reports of the same table (not the report itself).</summary>
    public ObservableCollection<string> DrillDownChoices { get; } = [];

    public string DrillDownChoice
    {
        get => ReportFieldRules.Display(Def?.DrillDownReport, ReportChoices.TableListLabel);
        set
        {
            if (value is not null)
            {
                Set(ReportFieldRules.Stored(value, ReportChoices.TableListLabel), r => r.DrillDownReport, (r, v) => r.DrillDownReport = v, nameof(DrillDownChoice));
            }
        }
    }

    private void RefreshDrillDownChoices()
    {
        var def = Def;
        var choices = new List<string> { ReportChoices.TableListLabel };
        if (def is not null && def.Type != ReportType.Dashboard)
        {
            choices.AddRange(All
                .Where(r => !ReferenceEquals(r, def) && Names.Equals(r.Table, def.Table) && r.Type is ReportType.Summary or ReportType.Crosstab or ReportType.Chart)
                .Select(r => r.Name));
        }

        ReportRowBase.SetItems(DrillDownChoices, choices);
        OnPropertyChanged(nameof(DrillDownChoice));
    }

    private bool Set<T>(T value, Func<ReportDef, T> get, Action<ReportDef, T> set, [CallerMemberName] string? name = null)
        => Def is { } def && Put(get(def), value, v => set(def, v), name);

    // ---- designer, schematic and change notification ----

    private void BuildDesigner()
    {
        RefreshTables();
        Designer = Def is not { } def
            ? null
            : def.Type switch
            {
                ReportType.Crosstab => new CrosstabReportViewModel(this, def),
                ReportType.Chart => new ChartReportViewModel(this, def),
                ReportType.Calendar => new CalendarReportViewModel(this, def),
                ReportType.Dashboard => new DashboardReportViewModel(this, def),
                _ => new SummaryReportViewModel(this, def),
            };
        RefreshDrillDownChoices();
        RebuildSchematic();
    }

    private void RebuildSchematic() => Schematic = Def is { } def ? ReportSchematicBuilder.Build(_main.Current, def) : null;

    protected override void Changed() => NotifyChanged();

    /// <summary>Called after anything of the selected report changed (by this view model or by its designer).</summary>
    internal void NotifyChanged()
    {
        SelectedReport?.Refresh();
        RefreshDrillDownChoices();
        RebuildSchematic();
        OnPropertyChanged(nameof(HeaderText));

        // Runs the validation service; the Generate gate follows.
        _main.Security.Refresh();
    }

    // ---- validation ----

    /// <summary>Every finding of the validation service for the whole project.</summary>
    public ObservableCollection<ReportIssueView> Issues { get; } = [];

    /// <summary>The findings about the selected report.</summary>
    public ObservableCollection<ReportIssueView> SelectedIssues { get; } = [];

    public bool HasIssues => Issues.Count > 0;

    public bool HasSelectedIssues => SelectedIssues.Count > 0;

    public string IssueSummary
    {
        get
        {
            var errors = Issues.Count(i => i.IsError);
            var warnings = Issues.Count - errors;
            return Issues.Count == 0
                ? "No problems found."
                : $"{errors} error(s) (they disable Generate), {warnings} warning(s).";
        }
    }

    /// <summary>
    /// Runs the validation service and refreshes the issue lists. Returns the first error as a sentence for the Generate gate, or
    /// null when there is none (warnings never block). Called by <see cref="SecurityViewModel.Refresh"/>.
    /// </summary>
    internal string? UpdateIssues()
    {
        IReadOnlyList<ReportIssueView> found;
        try
        {
            found = _main.ReportValidation.Validate(_main.Current);
        }
        catch (Exception ex)
        {
            found = [new ReportIssueView(ReportIssueSeverity.Warning, string.Empty, $"The report validation failed: {ex.Message}")];
        }

        ReportRowBase.SetItems(Issues, found);
        ReportRowBase.SetItems(SelectedIssues, found.Where(i => Def is { } def && Names.Equals(i.ReportName, def.Name)));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasSelectedIssues));
        OnPropertyChanged(nameof(IssueSummary));
        var first = found.FirstOrDefault(i => i.IsError);
        return first is null ? null : (string.IsNullOrEmpty(first.ReportName) ? first.Message : $"Report '{first.ReportName}': {first.Message}");
    }

    // ---- run preview ----

    public IReadOnlyList<string> PreviewColumns
    {
        get => _previewColumns;
        private set => SetProperty(ref _previewColumns, value);
    }

    public ObservableCollection<PreviewRow> PreviewRows { get; } = [];

    public bool IsPreviewRunning
    {
        get => _isPreviewRunning;
        private set => SetProperty(ref _isPreviewRunning, value);
    }

    /// <summary>The reason the last run failed (shown in the error colour), or empty.</summary>
    public string PreviewError
    {
        get => _previewError;
        private set
        {
            if (SetProperty(ref _previewError, value))
            {
                OnPropertyChanged(nameof(HasPreviewError));
            }
        }
    }

    public bool HasPreviewError => _previewError.Length > 0;

    /// <summary>"12 row(s)" or "First 200 of 1500 row(s)".</summary>
    public string PreviewSummary
    {
        get => _previewSummary;
        private set => SetProperty(ref _previewSummary, value);
    }

    /// <summary>Runs the selected report through <see cref="IReportPreviewService"/>. A new run cancels the one before it.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RunPreviewAsync()
    {
        if (Def is not { } def)
        {
            return;
        }

        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        ClearPreview(keepRunning: true);
        IsPreviewRunning = true;
        try
        {
            var result = await _main.ReportPreview.RunAsync(_main.Current, def, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_previewCts, cts))
            {
                return;
            }

            ShowPreview(result);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_previewCts, cts))
            {
                PreviewError = "The preview was cancelled.";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_previewCts, cts))
            {
                PreviewError = $"The preview failed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCts, cts))
            {
                IsPreviewRunning = false;
            }
        }
    }

    [RelayCommand]
    public void CancelPreview()
    {
        _previewCts?.Cancel();
        if (IsPreviewRunning)
        {
            IsPreviewRunning = false;
            PreviewError = "The preview was cancelled.";
        }
    }

    private void ShowPreview(ReportPreviewResult result)
    {
        if (!result.Succeeded)
        {
            PreviewError = result.Error!;
            return;
        }

        PreviewColumns = result.Columns;
        foreach (var row in result.Rows.Take(MaxPreviewRows))
        {
            PreviewRows.Add(new PreviewRow(Enumerable.Range(0, result.Columns.Length).Select(i => i < row.Length ? CellText(row[i]) : string.Empty).ToArray()));
        }

        PreviewSummary = result.Rows.Length > MaxPreviewRows
            ? $"First {MaxPreviewRows} of {result.Rows.Length} row(s)."
            : $"{result.Rows.Length} row(s).";
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private void ClearPreview(bool keepRunning = false)
    {
        if (!keepRunning)
        {
            _previewCts?.Cancel();
            _previewCts = null;
            IsPreviewRunning = false;
        }

        PreviewRows.Clear();
        PreviewColumns = [];
        PreviewError = string.Empty;
        PreviewSummary = string.Empty;
    }
}
