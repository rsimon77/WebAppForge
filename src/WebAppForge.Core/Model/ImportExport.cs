namespace WebAppForge.Core.Model;

/// <summary>The file formats of the generated import and export pages. A set of formats is a flags value.</summary>
[Flags]
public enum DataFormats
{
    None = 0,
    Csv = 1,
    Xlsx = 2,
    Json = 4,
    Xml = 8,

    /// <summary>A Word document (.docx) with one table.</summary>
    Word = 16,

    All = Csv | Xlsx | Json | Xml | Word,
}

/// <summary>What the header row of CSV, Excel and Word files and the import column mapping call a field.</summary>
public enum HeaderStyle
{
    /// <summary>The field caption (the field name when it has none).</summary>
    Caption,

    /// <summary>The field name as it is in the database.</summary>
    FieldName,
}

/// <summary>How an import treats a row whose primary key is already in the table.</summary>
public enum ImportMode
{
    /// <summary>Every row is added. A row whose key exists is an error.</summary>
    Insert,

    /// <summary>A row whose key exists updates that row; any other row is added.</summary>
    Upsert,
}

/// <summary>
/// Import and export of the generated site: an export menu on the list pages and an import page per table that uploads a file,
/// maps its columns to fields, checks the rows and adds them in one transaction. Off by default, so a project that never turns it
/// on generates the same site as before.
/// </summary>
public sealed class ImportExportSettings
{
    public const int DefaultMaxUploadMb = 10;

    public const string DefaultDateFormat = "yyyy-MM-dd";

    public const string DefaultFileNamePattern = "{table}-{date}";

    public bool Enabled { get; set; }

    /// <summary>Formats offered by the export menu. Excel and Word add the Open XML SDK package to the generated site.</summary>
    public DataFormats ExportFormats { get; set; } = DataFormats.Csv | DataFormats.Xlsx | DataFormats.Json | DataFormats.Xml;

    /// <summary>Formats the import page accepts.</summary>
    public DataFormats ImportFormats { get; set; } = DataFormats.Csv | DataFormats.Xlsx | DataFormats.Json | DataFormats.Xml;

    /// <summary>Largest upload in megabytes, 1 to 100.</summary>
    public int MaxUploadMb { get; set; } = DefaultMaxUploadMb;

    /// <summary>One character: the field separator of exported CSV files. "\t" or "tab" stands for a tab. An import detects the separator.</summary>
    public string CsvDelimiter { get; set; } = ",";

    public HeaderStyle HeaderStyle { get; set; } = HeaderStyle.Caption;

    /// <summary>Whether CSV, Excel and Word exports start with a header row, and whether the import page expects one by default.</summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// .NET format of exported dates. A date and time uses it followed by " HH:mm:ss". An import reads this format first, then ISO 8601
    /// and Excel dates.
    /// </summary>
    public string DateFormat { get; set; } = DefaultDateFormat;

    /// <summary>File name of an export without the extension. {table}, {date} (yyyyMMdd) and {datetime} (yyyyMMdd-HHmmss) are replaced.</summary>
    public string ExportFileNamePattern { get; set; } = DefaultFileNamePattern;

    /// <summary>Lowest user level that may export. Used with User Level security only, together with the table's view level.</summary>
    public int ExportMinLevel { get; set; }

    /// <summary>Lowest user level that may import. Used with User Level security only, together with the table's edit level.</summary>
    public int ImportMinLevel { get; set; }

    /// <summary>The character of <see cref="CsvDelimiter"/>, or null when it is not a valid single separator.</summary>
    public char? DelimiterChar
    {
        get
        {
            var text = CsvDelimiter;
            if (text is "\t" or "tab" or "TAB" or "Tab" or "\\t")
            {
                return '\t';
            }

            return text.Length == 1 && !char.IsLetterOrDigit(text[0]) && text[0] is not ('"' or '\r' or '\n' or ' ') ? text[0] : null;
        }
    }

    /// <summary>True when every setting has its default value, so the project file needs no element for it.</summary>
    public bool IsDefault()
    {
        var d = new ImportExportSettings();
        return Enabled == d.Enabled && ExportFormats == d.ExportFormats && ImportFormats == d.ImportFormats && MaxUploadMb == d.MaxUploadMb
            && CsvDelimiter == d.CsvDelimiter && HeaderStyle == d.HeaderStyle && IncludeHeader == d.IncludeHeader
            && DateFormat == d.DateFormat && ExportFileNamePattern == d.ExportFileNamePattern
            && ExportMinLevel == d.ExportMinLevel && ImportMinLevel == d.ImportMinLevel;
    }

    public ImportExportSettings Clone() => (ImportExportSettings)MemberwiseClone();
}

/// <summary>Import and export settings of one table. They apply only when <see cref="ImportExportSettings.Enabled"/> is set.</summary>
public sealed class TableImportExport
{
    /// <summary>Whether the list page offers an export of the table.</summary>
    public bool Export { get; set; } = true;

    /// <summary>Whether the table has an import page. The users table of the login security never has one.</summary>
    public bool Import { get; set; } = true;

    public ImportMode ImportMode { get; set; } = ImportMode.Insert;

    /// <summary>Fields that are never exported nor imported. Secret fields, the owner field and binary fields are always left out.</summary>
    public List<string> ExcludedFields { get; } = [];

    /// <summary>Most rows one export contains. 0 means all rows.</summary>
    public int ExportMaxRows { get; set; }

    public bool IsDefault() => Export && Import && ImportMode == ImportMode.Insert && ExcludedFields.Count == 0 && ExportMaxRows == 0;

    public TableImportExport Clone()
    {
        var copy = new TableImportExport { Export = Export, Import = Import, ImportMode = ImportMode, ExportMaxRows = ExportMaxRows };
        copy.ExcludedFields.AddRange(ExcludedFields);
        return copy;
    }
}
