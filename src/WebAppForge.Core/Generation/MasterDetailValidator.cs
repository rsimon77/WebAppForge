using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Generation-time checks of master/detail links and form pages. The wording is shared with the desktop editor
/// ("Master/detail A -> B: ..."). Warnings of the editor (differing link field types, partial composite keys, ...) never block generation.
/// </summary>
internal static class MasterDetailValidator
{
    private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static void Validate(IReadOnlyList<TableInfo> tables)
    {
        foreach (var table in tables.Where(t => t.Generate))
        {
            ValidateFormPages(table);
            for (var i = 0; i < table.Details.Count; i++)
            {
                ValidateLink(tables, table, i);
            }
        }

        ValidateCycles(tables);
    }

    private static void ValidateFormPages(TableInfo table)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in table.FormPages)
        {
            var name = page.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                throw new InvalidOperationException($"Table '{table.Name}': a form page has no name.");
            }

            if (!seen.Add(name))
            {
                throw new InvalidOperationException($"Table '{table.Name}': the form page '{name}' exists twice.");
            }
        }
    }

    private static void ValidateLink(IReadOnlyList<TableInfo> tables, TableInfo master, int index)
    {
        var link = master.Details[index];
        var prefix = $"Master/detail {master.Name} -> {link.DetailTable}: ";
        InvalidOperationException Error(string text) => new(prefix + text);

        if (string.IsNullOrWhiteSpace(link.DetailTable))
        {
            throw Error("choose the detail table.");
        }

        var detail = tables.FirstOrDefault(t => Same(t.Name, link.DetailTable))
            ?? throw Error($"the detail table '{link.DetailTable}' does not exist.");
        if (!detail.Generate)
        {
            throw Error($"the detail table '{detail.Name}' is not generated (Generate is off).");
        }

        if (master.PrimaryKeys.Count == 0)
        {
            throw Error($"the master table '{master.Name}' has no primary key.");
        }

        if (link.Links.Count == 0)
        {
            throw Error("add at least one key link (master field = detail field).");
        }

        var pairs = new List<(FieldInfo Master, FieldInfo Detail)>();
        for (var n = 0; n < link.Links.Count; n++)
        {
            var pair = link.Links[n];
            if (string.IsNullOrWhiteSpace(pair.MasterField))
            {
                throw Error($"key link {n + 1} has no master field.");
            }

            if (string.IsNullOrWhiteSpace(pair.DetailField))
            {
                throw Error($"key link {n + 1} has no detail field.");
            }

            var m = master.Fields.FirstOrDefault(f => Same(f.Name, pair.MasterField))
                ?? throw Error($"the master field '{pair.MasterField}' does not exist in table '{master.Name}'.");
            var d = detail.Fields.FirstOrDefault(f => Same(f.Name, pair.DetailField))
                ?? throw Error($"the detail field '{pair.DetailField}' does not exist in table '{detail.Name}'.");
            if (pairs.Any(p => ReferenceEquals(p.Master, m) && ReferenceEquals(p.Detail, d)))
            {
                throw Error($"the key link '{pair.MasterField} = {pair.DetailField}' is listed twice.");
            }

            pairs.Add((m, d));
        }

        for (var other = 0; other < index; other++)
        {
            var o = master.Details[other];
            if (Same(o.DetailTable, link.DetailTable) && o.Links.Count == link.Links.Count
                && link.Links.All(p => o.Links.Any(q => Same(q.MasterField, p.MasterField) && Same(q.DetailField, p.DetailField))))
            {
                throw Error($"duplicate of another link of '{master.Name}' to '{detail.Name}' with the same key fields.");
            }
        }

        foreach (var column in link.Columns)
        {
            if (!detail.Fields.Any(f => Same(f.Name, column)))
            {
                throw Error($"the grid column '{column}' does not exist in table '{detail.Name}'.");
            }
        }

        if (link.PageSize is < 1 or > 200)
        {
            throw Error($"the page size {link.PageSize} must be from 1 to 200.");
        }

        if (link.FormPage.Trim().Length > 0 && !master.FormPages.Any(p => Same(p.Name, link.FormPage)))
        {
            throw Error($"the grid is placed on form page '{link.FormPage.Trim()}', which '{master.Name}' does not have.");
        }

        if (link.OnDelete == DeleteRule.SetNull)
        {
            foreach (var (_, d) in pairs)
            {
                if (d.IsRequired || d.IsPrimaryKey)
                {
                    throw Error($"on delete 'Set null' needs nullable link fields, but '{d.Name}' of '{detail.Name}' is required.");
                }
            }
        }

        if (Same(master.Name, detail.Name))
        {
            if (link.OnDelete == DeleteRule.Cascade)
            {
                throw Error("a table cannot cascade delete to itself (Cascade delete cycle: "
                    + $"{master.Name} -> {master.Name}). Use Restrict or Set null.");
            }
        }

        if (link.ShowOnAdd && !(master.Pages.Edit && (master.PrimaryKeys.Count > 1 || master.Fields.Any(f => !f.IsPrimaryKey && !f.IsSecret))))
        {
            throw Error($"the details are shown after add (ShowOnAdd) but the edit page of '{master.Name}' is not generated.");
        }
    }

    /// <summary>Cascade delete rules between different tables may not form a cycle.</summary>
    private static void ValidateCycles(IReadOnlyList<TableInfo> tables)
    {
        var edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables.Where(t => t.Generate))
        {
            edges[t.Name] = t.Details.Where(l => l.OnDelete == DeleteRule.Cascade && !Same(l.DetailTable, t.Name)).Select(l => l.DetailTable.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        void Visit(string name)
        {
            state[name] = 1;
            path.Add(name);
            foreach (var next in edges.GetValueOrDefault(name) ?? [])
            {
                var s = state.GetValueOrDefault(next);
                if (s == 1)
                {
                    var start = path.FindIndex(p => Same(p, next));
                    var cycle = path.Skip(start).Append(next);
                    throw new InvalidOperationException("Cascade delete cycle: " + string.Join(" -> ", cycle) + ". Cascade delete rules may not form a cycle; change one of them to Restrict or Set null.");
                }

                if (s == 0)
                {
                    Visit(next);
                }
            }

            path.RemoveAt(path.Count - 1);
            state[name] = 2;
        }

        foreach (var name in edges.Keys.ToList())
        {
            if (state.GetValueOrDefault(name) == 0)
            {
                Visit(name);
            }
        }
    }
}
