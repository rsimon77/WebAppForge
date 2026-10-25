using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.ViewModels;

public enum RelationSeverity
{
    /// <summary>The generator would fail or generate something wrong. Generate is disabled.</summary>
    Error,

    /// <summary>Allowed, but probably not what was meant.</summary>
    Warning,
}

/// <summary>One finding of <see cref="RelationValidator"/>. <see cref="Link"/> is set for findings about one master/detail link.</summary>
public sealed record RelationIssue(RelationSeverity Severity, string Table, string Message, DetailLink? Link = null)
{
    public bool IsError => Severity == RelationSeverity.Error;

    /// <summary>The message with its severity in front, as the validation list shows it.</summary>
    public string Text => (IsError ? Localizer.Instance["RelationValidator_Error"] : Localizer.Instance["RelationValidator_Warning"]) + Message;
}

/// <summary>
/// Checks master/detail links and multi-page forms of the open project. Table and field names compare case-insensitively
/// and only generated master tables are checked. Core validates again when it generates, so the messages are meant to be mirrored there.
/// </summary>
public static class RelationValidator
{
    public const int MinPageSize = 1;

    public const int MaxPageSize = 200;

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<RelationIssue> Validate(IEnumerable<TableInfo> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var all = tables.ToList();
        var issues = new List<RelationIssue>();
        foreach (var master in all.Where(t => t.Generate))
        {
            for (var index = 0; index < master.Details.Count; index++)
            {
                CheckLink(all, master, master.Details[index], index, issues);
            }

            CheckFormPages(master, issues);
        }

        CheckCascadeCycles(all, issues);
        return issues;
    }

    /// <summary>The findings about the form pages of one table (names, modes without pages, fields on missing pages).</summary>
    public static IReadOnlyList<RelationIssue> ValidateFormPages(TableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var issues = new List<RelationIssue>();
        CheckFormPages(table, issues);
        return issues;
    }

    /// <summary>The first error, or null. Warnings never block generation.</summary>
    public static string? FirstError(IEnumerable<TableInfo> tables)
        => Validate(tables).FirstOrDefault(i => i.IsError)?.Message;

    /// <summary>"Customers -> Orders", with a placeholder while no detail table is chosen.</summary>
    public static string LinkName(TableInfo master, DetailLink link)
        => $"{master.Name} -> {(string.IsNullOrWhiteSpace(link.DetailTable) ? Localizer.Instance["Relations_NoDetailTable"] : link.DetailTable)}";

    /// <summary>"Id = CustomerId" pairs joined with ", ".</summary>
    public static string PairsText(DetailLink link)
        => string.Join(", ", link.Links.Select(l => $"{l.MasterField} = {l.DetailField}"));

    /// <summary>True when both links relate the same detail table through the same set of pairs (order does not matter).</summary>
    public static bool SameLink(DetailLink a, DetailLink b)
        => Names.Equals(a.DetailTable, b.DetailTable) && PairSet(a).SetEquals(PairSet(b));

    public static TableInfo? FindTable(IEnumerable<TableInfo> tables, string? name)
        => string.IsNullOrWhiteSpace(name) ? null : tables.FirstOrDefault(t => Names.Equals(t.Name, name));

    public static FieldInfo? FindField(TableInfo? table, string? name)
        => table is null || string.IsNullOrWhiteSpace(name) ? null : table.Fields.FirstOrDefault(f => Names.Equals(f.Name, name));

    private static HashSet<string> PairSet(DetailLink link)
        => link.Links.Select(l => (l.MasterField + "\u0001" + l.DetailField).ToUpperInvariant()).ToHashSet();

    private static void CheckLink(List<TableInfo> all, TableInfo master, DetailLink link, int index, List<RelationIssue> issues)
    {
        var name = LinkName(master, link);

        void Add(RelationSeverity severity, string message)
            => issues.Add(new RelationIssue(severity, master.Name, Localizer.Instance.Format("RelationValidator_MasterDetail", name, message), link));

        void Error(string message) => Add(RelationSeverity.Error, message);
        void Warn(string message) => Add(RelationSeverity.Warning, message);

        TableInfo? detail = null;
        if (string.IsNullOrWhiteSpace(link.DetailTable))
        {
            Error(Localizer.Instance["RelationValidator_ChooseTheDetailTable"]);
        }
        else
        {
            detail = FindTable(all, link.DetailTable);
            if (detail is null)
            {
                Error(Localizer.Instance.Format("RelationValidator_TheDetailTableDoesNotExist", link.DetailTable));
            }
            else if (!detail.Generate)
            {
                Error(Localizer.Instance.Format("RelationValidator_TheDetailTableIsNotGenerated", link.DetailTable));
            }
        }

        if (link.Links.Count == 0)
        {
            Error("add at least one key link (master field = detail field).");
        }

        var seenPairs = new HashSet<string>();
        for (var i = 0; i < link.Links.Count; i++)
        {
            var pair = link.Links[i];
            if (string.IsNullOrWhiteSpace(pair.MasterField))
            {
                Error(Localizer.Instance.Format("RelationValidator_KeyLinkHasNoMasterField", (i + 1).ToString(CultureInfo.InvariantCulture)));
            }
            else if (FindField(master, pair.MasterField) is null)
            {
                Error(Localizer.Instance.Format("RelationValidator_TheMasterFieldDoesNotExist", pair.MasterField, master.Name));
            }

            if (string.IsNullOrWhiteSpace(pair.DetailField))
            {
                Error(Localizer.Instance.Format("RelationValidator_KeyLinkHasNoDetailField", (i + 1).ToString(CultureInfo.InvariantCulture)));
            }
            else if (detail is not null && FindField(detail, pair.DetailField) is null)
            {
                Error(Localizer.Instance.Format("RelationValidator_TheDetailFieldDoesNotExist", pair.DetailField, detail.Name));
            }

            if (!string.IsNullOrWhiteSpace(pair.MasterField) && !string.IsNullOrWhiteSpace(pair.DetailField)
                && !seenPairs.Add((pair.MasterField + "\u0001" + pair.DetailField).ToUpperInvariant()))
            {
                Error(Localizer.Instance.Format("RelationValidator_TheKeyLinkIsListedTwice", pair.MasterField, pair.DetailField));
            }

            var masterField = FindField(master, pair.MasterField);
            var detailField = FindField(detail, pair.DetailField);
            if (masterField is not null && detailField is not null)
            {
                if (masterField.Type != detailField.Type)
                {
                    Warn(Localizer.Instance.Format("RelationValidator_MasterFieldIsButDetailField", master.Name, masterField.Name, masterField.Type, detail!.Name, detailField.Name, detailField.Type));
                }

                if (ReferenceEquals(master, detail) && Names.Equals(masterField.Name, detailField.Name))
                {
                    Warn(Localizer.Instance.Format("RelationValidator_TheKeyLinkRelatesEveryRow", pair.MasterField, pair.DetailField));
                }
            }
        }

        for (var earlier = 0; earlier < index; earlier++)
        {
            if (link.Links.Count > 0 && SameLink(master.Details[earlier], link))
            {
                Error(Localizer.Instance.Format("RelationValidator_DuplicateOfAnotherLinkOfTo", master.Name, link.DetailTable, PairsText(link)));
                break;
            }
        }

        if (link.Links.Count > 0 && link.Links.Count < master.PrimaryKeys.Count
            && link.Links.All(l => FindField(master, l.MasterField) is { IsPrimaryKey: true }))
        {
            Warn(Localizer.Instance.Format("RelationValidator_LinksOfThePrimaryKeyColumns", link.Links.Count.ToString(CultureInfo.InvariantCulture), master.PrimaryKeys.Count.ToString(CultureInfo.InvariantCulture), master.Name));
        }

        if (detail is not null)
        {
            foreach (var column in link.Columns)
            {
                if (FindField(detail, column) is null)
                {
                    Error(Localizer.Instance.Format("RelationValidator_TheGridColumnDoesNotExist", column, detail.Name));
                }
            }
        }

        if (link.PageSize < MinPageSize || link.PageSize > MaxPageSize)
        {
            Error(Localizer.Instance.Format("RelationValidator_ThePageSizeMustBeFrom", link.PageSize.ToString(CultureInfo.InvariantCulture), MinPageSize, MaxPageSize));
        }

        if (!string.IsNullOrEmpty(link.FormPage) && !master.FormPages.Any(p => Names.Equals(p.Name, link.FormPage)))
        {
            Error(Localizer.Instance.Format("RelationValidator_TheGridIsPlacedOnForm", link.FormPage, master.Name));
        }

        if (link.OnDelete == DeleteRule.SetNull && detail is not null)
        {
            foreach (var pair in link.Links)
            {
                if (FindField(detail, pair.DetailField) is { } field && (field.IsRequired || field.IsPrimaryKey))
                {
                    Error(Localizer.Instance.Format("RelationValidator_OnDeleteSetNullNeedsNullable", field.Name, detail.Name));
                }
            }
        }

        if (link.OnUpdate == UpdateRule.Cascade && link.Links.Count > 0
            && link.Links.All(l => FindField(master, l.MasterField) is { } f && (f.IsPrimaryKey || f.IsAutoIncrement)))
        {
            Warn(Localizer.Instance["RelationValidator_CascadeOnUpdateOnlyMattersFor"]);
        }
    }

    private static void CheckFormPages(TableInfo table, List<RelationIssue> issues)
    {
        void Add(RelationSeverity severity, string message) => issues.Add(new RelationIssue(severity, table.Name, Localizer.Instance.Format("RelationValidator_Table", table.Name, message)));

        var seen = new HashSet<string>(Names);
        foreach (var page in table.FormPages)
        {
            if (string.IsNullOrWhiteSpace(page.Name))
            {
                Add(RelationSeverity.Error, Localizer.Instance["RelationValidator_AFormPageHasNoName"]);
            }
            else if (!seen.Add(page.Name))
            {
                Add(RelationSeverity.Error, Localizer.Instance.Format("RelationValidator_TheFormPageExistsTwice", page.Name));
            }
        }

        foreach (var (kind, design) in new[]
                 {
                     (PageKind.View, table.Designs.View), (PageKind.Add, table.Designs.Add),
                     (PageKind.Copy, table.Designs.Copy), (PageKind.Edit, table.Designs.Edit),
                 })
        {
            if (design.MultiPage != MultiPageMode.None && table.FormPages.Count == 0)
            {
                Add(RelationSeverity.Warning, Localizer.Instance.Format("RelationValidator_ThePageUsesFormPagesBut", kind, design.MultiPage));
            }
        }

        foreach (var field in table.Fields)
        {
            var page = field.Ui.FormPage;
            if (!string.IsNullOrEmpty(page) && !seen.Contains(page))
            {
                Add(RelationSeverity.Warning, Localizer.Instance.Format("RelationValidator_FieldIsOnFormPageWhich", field.Name, page));
            }
        }
    }

    /// <summary>Cascade delete links form a graph from master to detail. A loop in it would delete records forever.</summary>
    private static void CheckCascadeCycles(List<TableInfo> all, List<RelationIssue> issues)
    {
        var edges = new Dictionary<string, List<string>>(Names);
        foreach (var master in all.Where(t => t.Generate))
        {
            foreach (var link in master.Details.Where(l => l.OnDelete == DeleteRule.Cascade))
            {
                if (FindTable(all, link.DetailTable) is { Generate: true } detail)
                {
                    if (!edges.TryGetValue(master.Name, out var list))
                    {
                        edges[master.Name] = list = [];
                    }

                    list.Add(detail.Name);
                }
            }
        }

        var reported = new HashSet<string>(Names);
        foreach (var start in all.Where(t => t.Generate).Select(t => t.Name))
        {
            if (reported.Contains(start))
            {
                continue;
            }

            var path = FindCycle(start, edges);
            if (path is null)
            {
                continue;
            }

            foreach (var table in path)
            {
                reported.Add(table);
            }

            var message = path.Count == 1
                ? Localizer.Instance.Format("RelationValidator_CascadeDeleteCycleWouldDeleteIts", path[0], path[0], path[0])
                : Localizer.Instance.Format("RelationValidator_CascadeDeleteCycleLoop", string.Join(" -> ", path), path[0]);
            issues.Add(new RelationIssue(RelationSeverity.Error, path[0], message));
        }
    }

    /// <summary>A path that leaves <paramref name="start"/> and comes back to it, or null.</summary>
    private static List<string>? FindCycle(string start, Dictionary<string, List<string>> edges)
    {
        var stack = new List<string> { start };
        var visited = new HashSet<string>(Names);

        bool Walk(string node)
        {
            if (!edges.TryGetValue(node, out var next))
            {
                return false;
            }

            foreach (var target in next)
            {
                if (Names.Equals(target, start))
                {
                    return true;
                }

                if (visited.Add(target))
                {
                    stack.Add(target);
                    if (Walk(target))
                    {
                        return true;
                    }

                    stack.RemoveAt(stack.Count - 1);
                }
            }

            return false;
        }

        visited.Add(start);
        return Walk(start) ? stack : null;
    }
}
