using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebAppForge.Core.Model;
using FieldInfo = WebAppForge.Core.Model.FieldInfo;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// Copies every public property of one customization object into another (settings, lists of rules, nested objects),
/// so copy and reset keep the existing object references and the editor and the model stay in step.
/// </summary>
public static class DeepCopy
{
    public static void Into(object source, object target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var value = property.GetValue(source);
            if (value is IList list && property.PropertyType != typeof(string) && !property.PropertyType.IsArray)
            {
                if (property.GetValue(target) is IList targetList && !targetList.IsReadOnly)
                {
                    targetList.Clear();
                    foreach (var item in list)
                    {
                        targetList.Add(Clone(item));
                    }
                }

                continue;
            }

            if (!property.CanWrite)
            {
                continue;
            }

            if (IsSimple(property.PropertyType) || value is null)
            {
                property.SetValue(target, value);
                continue;
            }

            var current = property.GetValue(target);
            if (current is null)
            {
                current = Activator.CreateInstance(property.PropertyType)!;
                property.SetValue(target, current);
            }

            Into(value, current);
        }
    }

    /// <summary>A copy of a customization object (or the same value for strings, numbers and enums).</summary>
    public static object? Clone(object? item)
    {
        if (item is null || IsSimple(item.GetType()))
        {
            return item;
        }

        var copy = Activator.CreateInstance(item.GetType())!;
        Into(item, copy);
        return copy;
    }

    private static bool IsSimple(Type type)
        => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
           || type == typeof(DateTime) || Nullable.GetUnderlyingType(type) is not null;
}

/// <summary>
/// A text form of everything the customization pages edit. The project is dirty when it differs from the saved state,
/// whether or not the project file stores these settings yet.
/// </summary>
public static class CustomizationFingerprint
{
    public static string Compute(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var text = new StringBuilder();
        text.Append(project.ProjectId).Append(JsonSerializer.Serialize(project.Meta));
        foreach (var table in project.Tables)
        {
            // Designs include the multi-page mode; the field Ui below includes each field's form page.
            text.Append('|').Append(table.Name)
                .Append(JsonSerializer.Serialize(table.Ui))
                .Append(JsonSerializer.Serialize(table.Designs))
                .Append("#details").Append(JsonSerializer.Serialize(table.Details))
                .Append("#formpages").Append(JsonSerializer.Serialize(table.FormPages));
            foreach (var field in table.Fields)
            {
                text.Append('/').Append(field.Name).Append(JsonSerializer.Serialize(field.Ui));
            }
        }

        // The reports: every setting of every report (the report editor writes them straight into the model).
        text.Append("#reports").Append(JsonSerializer.Serialize(project.Reports));
        return text.ToString();
    }
}

public enum RegexTestOutcome
{
    NoPattern,
    InvalidPattern,
    Match,
    NoMatch,
    TimedOut,
}

public sealed record RegexTestResult(RegexTestOutcome Outcome, string Message);

/// <summary>Checks sample text against a validation pattern. Like the HTML pattern attribute, the whole value must match.</summary>
public static class RegexTester
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Null when the pattern compiles; otherwise why it does not.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            _ = new Regex(Anchored(pattern), RegexOptions.None, DefaultTimeout);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    public static RegexTestResult Test(string? pattern, string? sample, TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return new RegexTestResult(RegexTestOutcome.NoPattern, "Enter a pattern to test it.");
        }

        try
        {
            var regex = new Regex(Anchored(pattern), RegexOptions.None, timeout ?? DefaultTimeout);
            return regex.IsMatch(sample ?? string.Empty)
                ? new RegexTestResult(RegexTestOutcome.Match, "Matches")
                : new RegexTestResult(RegexTestOutcome.NoMatch, "Does not match");
        }
        catch (RegexMatchTimeoutException)
        {
            return new RegexTestResult(RegexTestOutcome.TimedOut, "The test took longer than 1 second and was stopped. Simplify the pattern.");
        }
        catch (ArgumentException ex)
        {
            return new RegexTestResult(RegexTestOutcome.InvalidPattern, $"Invalid pattern: {ex.Message}");
        }
    }

    private static string Anchored(string pattern) => $"^(?:{pattern})$";
}

/// <summary>The sample shown under the Display settings of a field, in en-US so it reads the same on every machine.</summary>
public static class FormatPreviewer
{
    public const string InvalidFormat = "Invalid format string";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTime SampleDate = new(2026, 3, 14, 15, 45, 30);

    public static ViewFormat Effective(FieldType type, ViewFormat format) => format != ViewFormat.Auto
        ? format
        : type switch
        {
            FieldType.Integer or FieldType.Decimal => ViewFormat.Number,
            FieldType.Boolean => ViewFormat.Boolean,
            FieldType.Date => ViewFormat.Date,
            FieldType.DateTime => ViewFormat.DateTime,
            _ => ViewFormat.Text,
        };

    public static string Preview(FieldType type, FieldUi ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var formatString = string.IsNullOrWhiteSpace(ui.FormatString) ? null : ui.FormatString;
        try
        {
            switch (Effective(type, ui.Format))
            {
                case ViewFormat.MultilineText:
                    return "Line one\nLine two";
                case ViewFormat.Number:
                    return type == FieldType.Integer
                        ? 1234567m.ToString(formatString ?? "N0", Culture)
                        : 1234.5678m.ToString(formatString ?? "N2", Culture);
                case ViewFormat.Currency:
                    return 1234.5m.ToString(formatString ?? "C2", Culture);
                case ViewFormat.Percent:
                    return 0.256m.ToString(formatString ?? "P1", Culture);
                case ViewFormat.Date:
                    return SampleDate.ToString(formatString ?? "d", Culture);
                case ViewFormat.DateTime:
                    return SampleDate.ToString(formatString ?? "g", Culture);
                case ViewFormat.Time:
                    return SampleDate.ToString(formatString ?? "t", Culture);
                case ViewFormat.Boolean:
                    return $"{(string.IsNullOrEmpty(ui.TrueText) ? "Yes" : ui.TrueText)} / {(string.IsNullOrEmpty(ui.FalseText) ? "No" : ui.FalseText)}";
                case ViewFormat.Image:
                    return "[image] /images/sample.png";
                case ViewFormat.Link:
                    return "https://example.com";
                case ViewFormat.EmailLink:
                    return "user@example.com";
                case ViewFormat.Badge:
                    return ui.BadgeRules.Count == 0
                        ? "No badge rules yet"
                        : $"[{ui.BadgeRules[0].Style}] {(string.IsNullOrEmpty(ui.BadgeRules[0].Text) ? ui.BadgeRules[0].Value : ui.BadgeRules[0].Text)}";
                default:
                    return "Sample text";
            }
        }
        catch (FormatException)
        {
            return InvalidFormat;
        }
    }
}

public sealed record SchematicField(string Caption, string Control, string Group);

/// <summary>What the schematic preview draws for one page type. Plain data so it can be tested without a window.</summary>
public sealed record PageSchematic(
    PageKind Kind,
    string Heading,
    string Subtitle,
    bool HasHeaderHtml,
    bool HasFooterHtml,
    PageContainer Container,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<SchematicField> Fields,
    FormLayout FormLayout,
    int FormColumns,
    IReadOnlyList<string> TopButtons,
    IReadOnlyList<string> BottomButtons,
    ListLayout ListLayout,
    bool Striped,
    bool Hover,
    bool Bordered,
    bool StickyHeader,
    bool ShowPager,
    string EmptyText,
    string Note)
{
    /// <summary>The form split into pages (tabs, pills, accordion or wizard). Null for a single page form.</summary>
    public MultiPageSchematic? MultiPage { get; init; }

    /// <summary>Detail grids drawn below the form (links without a form page, or all links when the form is not split).</summary>
    public IReadOnlyList<SchematicGrid> DetailGrids { get; init; } = [];
}

/// <summary>A detail grid placeholder: a heading, the column headers and whether "Add" is offered.</summary>
public sealed record SchematicGrid(string Caption, string DetailTable, IReadOnlyList<string> Headers, bool CanAdd);

/// <summary>One page of a multi-page form with its field boxes and the detail grids that sit on it.</summary>
public sealed record SchematicPage(string Name, string Caption, string Icon, string Description, IReadOnlyList<SchematicField> Fields, IReadOnlyList<SchematicGrid> Grids);

public sealed record MultiPageSchematic(MultiPageMode Mode, IReadOnlyList<SchematicPage> Pages);

public static class SchematicBuilder
{
    private const int MaxColumns = 6;
    private const int MaxFields = 8;

    public static PageSchematic Build(TableInfo table, PageKind kind) => Build(table, kind, null);

    /// <param name="allTables">The tables of the project, used to name the columns of detail grids. Null draws the grids with their own columns only.</param>
    public static PageSchematic Build(TableInfo table, PageKind kind, IEnumerable<TableInfo>? allTables)
    {
        ArgumentNullException.ThrowIfNull(table);
        var design = PageRules.DesignOf(table, kind);
        var name = string.IsNullOrWhiteSpace(table.Caption) ? table.Name : table.Caption;

        var heading = !string.IsNullOrWhiteSpace(design.Title)
            ? design.Title
            : kind == PageKind.List ? name : $"{kind} {name}";

        var headers = new List<string>();
        var rows = new List<IReadOnlyList<string>>();
        var fields = new List<SchematicField>();
        var top = new List<string>();
        var bottom = new List<string>();
        var note = string.Empty;

        if (kind == PageKind.List)
        {
            var columns = ListFields(table, design).Take(MaxColumns).ToList();
            if (design.ShowRowNumbers)
            {
                headers.Add("#");
            }

            if (design.ShowActionsFirst)
            {
                headers.Add("Actions");
            }

            headers.AddRange(columns.Select(Caption));
            if (!design.ShowActionsFirst)
            {
                headers.Add("Actions");
            }

            for (var row = 1; row <= 3; row++)
            {
                var cells = new List<string>();
                if (design.ShowRowNumbers)
                {
                    cells.Add(row.ToString(CultureInfo.InvariantCulture));
                }

                if (design.ShowActionsFirst)
                {
                    cells.Add("View Edit Delete");
                }

                cells.AddRange(columns.Select(f => SampleValue(f, row)));
                if (!design.ShowActionsFirst)
                {
                    cells.Add("View Edit Delete");
                }

                rows.Add(cells);
            }

            top.Add(string.IsNullOrWhiteSpace(design.AddButtonText) ? "Add" : design.AddButtonText);
            top.Add("Search");
        }
        else
        {
            fields.AddRange(FormFields(table, kind).Take(MaxFields).Select(f =>
                new SchematicField(Caption(f), EffectiveControl(f), f.Ui.Group)));

            if (PageRules.IsFormPage(kind))
            {
                var buttons = new List<string>
                {
                    !string.IsNullOrWhiteSpace(design.SubmitText) ? design.SubmitText : DefaultSubmit(kind),
                };
                if (design.ShowCancel)
                {
                    buttons.Add(string.IsNullOrWhiteSpace(design.CancelText) ? "Cancel" : design.CancelText);
                }

                if (design.Buttons is ButtonPlacement.Top or ButtonPlacement.Both)
                {
                    top.AddRange(buttons);
                }

                if (design.Buttons is ButtonPlacement.Bottom or ButtonPlacement.Both)
                {
                    bottom.AddRange(buttons);
                }
            }
            else if (kind == PageKind.View)
            {
                bottom.AddRange(["Edit", "Back"]);
            }
            else
            {
                note = "Are you sure you want to delete this record?";
                bottom.AddRange(["Delete", "Cancel"]);
            }
        }

        var columnsInForm = design.FormLayout switch
        {
            FormLayout.TwoColumns => 2,
            FormLayout.ThreeColumns => 3,
            _ => 1,
        };

        MultiPageSchematic? multi = null;
        IReadOnlyList<SchematicGrid> below = [];
        if (kind is PageKind.View or PageKind.Add or PageKind.Copy or PageKind.Edit)
        {
            var grids = DetailGrids(table, kind, allTables?.ToList() ?? []);
            if (design.MultiPage != MultiPageMode.None && table.FormPages.Count > 0)
            {
                multi = BuildPages(table, kind, design.MultiPage, grids);
                below = grids.Where(g => PageIndex(table, g.Link.FormPage) < 0).Select(g => g.Grid).ToList();
            }
            else
            {
                below = grids.Select(g => g.Grid).ToList();
            }

            if (kind == PageKind.Add && table.Details.Any(l => l.ShowOnAdd))
            {
                note = "After saving, the edit page opens so detail rows can be added.";
            }
        }

        return new PageSchematic(
            kind,
            heading,
            design.Subtitle ?? string.Empty,
            !string.IsNullOrWhiteSpace(design.HeaderHtml),
            !string.IsNullOrWhiteSpace(design.FooterHtml),
            design.Container,
            headers,
            rows,
            fields,
            design.FormLayout,
            PageRules.IsFormPage(kind) ? columnsInForm : 1,
            top,
            bottom,
            design.ListLayout,
            design.Striped,
            design.Hover,
            design.Bordered,
            design.StickyHeader,
            design.ShowPager,
            string.IsNullOrWhiteSpace(design.EmptyText) ? "No records found." : design.EmptyText,
            note)
        {
            MultiPage = multi,
            DetailGrids = below,
        };
    }

    /// <summary>Index of the form page called <paramref name="name"/>, or -1 when it does not exist (or the name is empty).</summary>
    public static int PageIndex(TableInfo table, string? name)
        => string.IsNullOrEmpty(name)
            ? -1
            : table.FormPages.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static MultiPageSchematic BuildPages(TableInfo table, PageKind kind, MultiPageMode mode, List<(DetailLink Link, SchematicGrid Grid)> grids)
    {
        var fields = FormFields(table, kind).ToList();
        var pages = new List<SchematicPage>();
        for (var index = 0; index < table.FormPages.Count; index++)
        {
            var def = table.FormPages[index];
            var here = fields
                .Where(f => Math.Max(PageIndex(table, f.Ui.FormPage), 0) == index)
                .Take(MaxFields)
                .Select(f => new SchematicField(Caption(f), EffectiveControl(f), f.Ui.Group))
                .ToList();
            var hosted = grids.Where(g => PageIndex(table, g.Link.FormPage) == index).Select(g => g.Grid).ToList();
            pages.Add(new SchematicPage(
                def.Name,
                string.IsNullOrWhiteSpace(def.Caption) ? def.Name : def.Caption,
                def.Icon,
                def.Description,
                here,
                hosted));
        }

        return new MultiPageSchematic(mode, pages);
    }

    /// <summary>The detail grids of the master page: View shows the links with "show on view", Edit those with "show on edit". Add and Copy show none.</summary>
    private static List<(DetailLink Link, SchematicGrid Grid)> DetailGrids(TableInfo table, PageKind kind, List<TableInfo> allTables)
    {
        var result = new List<(DetailLink, SchematicGrid)>();
        if (kind is not (PageKind.View or PageKind.Edit))
        {
            return result;
        }

        foreach (var link in table.Details.Where(l => kind == PageKind.View ? l.ShowOnView : l.ShowOnEdit))
        {
            var detail = allTables.FirstOrDefault(t => string.Equals(t.Name, link.DetailTable, StringComparison.OrdinalIgnoreCase));
            var headers = new List<string>();
            if (link.Columns.Count > 0)
            {
                headers.AddRange(link.Columns.Take(4).Select(c => detail?.Fields.FirstOrDefault(f => string.Equals(f.Name, c, StringComparison.OrdinalIgnoreCase)) is { } f ? Caption(f) : c));
            }
            else if (detail is not null)
            {
                headers.AddRange(detail.Fields.Where(f => f.ShowInList).Take(4).Select(Caption));
            }

            if (headers.Count == 0)
            {
                headers.Add("(columns)");
            }

            if (kind == PageKind.Edit && (link.AllowEdit || link.AllowDelete))
            {
                headers.Add("Actions");
            }

            var caption = !string.IsNullOrWhiteSpace(link.Caption)
                ? link.Caption
                : detail is not null && !string.IsNullOrWhiteSpace(detail.Caption) ? detail.Caption
                : string.IsNullOrWhiteSpace(link.DetailTable) ? "(detail table)" : link.DetailTable;
            result.Add((link, new SchematicGrid(caption, link.DetailTable, headers, kind == PageKind.Edit && link.AllowAdd)));
        }

        return result;
    }

    /// <summary>The columns of the list: the visible entries of the column editor, or every field shown in the list.</summary>
    public static IEnumerable<FieldInfo> ListFields(TableInfo table, PageDesign design)
    {
        if (design.Columns.Count == 0)
        {
            return table.Fields.Where(f => f.ShowInList);
        }

        return design.Columns
            .Where(c => c.Visible)
            .Select(c => table.Fields.FirstOrDefault(f => string.Equals(f.Name, c.Field, StringComparison.OrdinalIgnoreCase)))
            .OfType<FieldInfo>();
    }

    public static IEnumerable<FieldInfo> FormFields(TableInfo table, PageKind kind) => kind switch
    {
        PageKind.Add or PageKind.Copy => table.Fields.Where(f => f.ShowInAdd && !f.IsAutoIncrement && f.Ui.Control != EditControl.Hidden),
        PageKind.Edit => table.Fields.Where(f => f.ShowInEdit && f.Ui.Control != EditControl.Hidden),
        PageKind.Search => table.Fields.Where(f => f.Searchable),
        PageKind.View or PageKind.Delete => table.Fields.Where(f => f.ShowInView),
        _ => [],
    };

    public static string EffectiveControl(FieldInfo field)
    {
        if (field.Ui.Control != EditControl.Auto)
        {
            return field.Ui.Control.ToString();
        }

        return field.Type switch
        {
            FieldType.Integer or FieldType.Decimal => "Number",
            FieldType.Boolean => "Checkbox",
            FieldType.Date => "Date",
            FieldType.DateTime => "DateTime",
            FieldType.Binary => "ReadOnly",
            _ => "TextBox",
        };
    }

    private static string DefaultSubmit(PageKind kind) => kind switch
    {
        PageKind.Add => "Add",
        PageKind.Copy => "Copy",
        PageKind.Edit => "Save",
        _ => "Search",
    };

    private static string Caption(FieldInfo field) => string.IsNullOrWhiteSpace(field.Caption) ? field.Name : field.Caption;

    private static string SampleValue(FieldInfo field, int row) => field.Type switch
    {
        FieldType.Integer => (row * 10).ToString(CultureInfo.InvariantCulture),
        FieldType.Decimal => $"{row * 12}.50",
        FieldType.Boolean => row % 2 == 1 ? "Yes" : "No",
        FieldType.Date => $"2026-03-{10 + row}",
        FieldType.DateTime => $"2026-03-{10 + row} 09:30",
        _ => $"{field.Name} {row}",
    };
}
