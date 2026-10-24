using System.Globalization;
using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of import and export: &lt;importExport enabled="" exportFormats="" .../&gt; under the project and
/// &lt;importExport export="" import="" mode="" maxRows=""&gt;&lt;exclude field=""/&gt;&lt;/importExport&gt; under a table.
/// Elements are omitted when every setting is default and attributes when default, so older project files load unchanged.
/// </summary>
internal static class ImportExportXml
{
    private static readonly ImportExportSettings Defaults = new();

    public static void Append(XElement root, ImportExportSettings settings)
    {
        if (settings.IsDefault())
        {
            return;
        }

        var d = Defaults;
        var e = new XElement("importExport");
        WriteBool(e, "enabled", settings.Enabled, d.Enabled);
        WriteEnum(e, "exportFormats", settings.ExportFormats, d.ExportFormats);
        WriteEnum(e, "importFormats", settings.ImportFormats, d.ImportFormats);
        WriteInt(e, "maxUploadMb", settings.MaxUploadMb, d.MaxUploadMb);
        WriteStr(e, "csvDelimiter", settings.CsvDelimiter, d.CsvDelimiter);
        WriteEnum(e, "headerStyle", settings.HeaderStyle, d.HeaderStyle);
        WriteBool(e, "includeHeader", settings.IncludeHeader, d.IncludeHeader);
        WriteStr(e, "dateFormat", settings.DateFormat, d.DateFormat);
        WriteStr(e, "fileName", settings.ExportFileNamePattern, d.ExportFileNamePattern);
        WriteInt(e, "exportMinLevel", settings.ExportMinLevel, d.ExportMinLevel);
        WriteInt(e, "importMinLevel", settings.ImportMinLevel, d.ImportMinLevel);
        root.Add(e);
    }

    public static void Read(XElement root, Project project)
    {
        if (root.Element("importExport") is not { } e)
        {
            return;
        }

        var d = Defaults;
        project.ImportExport = new ImportExportSettings
        {
            Enabled = ReadBool(e, "enabled", d.Enabled),
            ExportFormats = ReadFormats(e, "exportFormats", d.ExportFormats),
            ImportFormats = ReadFormats(e, "importFormats", d.ImportFormats),
            MaxUploadMb = ReadInt(e, "maxUploadMb", d.MaxUploadMb),
            CsvDelimiter = ReadStr(e, "csvDelimiter", d.CsvDelimiter),
            HeaderStyle = ReadEnum(e, "headerStyle", d.HeaderStyle),
            IncludeHeader = ReadBool(e, "includeHeader", d.IncludeHeader),
            DateFormat = ReadStr(e, "dateFormat", d.DateFormat),
            ExportFileNamePattern = ReadStr(e, "fileName", d.ExportFileNamePattern),
            ExportMinLevel = ReadInt(e, "exportMinLevel", d.ExportMinLevel),
            ImportMinLevel = ReadInt(e, "importMinLevel", d.ImportMinLevel),
        };
    }

    public static void AppendTable(XElement table, TableInfo info)
    {
        var settings = info.ImportExport;
        if (settings.IsDefault())
        {
            return;
        }

        var d = new TableImportExport();
        var e = new XElement("importExport");
        WriteBool(e, "export", settings.Export, d.Export);
        WriteBool(e, "import", settings.Import, d.Import);
        WriteEnum(e, "mode", settings.ImportMode, d.ImportMode);
        WriteInt(e, "maxRows", settings.ExportMaxRows, d.ExportMaxRows);
        foreach (var field in settings.ExcludedFields)
        {
            e.Add(new XElement("exclude", new XAttribute("field", field)));
        }

        table.Add(e);
    }

    public static void ReadTable(XElement table, TableInfo info)
    {
        if (table.Element("importExport") is not { } e)
        {
            return;
        }

        var d = new TableImportExport();
        info.ImportExport = new TableImportExport
        {
            Export = ReadBool(e, "export", d.Export),
            Import = ReadBool(e, "import", d.Import),
            ImportMode = ReadEnum(e, "mode", d.ImportMode),
            ExportMaxRows = ReadInt(e, "maxRows", d.ExportMaxRows),
        };
        foreach (var exclude in e.Elements("exclude"))
        {
            info.ImportExport.ExcludedFields.Add(ReadStr(exclude, "field", string.Empty));
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
            e.Add(new XAttribute(name, value));
        }
    }

    private static void WriteEnum<T>(XElement e, string name, T value, T fallback)
        where T : struct, System.Enum
    {
        if (!EqualityComparer<T>.Default.Equals(value, fallback))
        {
            e.Add(new XAttribute(name, value.ToString().Replace(" ", string.Empty)));
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

    /// <summary>A comma separated list of formats such as "Csv,Xlsx"; a value that does not parse keeps the fallback.</summary>
    private static DataFormats ReadFormats(XElement e, string name, DataFormats fallback) =>
        System.Enum.TryParse<DataFormats>(e.Attribute(name)?.Value, out var value) && (value & ~DataFormats.All) == 0 ? value : fallback;
}
