using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Reports;

/// <summary>
/// Turns the reports of a project into SQL for SQLite. Identifiers come from the project only (quoted); every value of a request
/// (filters, owner, clicked keys, paging) is a parameter. The SQL is built by <see cref="ReportEngine"/>, the same code the
/// generated sites run, so what the editor previews is what the site shows.
/// </summary>
public sealed class ReportQueryBuilder(Project project)
{
    public Project Project { get; } = project ?? throw new ArgumentNullException(nameof(project));

    // ---- descriptors ----------------------------------------------------------------------------

    /// <summary>True when the project hides this field from reports: secret fields and the password column of the user table.</summary>
    internal static bool IsHidden(Project project, TableInfo table, FieldInfo field) =>
        field.IsSecret
        || (project.Security.Enabled
            && string.Equals(table.Name, project.Security.UserTable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(field.Name, project.Security.PasswordField, StringComparison.OrdinalIgnoreCase));

    public TableInfo Table(string name) =>
        Project.Tables.FirstOrDefault(t => t.Generate && string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"The table '{name}' does not exist or is not generated.");

    public FieldInfo Field(TableInfo table, string name) =>
        table.Fields.FirstOrDefault(f => string.Equals(f.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase) && !IsHidden(Project, table, f))
        ?? throw new InvalidOperationException($"The field '{name}' does not exist in table '{table.Name}'.");

    public static SpecKind KindOf(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Decimal => SpecKind.Number,
        FieldType.Boolean => SpecKind.Boolean,
        FieldType.Date or FieldType.DateTime => SpecKind.Date,
        _ => SpecKind.Text,
    };

    public SpecColumn Column(TableInfo table, FieldInfo field)
    {
        var ui = field.Ui;
        var format = ui.Format switch
        {
            ViewFormat.Number => "number",
            ViewFormat.Currency => "currency",
            ViewFormat.Percent => "percent",
            ViewFormat.Date => "date",
            ViewFormat.DateTime => "datetime",
            ViewFormat.Time => "time",
            ViewFormat.Boolean => "boolean",
            _ => field.Type switch
            {
                FieldType.Date => "date",
                FieldType.DateTime => "datetime",
                _ => string.Empty,
            },
        };
        return new SpecColumn
        {
            Name = field.Name,
            Column = Templates.QuoteIdent(field.Name),
            Caption = field.Caption.Length > 0 ? field.Caption : field.Name,
            Kind = KindOf(field.Type),
            Format = format,
            FormatString = ui.FormatString,
            TrueText = ui.TrueText,
            FalseText = ui.FalseText,
        };
    }

    public SpecColumn Column(TableInfo table, string field) => Column(table, Field(table, field));

    /// <summary>The source of a table: its filter, the extra conditions, the owner column (only when security is on) and the detail order.</summary>
    public SpecSource Source(TableInfo table, params string[] conditions)
    {
        var owner = Project.Security.Enabled && table.OwnerField.Length > 0
            ? table.Fields.FirstOrDefault(f => string.Equals(f.Name, table.OwnerField, StringComparison.OrdinalIgnoreCase))
            : null;
        string order;
        if (table.Sort.Count > 0)
        {
            order = string.Join(", ", table.Sort.Select(s => Templates.QuoteIdent(s.Field) + (s.Descending ? " DESC" : " ASC")));
        }
        else
        {
            order = string.Join(", ", table.PrimaryKeys.Select(k => Templates.QuoteIdent(k.Name) + " ASC"));
        }

        return new SpecSource
        {
            Table = Templates.QuoteIdent(table.Name),
            Conditions = new[] { table.Filter }.Concat(conditions).Where(c => c.Trim().Length > 0).ToList(),
            OwnerColumn = owner is null ? string.Empty : Templates.QuoteIdent(owner.Name),
            Order = order,
            Columns = table.Fields.Where(f => f.Type != FieldType.Binary && !IsHidden(Project, table, f)).Select(f => Column(table, f)).ToList(),
        };
    }

    public SpecSource Source(ReportDef report) => Source(Table(report.Table), report.Filter);

    public SpecGroup Group(TableInfo table, ReportField field) => new()
    {
        Field = Column(table, field.Field),
        Interval = Enum.Parse<SpecInterval>(field.Interval.ToString()),
        RangeSize = field.RangeSize,
        Descending = field.Sort == SortDirection.Descending,
        ByAggregate = field.SortBy == GroupSortBy.Aggregate,
        Caption = ReportCaptions.Field(field, table),
    };

    public SpecAggregate Aggregate(TableInfo table, ReportAggregate aggregate) => new()
    {
        Field = aggregate.Field.Trim().Length == 0 ? null : Column(table, aggregate.Field),
        Function = Enum.Parse<SpecFunction>(aggregate.Function.ToString()),
        Caption = ReportCaptions.Aggregate(aggregate, table),
        FormatString = aggregate.FormatString,
    };

    public IReadOnlyList<SpecFilter> Filters(TableInfo table, IEnumerable<ReportFilter> filters) => filters
        .Where(f => f.Field.Trim().Length > 0)
        .Select(f =>
        {
            var column = Column(table, f.Field);
            return new SpecFilter
            {
                Field = column,
                Caption = f.Caption.Length > 0 ? f.Caption : column.Caption,
                Relative = f.RelativeDates && column.Kind == SpecKind.Date,
            };
        })
        .ToList();

    public SpecSummary Summary(ReportDef report)
    {
        var table = Table(report.Table);
        var s = report.Summary;
        var detail = s.DetailFields.Count > 0
            ? s.DetailFields.Select(f => Column(table, f)).ToList()
            : table.Fields.Where(f => f.ShowInList && f.Type != FieldType.Binary && !IsHidden(Project, table, f) && !(Project.Security.Enabled && string.Equals(f.Name, table.OwnerField, StringComparison.OrdinalIgnoreCase))).Select(f => Column(table, f)).ToList();
        return new SpecSummary
        {
            Source = Source(report),
            Groups = s.Groups.Select(g => Group(table, g)).ToList(),
            Aggregates = s.Aggregates.Select(a => Aggregate(table, a)).ToList(),
            DetailColumns = detail,
            Compact = s.Layout == SummaryLayout.Compact,
            ShowGroupTotals = s.ShowGroupTotals,
            ShowGrandTotal = s.ShowGrandTotal,
            GroupsPerPage = s.GroupsPerPage,
            DetailRowsPerGroup = s.DetailRowsPerGroup,
            Filters = Filters(table, s.Filters),
        };
    }

    public SpecCrosstab Crosstab(ReportDef report)
    {
        var table = Table(report.Table);
        var c = report.Crosstab;
        return new SpecCrosstab
        {
            Source = Source(report),
            Rows = Group(table, c.Rows),
            Columns = Group(table, c.Columns),
            Value = Aggregate(table, c.Value),
            ShowRowTotals = c.ShowRowTotals,
            ShowColumnTotals = c.ShowColumnTotals,
            ShowGrandTotal = c.ShowGrandTotal,
            MaxColumns = c.MaxColumns,
            Filters = Filters(table, c.Filters),
        };
    }

    /// <summary>A fresh request for a report: set the owner (the signed-in user name) when the source table has an owner field.</summary>
    public SpecRequest Request(string? owner = null) => new() { Owner = owner };

    /// <summary>True when the source table of the report is limited to its owner (security on and an owner field set).</summary>
    public bool NeedsOwner(ReportDef report) => Source(report).OwnerColumn.Length > 0;

    // ---- summary --------------------------------------------------------------------------------

    /// <summary>
    /// The groups at <paramref name="level"/> (1 = outermost; 0 = the grand total row): columns _g0.. then _a0...
    /// Ordered by the group at that level (its value or the first aggregate). <paramref name="limit"/> 0 = all.
    /// </summary>
    public ReportPlan SummaryGroups(ReportDef report, SpecRequest request, int level, int limit = 0, int offset = 0, IReadOnlyList<string>? firstGroupKeys = null)
    {
        var spec = Summary(report);
        return ReportEngine.Groups(spec.Source, spec.Filters, request, spec.Groups, level, spec.Aggregates, firstGroupKeys, limit, offset);
    }

    public ReportPlan SummaryTotal(ReportDef report, SpecRequest request) => SummaryGroups(report, request, 0);

    public ReportPlan SummaryCountGroups(ReportDef report, SpecRequest request, int level)
    {
        var spec = Summary(report);
        return ReportEngine.CountGroups(spec.Source, spec.Filters, request, spec.Groups, level);
    }

    /// <summary>The detail rows of one group, given the keys of its group values (outermost first).</summary>
    public ReportPlan SummaryDetail(ReportDef report, SpecRequest request, IReadOnlyList<string> groupKeys, int limit = 0)
    {
        var spec = Summary(report);
        var constraints = groupKeys.Select((key, i) => new SpecConstraint(spec.Groups[i], key)).ToList();
        return ReportEngine.Detail(spec.Source, spec.Filters, request, constraints, spec.DetailColumns, limit);
    }

    // ---- crosstab -------------------------------------------------------------------------------

    /// <summary>A crosstab query (see <see cref="ReportEngine.Crosstab"/>). <paramref name="topColumns"/> folds the other columns into "Other".</summary>
    public ReportPlan Crosstab(ReportDef report, SpecRequest request, CrosstabPart part, IReadOnlyList<string>? topColumns = null) =>
        ReportEngine.Crosstab(Crosstab(report), request, part, topColumns);

    // ---- series, KPI, rows (charts, dashboards, calendars) --------------------------------------

    /// <summary>
    /// The data of a chart. Without a series field: category in _g0 and one aggregate per series (_a0...), ordered by the category and
    /// cut to TopN groups. With a series field: the crosstab cells (_r category, _c series, _v value of the first series).
    /// </summary>
    public ReportPlan Series(ReportDef report, SpecRequest request)
    {
        var table = Table(report.Table);
        var c = report.Chart;
        var source = Source(report);
        var filters = Filters(table, c.Filters);
        var category = Group(table, c.Category);
        if (c.SeriesField is { } seriesField)
        {
            var x = new SpecCrosstab
            {
                Source = source,
                Rows = category,
                Columns = Group(table, seriesField),
                Value = Aggregate(table, c.Series.Count > 0 ? c.Series[0].Value : new ReportAggregate()),
                Filters = filters,
            };
            return ReportEngine.Crosstab(x, request, CrosstabPart.Cells);
        }

        var aggregates = c.Series.Select(s => Aggregate(table, s.Value)).ToList();
        return ReportEngine.Groups(source, filters, request, [category], 1, aggregates, null, Math.Max(0, c.TopN), 0);
    }

    /// <summary>One aggregate over a table (a dashboard KPI): a single row with the value in _a0.</summary>
    public ReportPlan Kpi(DashboardItem item, SpecRequest request)
    {
        var table = Table(item.Table);
        var source = Source(table, item.Filter);
        return ReportEngine.Groups(source, [], request, [], 0, [Aggregate(table, item.Value)]);
    }

    /// <summary>Rows of a report's table with the given fields, in the table order (calendar events, previews).</summary>
    public ReportPlan Rows(ReportDef report, SpecRequest request, IEnumerable<string> fields, int limit = 0)
    {
        var table = Table(report.Table);
        var filters = report.Type switch
        {
            ReportType.Calendar => Filters(table, report.Calendar.Filters),
            ReportType.Summary => Filters(table, report.Summary.Filters),
            ReportType.Crosstab => Filters(table, report.Crosstab.Filters),
            ReportType.Chart => Filters(table, report.Chart.Filters),
            _ => [],
        };
        return ReportEngine.Rows(Source(report), filters, request, fields.Select(f => Column(table, f)).ToList(), limit);
    }

    /// <summary>A clicked-value constraint on a field of the report (for drill-down and group detail).</summary>
    public SpecConstraint Constraint(ReportDef report, ReportField field, string key) => new(Group(Table(report.Table), field), key);
}
