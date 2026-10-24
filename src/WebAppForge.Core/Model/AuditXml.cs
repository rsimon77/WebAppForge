using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>The audit settings in the project file. Nothing is written while they keep their defaults.</summary>
internal static class AuditXml
{
    public static void AppendProject(XElement root, AuditSettings audit)
    {
        if (audit.IsDefault)
        {
            return;
        }

        root.Add(new XElement("audit",
            new XAttribute("enabled", audit.Enabled),
            new XAttribute("logTable", audit.LogTable),
            new XAttribute("createTable", audit.CreateTable),
            new XAttribute("values", audit.Values.ToString()),
            new XAttribute("recordIpAddress", audit.RecordIpAddress),
            new XAttribute("showLogPage", audit.ShowLogPage),
            new XAttribute("logPageMinLevel", audit.LogPageMinLevel)));
    }

    public static AuditSettings ReadProject(XElement root)
    {
        var d = new AuditSettings();
        if (root.Element("audit") is not { } e)
        {
            return d;
        }

        return new AuditSettings
        {
            Enabled = Bool(e, "enabled", d.Enabled),
            LogTable = (string?)e.Attribute("logTable") ?? d.LogTable,
            CreateTable = Bool(e, "createTable", d.CreateTable),
            Values = Enum.TryParse<AuditValues>((string?)e.Attribute("values"), true, out var values) ? values : d.Values,
            RecordIpAddress = Bool(e, "recordIpAddress", d.RecordIpAddress),
            ShowLogPage = Bool(e, "showLogPage", d.ShowLogPage),
            LogPageMinLevel = int.TryParse((string?)e.Attribute("logPageMinLevel"), out var level) ? level : d.LogPageMinLevel,
        };
    }

    public static void AppendTable(XElement table, TableAudit audit)
    {
        if (audit.IsDefault)
        {
            return;
        }

        var e = new XElement("audit",
            new XAttribute("enabled", audit.Enabled),
            new XAttribute("inserts", audit.Inserts),
            new XAttribute("updates", audit.Updates),
            new XAttribute("deletes", audit.Deletes));
        foreach (var field in audit.ExcludedFields)
        {
            e.Add(new XElement("exclude", new XAttribute("field", field)));
        }

        table.Add(e);
    }

    public static void ReadTable(XElement table, TableInfo info)
    {
        if (table.Element("audit") is not { } e)
        {
            return;
        }

        var d = new TableAudit();
        info.Audit = new TableAudit
        {
            Enabled = Bool(e, "enabled", d.Enabled),
            Inserts = Bool(e, "inserts", d.Inserts),
            Updates = Bool(e, "updates", d.Updates),
            Deletes = Bool(e, "deletes", d.Deletes),
        };
        foreach (var exclude in e.Elements("exclude"))
        {
            if ((string?)exclude.Attribute("field") is { Length: > 0 } field)
            {
                info.Audit.ExcludedFields.Add(field);
            }
        }
    }

    private static bool Bool(XElement e, string name, bool fallback) => bool.TryParse((string?)e.Attribute(name), out var value) ? value : fallback;
}
