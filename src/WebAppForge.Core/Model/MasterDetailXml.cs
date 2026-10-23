using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of the master/detail and multi-page form contract, per table:
/// &lt;details&gt;&lt;detail ...&gt;&lt;link master="" detail=""/&gt;&lt;column field=""/&gt;&lt;/detail&gt;&lt;/details&gt;,
/// &lt;foreignKeys&gt;&lt;fk ...&gt;&lt;column from="" to=""/&gt;&lt;/fk&gt;&lt;/foreignKeys&gt; and
/// &lt;formPages&gt;&lt;page name="" caption="" icon="" description=""/&gt;&lt;/formPages&gt;.
/// Elements are omitted when empty and attributes when default, so older project files load unchanged.
/// </summary>
internal static class MasterDetailXml
{
    private static readonly DetailLink DetailDefaults = new();

    public static void Append(XElement table, TableInfo info)
    {
        if (info.Details.Count > 0)
        {
            var d = DetailDefaults;
            var details = new XElement("details");
            foreach (var detail in info.Details)
            {
                var e = new XElement("detail", new XAttribute("table", detail.DetailTable));
                WriteStr(e, "caption", detail.Caption, d.Caption);
                WriteBool(e, "showOnView", detail.ShowOnView, d.ShowOnView);
                WriteBool(e, "showOnEdit", detail.ShowOnEdit, d.ShowOnEdit);
                WriteBool(e, "showOnAdd", detail.ShowOnAdd, d.ShowOnAdd);
                WriteEnum(e, "preview", detail.Preview, d.Preview);
                WriteEnum(e, "onDelete", detail.OnDelete, d.OnDelete);
                WriteEnum(e, "onUpdate", detail.OnUpdate, d.OnUpdate);
                if (detail.PageSize != d.PageSize)
                {
                    e.Add(new XAttribute("pageSize", detail.PageSize));
                }

                WriteBool(e, "allowAdd", detail.AllowAdd, d.AllowAdd);
                WriteBool(e, "allowEdit", detail.AllowEdit, d.AllowEdit);
                WriteBool(e, "allowDelete", detail.AllowDelete, d.AllowDelete);
                WriteStr(e, "formPage", detail.FormPage, d.FormPage);
                foreach (var link in detail.Links)
                {
                    e.Add(new XElement("link", new XAttribute("master", link.MasterField), new XAttribute("detail", link.DetailField)));
                }

                foreach (var column in detail.Columns)
                {
                    e.Add(new XElement("column", new XAttribute("field", column)));
                }

                details.Add(e);
            }

            table.Add(details);
        }

        if (info.ForeignKeys.Count > 0)
        {
            var keys = new XElement("foreignKeys");
            foreach (var fk in info.ForeignKeys)
            {
                var e = new XElement("fk");
                WriteStr(e, "onDelete", fk.OnDelete, string.Empty);
                WriteStr(e, "onUpdate", fk.OnUpdate, string.Empty);
                e.Add(new XAttribute("referencedTable", fk.ReferencedTable));
                for (var i = 0; i < fk.Columns.Count; i++)
                {
                    e.Add(new XElement("column",
                        new XAttribute("from", fk.Columns[i]),
                        new XAttribute("to", i < fk.ReferencedColumns.Count ? fk.ReferencedColumns[i] : string.Empty)));
                }

                keys.Add(e);
            }

            table.Add(keys);
        }

        if (info.FormPages.Count > 0)
        {
            var pages = new XElement("formPages");
            foreach (var page in info.FormPages)
            {
                var e = new XElement("page", new XAttribute("name", page.Name));
                WriteStr(e, "caption", page.Caption, string.Empty);
                WriteStr(e, "icon", page.Icon, string.Empty);
                WriteStr(e, "description", page.Description, string.Empty);
                pages.Add(e);
            }

            table.Add(pages);
        }
    }

    public static void Read(XElement table, TableInfo info)
    {
        if (table.Element("details") is { } details)
        {
            var d = DetailDefaults;
            foreach (var e in details.Elements("detail"))
            {
                var detail = new DetailLink
                {
                    DetailTable = ReadStr(e, "table", string.Empty),
                    Caption = ReadStr(e, "caption", d.Caption),
                    ShowOnView = ReadBool(e, "showOnView", d.ShowOnView),
                    ShowOnEdit = ReadBool(e, "showOnEdit", d.ShowOnEdit),
                    ShowOnAdd = ReadBool(e, "showOnAdd", d.ShowOnAdd),
                    Preview = ReadEnum(e, "preview", d.Preview),
                    OnDelete = ReadEnum(e, "onDelete", d.OnDelete),
                    OnUpdate = ReadEnum(e, "onUpdate", d.OnUpdate),
                    PageSize = int.TryParse(e.Attribute("pageSize")?.Value, out var size) ? size : d.PageSize,
                    AllowAdd = ReadBool(e, "allowAdd", d.AllowAdd),
                    AllowEdit = ReadBool(e, "allowEdit", d.AllowEdit),
                    AllowDelete = ReadBool(e, "allowDelete", d.AllowDelete),
                    FormPage = ReadStr(e, "formPage", d.FormPage),
                };
                foreach (var link in e.Elements("link"))
                {
                    detail.Links.Add(new KeyLink { MasterField = ReadStr(link, "master", string.Empty), DetailField = ReadStr(link, "detail", string.Empty) });
                }

                foreach (var column in e.Elements("column"))
                {
                    detail.Columns.Add(ReadStr(column, "field", string.Empty));
                }

                info.Details.Add(detail);
            }
        }

        if (table.Element("foreignKeys") is { } keys)
        {
            foreach (var e in keys.Elements("fk"))
            {
                var fk = new ForeignKeyInfo
                {
                    ReferencedTable = ReadStr(e, "referencedTable", string.Empty),
                    OnDelete = ReadStr(e, "onDelete", string.Empty),
                    OnUpdate = ReadStr(e, "onUpdate", string.Empty),
                };
                foreach (var column in e.Elements("column"))
                {
                    fk.Columns.Add(ReadStr(column, "from", string.Empty));
                    fk.ReferencedColumns.Add(ReadStr(column, "to", string.Empty));
                }

                info.ForeignKeys.Add(fk);
            }
        }

        if (table.Element("formPages") is { } pages)
        {
            foreach (var e in pages.Elements("page"))
            {
                info.FormPages.Add(new FormPageDef
                {
                    Name = ReadStr(e, "name", string.Empty),
                    Caption = ReadStr(e, "caption", string.Empty),
                    Icon = ReadStr(e, "icon", string.Empty),
                    Description = ReadStr(e, "description", string.Empty),
                });
            }
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

    private static T ReadEnum<T>(XElement e, string name, T fallback)
        where T : struct, System.Enum =>
        System.Enum.TryParse<T>(e.Attribute(name)?.Value, out var value) && System.Enum.IsDefined(value) ? value : fallback;
}
