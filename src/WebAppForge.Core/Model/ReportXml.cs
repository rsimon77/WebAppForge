using System.Globalization;
using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of the reports: &lt;reports&gt;&lt;report name="" kind="" ...&gt; with &lt;group/&gt;, &lt;aggregate/&gt;,
/// &lt;column/&gt; and &lt;item/&gt; children. The element is omitted when there are no reports and attributes when default,
/// so older project files load unchanged.
/// </summary>
internal static class ReportXml
{
    private static readonly ReportInfo Defaults = new();

    public static void Append(XElement root, IReadOnlyList<ReportInfo> reports)
    {
        if (reports.Count == 0)
        {
            return;
        }

        var d = Defaults;
        var list = new XElement("reports");
        foreach (var report in reports)
        {
            var e = new XElement("report", new XAttribute("name", report.Name), new XAttribute("kind", report.Kind.ToString()));
            WriteStr(e, "caption", report.Caption, d.Caption);
            WriteBool(e, "generate", report.Generate, d.Generate);
            WriteStr(e, "table", report.SourceTable, d.SourceTable);
            WriteStr(e, "filter", report.Filter, d.Filter);
            WriteStr(e, "description", report.Description, d.Description);
            WriteInt(e, "minLevel", report.MinLevel, d.MinLevel);
            WriteBool(e, "showInMenu", report.ShowInMenu, d.ShowInMenu);
            WriteStr(e, "menuGroup", report.MenuGroup, d.MenuGroup);
            WriteStr(e, "dateFilterField", report.DateFilterField, d.DateFilterField);
            WriteBool(e, "drillDown", report.DrillDown, d.DrillDown);
            WriteBool(e, "showDetails", report.ShowDetails, d.ShowDetails);
            WriteBool(e, "showGrandTotal", report.ShowGrandTotal, d.ShowGrandTotal);
            WriteStr(e, "rowField", report.RowField, d.RowField);
            WriteEnum(e, "rowInterval", report.RowInterval, d.RowInterval);
            WriteStr(e, "columnField", report.ColumnField, d.ColumnField);
            WriteEnum(e, "columnInterval", report.ColumnInterval, d.ColumnInterval);
            WriteStr(e, "valueField", report.ValueField, d.ValueField);
            WriteEnum(e, "function", report.Function, d.Function);
            WriteInt(e, "maxColumns", report.MaxColumns, d.MaxColumns);
            WriteEnum(e, "chartType", report.ChartType, d.ChartType);
            WriteEnum(e, "chartOrder", report.ChartOrder, d.ChartOrder);
            WriteInt(e, "topCount", report.TopCount, d.TopCount);
            WriteBool(e, "showDataTable", report.ShowDataTable, d.ShowDataTable);
            WriteStr(e, "dateField", report.DateField, d.DateField);
            WriteStr(e, "endDateField", report.EndDateField, d.EndDateField);
            WriteStr(e, "titleField", report.TitleField, d.TitleField);
            WriteBool(e, "linkToView", report.LinkToView, d.LinkToView);
            WriteBool(e, "weekStartsMonday", report.WeekStartsMonday, d.WeekStartsMonday);

            foreach (var group in report.Groups)
            {
                var g = new XElement("group", new XAttribute("field", group.Field));
                WriteEnum(g, "interval", group.Interval, DateInterval.None);
                WriteBool(g, "descending", group.Descending, false);
                e.Add(g);
            }

            foreach (var aggregate in report.Aggregates)
            {
                var a = new XElement("aggregate", new XAttribute("function", aggregate.Function.ToString()));
                WriteStr(a, "field", aggregate.Field, string.Empty);
                WriteStr(a, "caption", aggregate.Caption, string.Empty);
                e.Add(a);
            }

            foreach (var column in report.DetailColumns)
            {
                e.Add(new XElement("column", new XAttribute("field", column)));
            }

            foreach (var filter in report.FilterFields)
            {
                e.Add(new XElement("filter", new XAttribute("field", filter)));
            }

            foreach (var item in report.Items)
            {
                e.Add(new XElement("item", new XAttribute("report", item.Report), new XAttribute("width", item.Width)));
            }

            list.Add(e);
        }

        root.Add(list);
    }

    public static void Read(XElement root, List<ReportInfo> reports)
    {
        if (root.Element("reports") is not { } list)
        {
            return;
        }

        var d = Defaults;
        foreach (var e in list.Elements("report"))
        {
            var report = new ReportInfo
            {
                Name = ReadStr(e, "name", string.Empty),
                Kind = ReadEnum(e, "kind", d.Kind),
                Caption = ReadStr(e, "caption", d.Caption),
                Generate = ReadBool(e, "generate", d.Generate),
                SourceTable = ReadStr(e, "table", d.SourceTable),
                Filter = ReadStr(e, "filter", d.Filter),
                Description = ReadStr(e, "description", d.Description),
                MinLevel = ReadInt(e, "minLevel", d.MinLevel),
                ShowInMenu = ReadBool(e, "showInMenu", d.ShowInMenu),
                MenuGroup = ReadStr(e, "menuGroup", d.MenuGroup),
                DateFilterField = ReadStr(e, "dateFilterField", d.DateFilterField),
                DrillDown = ReadBool(e, "drillDown", d.DrillDown),
                ShowDetails = ReadBool(e, "showDetails", d.ShowDetails),
                ShowGrandTotal = ReadBool(e, "showGrandTotal", d.ShowGrandTotal),
                RowField = ReadStr(e, "rowField", d.RowField),
                RowInterval = ReadEnum(e, "rowInterval", d.RowInterval),
                ColumnField = ReadStr(e, "columnField", d.ColumnField),
                ColumnInterval = ReadEnum(e, "columnInterval", d.ColumnInterval),
                ValueField = ReadStr(e, "valueField", d.ValueField),
                Function = ReadEnum(e, "function", d.Function),
                MaxColumns = ReadInt(e, "maxColumns", d.MaxColumns),
                ChartType = ReadEnum(e, "chartType", d.ChartType),
                ChartOrder = ReadEnum(e, "chartOrder", d.ChartOrder),
                TopCount = ReadInt(e, "topCount", d.TopCount),
                ShowDataTable = ReadBool(e, "showDataTable", d.ShowDataTable),
                DateField = ReadStr(e, "dateField", d.DateField),
                EndDateField = ReadStr(e, "endDateField", d.EndDateField),
                TitleField = ReadStr(e, "titleField", d.TitleField),
                LinkToView = ReadBool(e, "linkToView", d.LinkToView),
                WeekStartsMonday = ReadBool(e, "weekStartsMonday", d.WeekStartsMonday),
            };

            foreach (var g in e.Elements("group"))
            {
                report.Groups.Add(new ReportGroup
                {
                    Field = ReadStr(g, "field", string.Empty),
                    Interval = ReadEnum(g, "interval", DateInterval.None),
                    Descending = ReadBool(g, "descending", false),
                });
            }

            foreach (var a in e.Elements("aggregate"))
            {
                report.Aggregates.Add(new ReportAggregate
                {
                    Field = ReadStr(a, "field", string.Empty),
                    Function = ReadEnum(a, "function", AggregateFunction.Count),
                    Caption = ReadStr(a, "caption", string.Empty),
                });
            }

            foreach (var c in e.Elements("column"))
            {
                report.DetailColumns.Add(ReadStr(c, "field", string.Empty));
            }

            foreach (var f in e.Elements("filter"))
            {
                report.FilterFields.Add(ReadStr(f, "field", string.Empty));
            }

            foreach (var i in e.Elements("item"))
            {
                report.Items.Add(new DashboardItem { Report = ReadStr(i, "report", string.Empty), Width = ReadInt(i, "width", 6) });
            }

            reports.Add(report);
        }
    }

    private static void WriteStr(XElement e, string name, string value, string fallback)
    {
        if (!string.Equals(value, fallback, StringComparison.Ordinal))
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void WriteBool(XElement e, string name, bool value, bool fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void WriteInt(XElement e, string name, int value, int fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void WriteEnum<T>(XElement e, string name, T value, T fallback)
        where T : struct, System.Enum
    {
        if (!EqualityComparer<T>.Default.Equals(value, fallback))
        {
            e.Add(new XAttribute(name, value.ToString()));
        }
    }

    private static string ReadStr(XElement e, string name, string fallback) => e.Attribute(name)?.Value ?? fallback;

    private static bool ReadBool(XElement e, string name, bool fallback) =>
        bool.TryParse(e.Attribute(name)?.Value, out var value) ? value : fallback;

    private static int ReadInt(XElement e, string name, int fallback) =>
        int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static T ReadEnum<T>(XElement e, string name, T fallback)
        where T : struct, System.Enum =>
        System.Enum.TryParse<T>(e.Attribute(name)?.Value, out var value) && System.Enum.IsDefined(value) ? value : fallback;
}
