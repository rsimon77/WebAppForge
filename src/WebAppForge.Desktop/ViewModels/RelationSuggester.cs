using WebAppForge.Desktop.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>A master/detail link proposed from a foreign key. <see cref="Exists"/> is true when the master already has that link.</summary>
public sealed record LinkSuggestion(
    TableInfo Master,
    string DetailTable,
    IReadOnlyList<(string MasterField, string DetailField)> Pairs,
    DeleteRule OnDelete,
    UpdateRule OnUpdate,
    bool Exists,
    string Label)
{
    /// <summary>Builds the link the suggestion stands for, with the defaults of a new link.</summary>
    public DetailLink ToLink()
    {
        var link = new DetailLink { DetailTable = DetailTable, OnDelete = OnDelete, OnUpdate = OnUpdate };
        foreach (var (masterField, detailField) in Pairs)
        {
            link.Links.Add(new KeyLink { MasterField = masterField, DetailField = detailField });
        }

        return link;
    }
}

/// <summary>
/// Proposes master/detail links from <see cref="TableInfo.ForeignKeys"/>: a foreign key on table D that references table M becomes
/// a detail link on M with D as the detail and the column pairs of the key.
/// </summary>
public static class RelationSuggester
{
    public static IReadOnlyList<LinkSuggestion> Suggest(IEnumerable<TableInfo> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var all = tables.ToList();
        var result = new List<LinkSuggestion>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in all)
        {
            foreach (var key in detail.ForeignKeys)
            {
                var master = RelationValidator.FindTable(all, key.ReferencedTable);
                if (master is null)
                {
                    continue;
                }

                var referenced = key.ReferencedColumns.Count > 0
                    ? key.ReferencedColumns.ToList()
                    : master.PrimaryKeys.Select(f => f.Name).ToList();
                if (key.Columns.Count == 0 || referenced.Count != key.Columns.Count)
                {
                    continue;
                }

                var pairs = referenced.Select((column, i) => (MasterField: column, DetailField: key.Columns[i])).ToList();
                var probe = new DetailLink { DetailTable = detail.Name };
                probe.Links.AddRange(pairs.Select(p => new KeyLink { MasterField = p.MasterField, DetailField = p.DetailField }));
                var duplicate = result.Any(s => ReferenceEquals(s.Master, master)
                    && RelationValidator.SameLink(s.ToLink(), probe));
                if (duplicate)
                {
                    continue;
                }

                var onDelete = DeleteRuleFrom(key.OnDelete);
                if (onDelete == DeleteRule.SetNull && pairs.Any(p => RelationValidator.FindField(detail, p.DetailField) is { IsRequired: true }))
                {
                    onDelete = DeleteRule.Restrict;
                }

                var onUpdate = Normalize(key.OnUpdate) == "CASCADE" ? UpdateRule.Cascade : UpdateRule.None;
                var label = $"{master.Name} -> {detail.Name} ({string.Join(", ", pairs.Select(p => $"{master.Name}.{p.MasterField} = {detail.Name}.{p.DetailField}"))})";
                if (onDelete != DeleteRule.Restrict)
                {
                    label += Localizer.Instance.Format("RelationSuggester_OnDelete", onDelete);
                }

                var unique = label;
                for (var n = 2; !labels.Add(unique); n++)
                {
                    unique = $"{label} #{n.ToString(CultureInfo.InvariantCulture)}";
                }

                result.Add(new LinkSuggestion(master, detail.Name, pairs, onDelete, onUpdate, master.Details.Any(l => RelationValidator.SameLink(l, probe)), unique));
            }
        }

        return result;
    }

    /// <summary>ON DELETE CASCADE gives Cascade and SET NULL gives Set null. RESTRICT, NO ACTION, SET DEFAULT and no rule give Restrict.</summary>
    public static DeleteRule DeleteRuleFrom(string? action) => Normalize(action) switch
    {
        "CASCADE" => DeleteRule.Cascade,
        "SET NULL" => DeleteRule.SetNull,
        _ => DeleteRule.Restrict,
    };

    private static string Normalize(string? action) => string.Join(' ', (action ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
