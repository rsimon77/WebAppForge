using System.Linq;
using Avalonia.Data.Converters;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>Short read-only texts about a table for the Tables grid and its inspector.</summary>
public static class TableSummary
{
    /// <summary>The key columns, "A + B" for a composite key, or "No key".</summary>
    public static string KeyText(TableInfo table)
    {
        var keys = table.PrimaryKeys;
        return keys.Count == 0 ? "No key" : string.Join(" + ", keys.Select(k => k.Name));
    }

    /// <summary>The initials of the generated pages, in page order: List, View, Add, Copy, Edit, Delete, Search.</summary>
    public static string PagesText(TableInfo table)
    {
        if (!table.Generate)
        {
            return "Not generated";
        }

        var p = table.Pages;
        var on = new[] { (p.List, "L"), (p.View, "V"), (p.Add, "A"), (p.Copy, "C"), (p.Edit, "E"), (p.Delete, "D"), (p.Search, "S") }
            .Where(x => x.Item1).Select(x => x.Item2).ToList();
        return on.Count == 0 ? "None" : string.Join(" ", on);
    }

    /// <summary>"14 fields, key OrderID, 2 detail links".</summary>
    public static string Subtitle(TableInfo table)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            table.Fields.Count == 1 ? "1 field" : $"{table.Fields.Count} fields",
            table.PrimaryKeys.Count == 0 ? "no key" : "key " + KeyText(table),
        };
        if (table.Details.Count > 0)
        {
            parts.Add(table.Details.Count == 1 ? "1 detail link" : $"{table.Details.Count} detail links");
        }

        return string.Join(", ", parts);
    }

    public static FuncValueConverter<TableInfo?, string> Key { get; } = new(t => t is null ? string.Empty : KeyText(t));

    public static FuncValueConverter<TableInfo?, string> Pages { get; } = new(t => t is null ? string.Empty : PagesText(t));

    public static FuncValueConverter<TableInfo?, string> FieldCount { get; } = new(t => t is null ? string.Empty : t.Fields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static FuncValueConverter<TableInfo?, string> Details { get; } = new(t => t is null ? string.Empty : Subtitle(t));
}
