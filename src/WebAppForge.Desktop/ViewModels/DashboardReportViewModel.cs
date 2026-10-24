using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One item of a dashboard: a KPI card (own table and aggregate) or an embedded report, with its span and height.</summary>
public sealed class DashboardItemRow : ReportRowBase
{
    private readonly DashboardReportViewModel _designer;

    public DashboardItemRow(DashboardItem model, DashboardReportViewModel designer)
        : base(designer)
    {
        Model = model;
        _designer = designer;
        Value = new AggregateRow(model.Value, designer, () => designer.Owner.TableByName(model.Table));
    }

    public DashboardItem Model { get; }

    /// <summary>The aggregate of a KPI card; its field choices follow the card's own table.</summary>
    public AggregateRow Value { get; }

    public DashboardItemKind Kind => Model.Kind;

    public bool IsKpi => Model.Kind == DashboardItemKind.Kpi;

    public bool IsReport => Model.Kind == DashboardItemKind.Report;

    public string Caption
    {
        get => Model.Caption;
        set
        {
            if (Write(Model.Caption, value ?? string.Empty, v => Model.Caption = v, nameof(Caption)))
            {
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    public string Table
    {
        get => Model.Table;
        set
        {
            if (value is null || !Write(Model.Table, value, v => Model.Table = v, nameof(Table)))
            {
                return;
            }

            // The aggregate's field belongs to the old table.
            var table = _designer.Owner.TableByName(value);
            if (Model.Value.Field.Length > 0 && ReportFieldRules.Find(table, Model.Value.Field) is null)
            {
                Model.Value.Field = string.Empty;
                Model.Value.Function = AggregateFunction.Count;
            }

            Value.RefreshAll();
            OnPropertyChanged(nameof(Text));
        }
    }

    public string Filter
    {
        get => Model.Filter;
        set => Write(Model.Filter, value ?? string.Empty, v => Model.Filter = v, nameof(Filter));
    }

    public string Style
    {
        get => Model.Style;
        set
        {
            if (value is not null)
            {
                Write(Model.Style, value, v => Model.Style = v, nameof(Style));
            }
        }
    }

    public string Icon
    {
        get => Model.Icon;
        set => Write(Model.Icon, (value ?? string.Empty).Trim(), v => Model.Icon = v, nameof(Icon));
    }

    /// <summary>The report or table opened when the card is clicked. "(none)" for no link.</summary>
    public string LinkTo
    {
        get => ReportFieldRules.Display(Model.LinkTo, ReportFieldRules.NoneLabel);
        set
        {
            if (value is not null)
            {
                Write(Model.LinkTo, ReportFieldRules.Stored(value, ReportFieldRules.NoneLabel), v => Model.LinkTo = v, nameof(LinkTo));
            }
        }
    }

    public string Report
    {
        get => Model.Report;
        set
        {
            if (value is not null && Write(Model.Report, value, v => Model.Report = v, nameof(Report)))
            {
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    public string SpanText
    {
        get => Shown(nameof(SpanText), Model.Span.ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = WholeNumber(text, 1, DashboardLayout.Columns, out var span);
            AcceptText(nameof(SpanText), text, ok ? null : $"Span must be a whole number from 1 to {DashboardLayout.Columns}. The last valid value was kept.", () => Model.Span = span);
            OnPropertyChanged(nameof(Text));
        }
    }

    public string HeightText
    {
        get => Shown(nameof(HeightText), Model.Height.ToString(CultureInfo.InvariantCulture));
        set
        {
            var text = value ?? string.Empty;
            var ok = WholeNumber(text, 0, 2000, out var height);
            AcceptText(nameof(HeightText), text, ok ? null : "Height must be a whole number of pixels from 0 (automatic) to 2000. The last valid value was kept.", () => Model.Height = height);
        }
    }

    /// <summary>The box label: the card caption, or the embedded report's caption.</summary>
    public string Label => ReportSchematicBuilder.ItemLabel(_designer.Owner.Project.Reports, Model);

    /// <summary>The list line: "KPI: Total orders (span 4)".</summary>
    public string Text => $"{(IsKpi ? "KPI" : "Report")}: {Label} (span {Model.Span})";

    internal void RaiseLabels()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Text));
    }
}

/// <summary>Designer of a Dashboard: the items with their settings, refresh interval and the live layout of the 12-column strip.</summary>
public sealed class DashboardReportViewModel : ReportDesignerBase
{
    public DashboardReportViewModel(ReportsViewModel owner, ReportDef report)
        : base(owner, report)
    {
        Items = new ModelRowList<DashboardItem, DashboardItemRow>(
            report.Dashboard.Items,
            this,
            i => new DashboardItemRow(i, this),
            r => r.Model,
            () => null);
        Items.Edited += (_, _) => OnPropertyChanged(nameof(HasSelectedItem));
        Items.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Items.Selected))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
            }
        };
        AddKpiCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(AddKpi);
        AddReportCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(AddReport);
        RefreshChoices();
        RefreshLayout();
    }

    public System.Windows.Input.ICommand AddKpiCommand { get; }

    public System.Windows.Input.ICommand AddReportCommand { get; }

    private DashboardSettings Model => Report.Dashboard;

    public ModelRowList<DashboardItem, DashboardItemRow> Items { get; }

    public bool HasSelectedItem => Items.Selected is not null;

    /// <summary>The items placed on the 12-column grid, wrapping like Bootstrap rows.</summary>
    public IReadOnlyList<DashboardBox> Boxes { get; private set; } = [];

    public ObservableCollection<string> EmbeddableReports { get; } = [];

    public ObservableCollection<string> LinkTargets { get; } = [];

    public IReadOnlyList<string> StyleNames => ContextStyles.Names;

    public IReadOnlyList<string> IconChoices => CustomizationChoices.Icons;

    public string RefreshSecondsText
    {
        get => Shown(nameof(RefreshSecondsText), Model.RefreshSeconds.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(RefreshSecondsText), value, "Refresh seconds", 0, 86400, n => Model.RefreshSeconds = n);
    }

    public void AddKpi() => AddItem(new DashboardItem
    {
        Kind = DashboardItemKind.Kpi,
        Caption = "New KPI",
        Table = Owner.TableNames.FirstOrDefault() ?? string.Empty,
        Span = 3,
        Style = "primary",
    });

    public void AddReport() => AddItem(new DashboardItem
    {
        Kind = DashboardItemKind.Report,
        Report = EmbeddableReports.FirstOrDefault() ?? string.Empty,
        Span = 6,
    });

    private void AddItem(DashboardItem item)
    {
        Model.Items.Add(item);
        var row = new DashboardItemRow(item, this);
        Items.Rows.Add(row);
        Items.Selected = row;
        Changed();
    }

    /// <summary>Re-reads the report and link pickers (the reports of the project may have changed).</summary>
    public void RefreshChoices()
    {
        ReportRowBase.SetItems(EmbeddableReports, Owner.DashboardReportNames(Report));
        ReportRowBase.SetItems(LinkTargets, [ReportFieldRules.NoneLabel, .. Owner.AllReportNames(Report), .. Owner.TableNames]);
    }

    private void RefreshLayout()
    {
        Boxes = DashboardLayout.Place(Items.Rows.Select(r => (r.Label, r.Model.Span, r.Model.Height)).ToList());
        OnPropertyChanged(nameof(Boxes));
        OnPropertyChanged(nameof(RowCount));
    }

    public int RowCount => DashboardLayout.RowCount(Boxes);

    protected override void Changed()
    {
        foreach (var row in Items.Rows)
        {
            row.RaiseLabels();
        }

        RefreshLayout();
        base.Changed();
    }
}
