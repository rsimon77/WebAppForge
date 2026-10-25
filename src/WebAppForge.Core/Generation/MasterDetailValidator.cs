using WebAppForge.Core.Model;
using M = WebAppForge.Core.Localization.CoreMessages;

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
                throw new InvalidOperationException(M.Format("MasterDetail_FormPageNoName", table.Name));
            }

            if (!seen.Add(name))
            {
                throw new InvalidOperationException(M.Format("MasterDetail_FormPageTwice", table.Name, name));
            }
        }
    }

    private static void ValidateLink(IReadOnlyList<TableInfo> tables, TableInfo master, int index)
    {
        var link = master.Details[index];
        InvalidOperationException Error(string text) => new(M.Format("MasterDetail_Problem", master.Name, link.DetailTable, text));

        if (string.IsNullOrWhiteSpace(link.DetailTable))
        {
            throw Error(M.Text("MasterDetail_ChooseDetail"));
        }

        var detail = tables.FirstOrDefault(t => Same(t.Name, link.DetailTable))
            ?? throw Error(M.Format("MasterDetail_DetailMissing", link.DetailTable));
        if (!detail.Generate)
        {
            throw Error(M.Format("MasterDetail_DetailNotGenerated", detail.Name));
        }

        if (master.PrimaryKeys.Count == 0)
        {
            throw Error(M.Format("MasterDetail_MasterNoKey", master.Name));
        }

        if (link.Links.Count == 0)
        {
            throw Error(M.Text("MasterDetail_NoLinks"));
        }

        var pairs = new List<(FieldInfo Master, FieldInfo Detail)>();
        for (var n = 0; n < link.Links.Count; n++)
        {
            var pair = link.Links[n];
            if (string.IsNullOrWhiteSpace(pair.MasterField))
            {
                throw Error(M.Format("MasterDetail_LinkNoMaster", n + 1));
            }

            if (string.IsNullOrWhiteSpace(pair.DetailField))
            {
                throw Error(M.Format("MasterDetail_LinkNoDetail", n + 1));
            }

            var m = master.Fields.FirstOrDefault(f => Same(f.Name, pair.MasterField))
                ?? throw Error(M.Format("MasterDetail_MasterFieldMissing", pair.MasterField, master.Name));
            var d = detail.Fields.FirstOrDefault(f => Same(f.Name, pair.DetailField))
                ?? throw Error(M.Format("MasterDetail_DetailFieldMissing", pair.DetailField, detail.Name));
            if (pairs.Any(p => ReferenceEquals(p.Master, m) && ReferenceEquals(p.Detail, d)))
            {
                throw Error(M.Format("MasterDetail_LinkTwice", pair.MasterField, pair.DetailField));
            }

            pairs.Add((m, d));
        }

        for (var other = 0; other < index; other++)
        {
            var o = master.Details[other];
            if (Same(o.DetailTable, link.DetailTable) && o.Links.Count == link.Links.Count
                && link.Links.All(p => o.Links.Any(q => Same(q.MasterField, p.MasterField) && Same(q.DetailField, p.DetailField))))
            {
                throw Error(M.Format("MasterDetail_Duplicate", master.Name, detail.Name));
            }
        }

        foreach (var column in link.Columns)
        {
            if (!detail.Fields.Any(f => Same(f.Name, column)))
            {
                throw Error(M.Format("MasterDetail_GridColumnMissing", column, detail.Name));
            }
        }

        if (link.PageSize is < 1 or > 200)
        {
            throw Error(M.Format("MasterDetail_PageSize", link.PageSize));
        }

        if (link.FormPage.Trim().Length > 0 && !master.FormPages.Any(p => Same(p.Name, link.FormPage)))
        {
            throw Error(M.Format("MasterDetail_FormPageMissing", link.FormPage.Trim(), master.Name));
        }

        if (link.OnDelete == DeleteRule.SetNull)
        {
            foreach (var (_, d) in pairs)
            {
                if (d.IsRequired || d.IsPrimaryKey)
                {
                    throw Error(M.Format("MasterDetail_SetNullRequired", d.Name, detail.Name));
                }
            }
        }

        if (Same(master.Name, detail.Name))
        {
            if (link.OnDelete == DeleteRule.Cascade)
            {
                throw Error(M.Format("MasterDetail_SelfCascade", master.Name));
            }
        }

        if (link.ShowOnAdd && !(master.Pages.Edit && (master.PrimaryKeys.Count > 1 || master.Fields.Any(f => !f.IsPrimaryKey && !f.IsSecret))))
        {
            throw Error(M.Format("MasterDetail_ShowOnAdd", master.Name));
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
                    throw new InvalidOperationException(M.Format("MasterDetail_Cycle", string.Join(" -> ", cycle)));
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
