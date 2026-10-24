using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>Designer of a Summary report: groups, aggregates, layout, detail fields, totals, paging and filters.</summary>
public sealed class SummaryReportViewModel : ReportDesignerBase
{
    public SummaryReportViewModel(ReportsViewModel owner, ReportDef report)
        : base(owner, report)
    {
        var summary = report.Summary;
        Groups = new ModelRowList<ReportField, ReportFieldRow>(
            summary.Groups,
            this,
            g => new ReportFieldRow(g, this),
            r => r.Model,
            () => NextGroup());
        Aggregates = new ModelRowList<ReportAggregate, AggregateRow>(
            summary.Aggregates,
            this,
            a => new AggregateRow(a, this),
            r => r.Model,
            () => new ReportAggregate { Function = AggregateFunction.Count });
        DetailFields = new FieldNameList(summary.DetailFields, this);
        Filters = new ReportFilterList(summary.Filters, this);
    }

    private SummarySettings Model => Report.Summary;

    public ModelRowList<ReportField, ReportFieldRow> Groups { get; }

    public ModelRowList<ReportAggregate, AggregateRow> Aggregates { get; }

    public FieldNameList DetailFields { get; }

    public ReportFilterList Filters { get; }

    public SummaryLayout Layout
    {
        get => Model.Layout;
        set
        {
            if (Put(Model.Layout, value, v => Model.Layout = v))
            {
                OnPropertyChanged(nameof(IsDetailLayout));
            }
        }
    }

    /// <summary>Detail fields only apply to the Detail layout.</summary>
    public bool IsDetailLayout => Model.Layout == SummaryLayout.Detail;

    public bool ShowGroupTotals
    {
        get => Model.ShowGroupTotals;
        set => Put(Model.ShowGroupTotals, value, v => Model.ShowGroupTotals = v);
    }

    public bool ShowGrandTotal
    {
        get => Model.ShowGrandTotal;
        set => Put(Model.ShowGrandTotal, value, v => Model.ShowGrandTotal = v);
    }

    public string GroupsPerPageText
    {
        get => Shown(nameof(GroupsPerPageText), Model.GroupsPerPage.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(GroupsPerPageText), value, "Groups per page", 0, 10000, n => Model.GroupsPerPage = n);
    }

    public string DetailRowsPerGroupText
    {
        get => Shown(nameof(DetailRowsPerGroupText), Model.DetailRowsPerGroup.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(DetailRowsPerGroupText), value, "Detail rows per group", 0, 10000, n => Model.DetailRowsPerGroup = n);
    }

    /// <summary>The next field of the table that is not a group yet.</summary>
    private ReportField? NextGroup()
    {
        var used = Model.Groups.Select(g => g.Field).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var next = ReportFieldRules.FieldsFor(Table, FieldRole.Group).FirstOrDefault(n => !used.Contains(n));
        if (next is null)
        {
            Notice(Table is null ? "Choose the source table first." : "Every field is already a group.");
            return null;
        }

        Notice(string.Empty);
        return new ReportField { Field = next };
    }
}

/// <summary>Designer of a Crosstab report: row and column fields, the value, totals, the column limit and filters.</summary>
public sealed class CrosstabReportViewModel : ReportDesignerBase
{
    public CrosstabReportViewModel(ReportsViewModel owner, ReportDef report)
        : base(owner, report)
    {
        Rows = new ReportFieldRow(report.Crosstab.Rows, this);
        Columns = new ReportFieldRow(report.Crosstab.Columns, this);
        Value = new AggregateRow(report.Crosstab.Value, this);
        Filters = new ReportFilterList(report.Crosstab.Filters, this);
    }

    private CrosstabSettings Model => Report.Crosstab;

    public ReportFieldRow Rows { get; }

    public ReportFieldRow Columns { get; }

    public AggregateRow Value { get; }

    public ReportFilterList Filters { get; }

    public bool ShowRowTotals
    {
        get => Model.ShowRowTotals;
        set => Put(Model.ShowRowTotals, value, v => Model.ShowRowTotals = v);
    }

    public bool ShowColumnTotals
    {
        get => Model.ShowColumnTotals;
        set => Put(Model.ShowColumnTotals, value, v => Model.ShowColumnTotals = v);
    }

    public bool ShowGrandTotal
    {
        get => Model.ShowGrandTotal;
        set => Put(Model.ShowGrandTotal, value, v => Model.ShowGrandTotal = v);
    }

    public string MaxColumnsText
    {
        get => Shown(nameof(MaxColumnsText), Model.MaxColumns.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(MaxColumnsText), value, "Maximum columns", 0, 1000, n => Model.MaxColumns = n);
    }
}
