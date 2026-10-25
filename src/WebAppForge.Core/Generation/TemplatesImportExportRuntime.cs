using System.Text;
using System.Text.RegularExpressions;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    /// <summary>
    /// Web/DataExchange.cs of a site that exports or imports: the file formats that need no package (CSV, JSON, XML), the export
    /// response, and the import pages (upload, column mapping, check, one-transaction commit). The settings of the project are
    /// compiled into <c>Config</c>. Fixed texts come from the UI string catalog (<c>@@UI:id@@</c>), so they are translatable.
    /// </summary>
    public static string DataExchangeRuntime(string ns, ImportExportSettings settings)
    {
        var text = DataExchangeText
            .Replace("@@NS@@", ns)
            .Replace("@@USING_LOCALIZATION@@", Localized ? $"using {ns}.Localization;" : string.Empty)
            .Replace("@@CONFIG@@", ConfigExpression(settings));
        if (Auditing is not null)
        {
            text = AuditedExchange(text);
        }

        if (MdTables.Any(t => ExchangeReferences(t).Count > 0))
        {
            text = ReferencedExchange(text);
        }

        if (Localized)
        {
            text = LocalizedExchange(text);
        }

        return Regex.Replace(text, @"@@UI:(\w+)@@", m => Ui(m.Groups[1].Value));
    }

    /// <summary>
    /// The import of a localized site also reads numbers and dates as the visitor's language writes them, after the invariant
    /// forms that the site's own exports use: "12,5" is 12.5 for a Hungarian visitor, and "2026. 08. 05." is a date. Group
    /// separators stay refused, so "1,5" never becomes 15. A date is tried in the visitor's order before the invariant
    /// (month first) one, so 03/04/2026 is 3 April for a British visitor.
    /// </summary>
    private static string LocalizedExchange(string text)
    {
        static string Swap(string text, string from, string to) =>
            text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : throw new InvalidOperationException("The import runtime changed: " + from);

        text = Swap(text,
            "if (decimal.TryParse(text, number, Invariant, out var exact) && exact",
            "if ((decimal.TryParse(text, number, Invariant, out var exact) || decimal.TryParse(text, number, Strings.Culture, out exact)) && exact");
        text = Swap(text,
            "if (decimal.TryParse(text, number, Invariant, out var fraction))",
            "if (decimal.TryParse(text, number, Invariant, out var fraction) || decimal.TryParse(text, number, Strings.Culture, out fraction))");
        return Swap(text,
            "|| DateTime.TryParse(text, Invariant, DateTimeStyles.None, out value);",
            "|| DateTime.TryParse(text, Strings.Culture, DateTimeStyles.None, out value) || DateTime.TryParse(text, Invariant, DateTimeStyles.None, out value);");
    }

    /// <summary>
    /// The import of a site where a table refers to another one with a foreign key of more than one column: a row whose values of
    /// such a key are all filled in must match a row of the referenced table, as the database would require. A key with an empty
    /// value is not checked, like a foreign key in SQL. Sites without such keys keep the import code without this check.
    /// </summary>
    private static string ReferencedExchange(string text)
    {
        static string Swap(string text, string from, string to) =>
            text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : throw new InvalidOperationException("The import runtime changed: " + from);

        const string property = "/// <summary>A foreign key of more than one column that every imported row must match.</summary>\n"
            + "        public Reference[] References { get; init; } = Array.Empty<Reference>();";
        text = Swap(text, "public sealed record Lookup(string Table, string Key, string Display, string Filter);",
            "public sealed record Lookup(string Table, string Key, string Display, string Filter);\n\n"
            + "    /// <summary>A foreign key: the referenced table, the indexes of its columns in Spec.Columns, the referenced columns and the caption of the table.</summary>\n"
            + "    public sealed record Reference(string Table, int[] Columns, string[] Referenced, string Caption);");
        text = text.Contains("public string AuditIdentity { get; init; } = string.Empty;", StringComparison.Ordinal)
            ? Swap(text, "public string AuditIdentity { get; init; } = string.Empty;", "public string AuditIdentity { get; init; } = string.Empty;\n\n        " + property)
            : Swap(text, "string SelectTop, string LimitTail, Col[] Columns);", "string SelectTop, string LimitTail, Col[] Columns)\n    {\n        " + property + "\n    }");
        text = Swap(text, "var seen = new HashSet<string>(StringComparer.Ordinal);",
            "var seen = new HashSet<string>(StringComparer.Ordinal);\n        var references = new Dictionary<string, bool>(StringComparer.Ordinal);");
        text = Swap(text, "if (matchColumns.Length > 0 && matchColumns.All(i => map[i] >= 0 && plan.Values[i] is not null))",
            "foreach (var reference in spec.References)\n"
            + "            {\n"
            + "                if (reference.Columns.All(i => map[i] >= 0 && plan.Values[i] is not null) && !await ReferenceExists(spec, reference, plan.Values, connection, references))\n"
            + "                {\n"
            + "                    plan.Errors.Add(string.Format(@@UI:impReference@@, string.Join(\", \", reference.Columns.Select(i => columns[i].Caption)), reference.Caption));\n"
            + "                }\n"
            + "            }\n\n"
            + "            if (matchColumns.Length > 0 && matchColumns.All(i => map[i] >= 0 && plan.Values[i] is not null))");
        return Swap(text, "/// <summary>True for a field that must have a value in a new row",
            "/// <summary>True when the referenced table has a row with the values of a row of the file; known answers are reused.</summary>\n"
            + "    private static async Task<bool> ReferenceExists(Spec spec, Reference reference, object?[] values, SqliteConnection connection, Dictionary<string, bool> known)\n"
            + "    {\n"
            + "        var key = reference.Table + \"\\u001e\" + string.Join(\"\\u001f\", reference.Columns.Select(i => Cell(spec.Columns[i], values[i])));\n"
            + "        if (!known.TryGetValue(key, out var exists))\n"
            + "        {\n"
            + "            var args = new DynamicParameters();\n"
            + "            var where = new List<string>();\n"
            + "            for (var n = 0; n < reference.Columns.Length; n++)\n"
            + "            {\n"
            + "                where.Add(reference.Referenced[n] + \" = @r\" + n);\n"
            + "                args.Add(\"@r\" + n, values[reference.Columns[n]]);\n"
            + "            }\n\n"
            + "            exists = await connection.ExecuteScalarAsync<long>(\"SELECT COUNT(*) FROM \" + reference.Table + \" WHERE \" + string.Join(\" AND \", where), args) > 0;\n"
            + "            known[key] = exists;\n"
            + "        }\n\n"
            + "        return exists;\n"
            + "    }\n\n"
            + "    /// <summary>True for a field that must have a value in a new row");
    }

    /// <summary>
    /// The import of a site with an audit trail: an added or updated row of an audited table gets its entry in the import
    /// transaction. The values are read back from the table, before and after the change.
    /// </summary>
    private static string AuditedExchange(string text)
    {
        static string Swap(string text, string from, string to) =>
            text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) : throw new InvalidOperationException("The import runtime changed: " + from);

        text = Swap(text, "string SelectTop, string LimitTail, Col[] Columns);", """
            string SelectTop, string LimitTail, Col[] Columns)
                {
                    /// <summary>Name of the table in the audit trail.</summary>
                    public string AuditTable { get; init; } = string.Empty;

                    public bool AuditInserts { get; init; }

                    public bool AuditUpdates { get; init; }

                    /// <summary>The kept columns, the key condition (@a0, @a1, ...) and the key fields in key order.</summary>
                    public string AuditColumns { get; init; } = string.Empty;

                    public string AuditWhere { get; init; } = string.Empty;

                    public string[] AuditKeys { get; init; } = Array.Empty<string>();

                    /// <summary>The statement that reads the key an insert assigned; empty when the key comes from the file.</summary>
                    public string AuditIdentity { get; init; } = string.Empty;
                }
            """);
        text = Swap(text, "private static async Task InsertRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner)",
            "private static async Task InsertRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner, HttpContext http)");
        text = Swap(text, "private static async Task UpdateRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner)",
            "private static async Task UpdateRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner, HttpContext http)");
        text = Swap(text, "await InsertRow(spec, connection, map, plan, owner);", "await InsertRow(spec, connection, map, plan, owner, context);");
        text = Swap(text, "await UpdateRow(spec, connection, map, plan, owner);", "await UpdateRow(spec, connection, map, plan, owner, context);");
        text = Swap(text, """
            await connection.ExecuteAsync("INSERT INTO " + spec.Table + " (" + string.Join(", ", names) + ") VALUES (" + string.Join(", ", marks) + ")", args);
            """, """
            var insert = "INSERT INTO " + spec.Table + " (" + string.Join(", ", names) + ") VALUES (" + string.Join(", ", marks) + ")";
                    if (!spec.AuditInserts)
                    {
                        await connection.ExecuteAsync(insert, args);
                        return;
                    }

                    object?[] keys;
                    if (spec.AuditIdentity.Length > 0)
                    {
                        keys = [await connection.ExecuteScalarAsync<long>(insert + spec.AuditIdentity, args)];
                    }
                    else
                    {
                        await connection.ExecuteAsync(insert, args);
                        keys = AuditKeyValues(spec, plan);
                    }

                    var (key, values) = await AuditRead(spec, connection, keys);
                    await Audit.WriteAsync(connection, http, spec.AuditTable, "insert", key, null, values);
            """);
        text = Swap(text, """
            await connection.ExecuteAsync("UPDATE " + spec.Table + " SET " + string.Join(", ", sets) + " WHERE " + string.Join(" AND ", where), args);
                }
            """, """
            var update = "UPDATE " + spec.Table + " SET " + string.Join(", ", sets) + " WHERE " + string.Join(" AND ", where);
                    if (!spec.AuditUpdates)
                    {
                        await connection.ExecuteAsync(update, args);
                        return;
                    }

                    var keyValues = AuditKeyValues(spec, plan);
                    var (recordKey, before) = await AuditRead(spec, connection, keyValues);
                    if (await connection.ExecuteAsync(update, args) > 0)
                    {
                        var (_, after) = await AuditRead(spec, connection, keyValues);
                        await Audit.WriteAsync(connection, http, spec.AuditTable, "update", recordKey, before, after);
                    }
                }

                /// <summary>The key values of a row of the file, in key order.</summary>
                private static object?[] AuditKeyValues(Spec spec, RowPlan plan) =>
                    spec.AuditKeys.Select(name => plan.Values[Array.FindIndex(spec.Columns, c => c.Name == name)]).ToArray();

                /// <summary>The record key and the kept values of a row, read in the import transaction; null values when the row is not there.</summary>
                private static async Task<(string Key, Dictionary<string, object?>? Values)> AuditRead(Spec spec, SqliteConnection connection, object?[] keys)
                {
                    var args = new DynamicParameters();
                    for (var i = 0; i < keys.Length; i++)
                    {
                        args.Add("@a" + i, keys[i]);
                    }

                    var row = await connection.QueryFirstOrDefaultAsync("SELECT " + spec.AuditColumns + " FROM " + spec.Table + " WHERE " + spec.AuditWhere, args) as IDictionary<string, object>;
                    var key = string.Join("/", keys.Select(k => Convert.ToString(k, CultureInfo.InvariantCulture)));
                    return (key, row?.Where(p => !p.Key.StartsWith("__", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => (object?)p.Value));
                }
            """);
        return text;
    }

    private static string ConfigExpression(ImportExportSettings s)
    {
        static string Formats(DataFormats formats) => string.Join(", ", new[]
        {
            (DataFormats.Csv, "Csv"), (DataFormats.Xlsx, "Xlsx"), (DataFormats.Json, "Json"), (DataFormats.Xml, "Xml"), (DataFormats.Word, "Word"),
        }.Where(f => formats.HasFlag(f.Item1)).Select(f => "Fmt." + f.Item2));

        var delimiter = s.DelimiterChar ?? ',';
        var delimiterLiteral = delimiter switch
        {
            '\t' => "'\\t'",
            '\'' => "'\\''",
            '\\' => "'\\\\'",
            _ => "'" + delimiter + "'",
        };
        return $"new({s.MaxUploadMb}, {delimiterLiteral}, {(s.HeaderStyle == HeaderStyle.Caption ? "true" : "false")}, {(s.IncludeHeader ? "true" : "false")}, "
            + $"{Lit(s.DateFormat)}, {Lit(s.ExportFileNamePattern)}, new[] {{ {Formats(s.ExportFormats)} }}, new[] {{ {Formats(s.ImportFormats)} }})";
    }

    /// <summary>
    /// Web/OpenXmlFormats.cs: Excel and Word files through the Open XML SDK. A site that offers neither gets a stub with the same
    /// members, so DataExchange compiles without the package.
    /// </summary>
    public static string OpenXmlFormatsRuntime(string ns) => (ExchangeOpenXml ? OpenXmlText : OpenXmlStub).Replace("@@NS@@", ns);

    private const string OpenXmlStub = """
        namespace @@NS@@.Web;

        /// <summary>Excel and Word files are not offered by this site, so these members are never called.</summary>
        public static class OpenXmlFormats
        {
            public static byte[] WriteXlsx(string sheet, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, Func<int, object?, string> text) => throw new NotSupportedException();

            public static byte[] WriteWord(string title, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, Func<int, object?, string> text) => throw new NotSupportedException();

            public static List<string?[]> ReadXlsx(byte[] data, int maxRows) => throw new NotSupportedException();

            public static List<string?[]> ReadWord(byte[] data, int maxRows) => throw new NotSupportedException();
        }
        """;

    private const string OpenXmlText = """
        using System.Globalization;
        using System.IO.Compression;
        using System.Text;
        using DocumentFormat.OpenXml;
        using DocumentFormat.OpenXml.Packaging;
        using S = DocumentFormat.OpenXml.Spreadsheet;
        using W = DocumentFormat.OpenXml.Wordprocessing;

        namespace @@NS@@.Web;

        /// <summary>Reads and writes Excel (.xlsx) and Word (.docx) files with the Open XML SDK. A file holds one table of text and numbers.</summary>
        public static class OpenXmlFormats
        {
            private const long MaxUnpackedBytes = 256L * 1024 * 1024;

            public static byte[] WriteXlsx(string sheet, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, Func<int, object?, string> text)
            {
                using var stream = new MemoryStream();
                using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
                {
                    var workbookPart = document.AddWorkbookPart();
                    workbookPart.Workbook = new S.Workbook();
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    var data = new S.SheetData();
                    worksheetPart.Worksheet = new S.Worksheet(data);
                    var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
                    sheets.Append(new S.Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1U, Name = SheetName(sheet) });

                    var rowNumber = 0U;
                    if (headers.Count > 0)
                    {
                        var header = new S.Row { RowIndex = ++rowNumber };
                        for (var i = 0; i < headers.Count; i++)
                        {
                            header.Append(TextCell(headers[i], Reference(i, rowNumber)));
                        }

                        data.Append(header);
                    }

                    foreach (var values in rows)
                    {
                        var row = new S.Row { RowIndex = ++rowNumber };
                        for (var i = 0; i < values.Length; i++)
                        {
                            switch (values[i])
                            {
                                case null:
                                    break;
                                case long or decimal:
                                    row.Append(new S.Cell { CellReference = Reference(i, rowNumber), DataType = S.CellValues.Number, CellValue = new S.CellValue(text(i, values[i])) });
                                    break;
                                default:
                                    row.Append(TextCell(text(i, values[i]), Reference(i, rowNumber)));
                                    break;
                            }
                        }

                        data.Append(row);
                    }

                    workbookPart.Workbook.Save();
                }

                return stream.ToArray();
            }

            public static byte[] WriteWord(string title, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, Func<int, object?, string> text)
            {
                using var stream = new MemoryStream();
                using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
                {
                    var main = document.AddMainDocumentPart();
                    main.Document = new W.Document(new W.Body());
                    var body = main.Document.Body!;
                    body.Append(new W.Paragraph(new W.Run(new W.RunProperties(new W.Bold()), new W.Text(Clean(title)))));

                    var table = new W.Table();
                    table.AppendChild(new W.TableProperties(new W.TableBorders(
                        new W.TopBorder { Val = W.BorderValues.Single, Size = 4U },
                        new W.BottomBorder { Val = W.BorderValues.Single, Size = 4U },
                        new W.LeftBorder { Val = W.BorderValues.Single, Size = 4U },
                        new W.RightBorder { Val = W.BorderValues.Single, Size = 4U },
                        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4U },
                        new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4U })));
                    if (headers.Count > 0)
                    {
                        table.Append(new W.TableRow(headers.Select(h => WordCell(h, true))));
                    }

                    foreach (var values in rows)
                    {
                        table.Append(new W.TableRow(Enumerable.Range(0, values.Length).Select(i => WordCell(values[i] is null ? string.Empty : text(i, values[i]), false))));
                    }

                    body.Append(table);
                    main.Document.Save();
                }

                return stream.ToArray();
            }

            public static List<string?[]> ReadXlsx(byte[] data, int maxRows)
            {
                Guard(data);
                using var stream = new MemoryStream(data);
                using var document = SpreadsheetDocument.Open(stream, false);
                var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("The workbook has no data.");
                var sheetId = workbookPart.Workbook.Descendants<S.Sheet>().FirstOrDefault()?.Id?.Value ?? throw new InvalidDataException("The workbook has no sheet.");
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheetId);
                var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
                var result = new List<string?[]>();
                foreach (var row in worksheetPart.Worksheet.Descendants<S.Row>())
                {
                    var cells = new List<string?>();
                    foreach (var cell in row.Elements<S.Cell>())
                    {
                        var index = ColumnIndex(cell.CellReference?.Value) ?? cells.Count;
                        while (cells.Count < index)
                        {
                            cells.Add(null);
                        }

                        if (index < cells.Count)
                        {
                            continue;
                        }

                        cells.Add(CellText(cell, shared));
                    }

                    if (result.Count > maxRows)
                    {
                        throw new DataExchange.TooManyRowsException();
                    }

                    result.Add(cells.ToArray());
                }

                return result;
            }

            public static List<string?[]> ReadWord(byte[] data, int maxRows)
            {
                Guard(data);
                using var stream = new MemoryStream(data);
                using var document = WordprocessingDocument.Open(stream, false);
                var table = document.MainDocumentPart?.Document?.Body?.Descendants<W.Table>().FirstOrDefault()
                    ?? throw new InvalidDataException("The document has no table.");
                var result = new List<string?[]>();
                foreach (var row in table.Elements<W.TableRow>())
                {
                    if (result.Count > maxRows)
                    {
                        throw new DataExchange.TooManyRowsException();
                    }

                    result.Add(row.Elements<W.TableCell>()
                        .Select(cell => (string?)string.Join("\n", cell.Elements<W.Paragraph>().Select(p => p.InnerText)))
                        .ToArray());
                }

                return result;
            }

            private static S.Cell TextCell(string text, string reference) => new()
            {
                CellReference = reference,
                DataType = S.CellValues.InlineString,
                InlineString = new S.InlineString(new S.Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve }),
            };

            private static W.TableCell WordCell(string text, bool bold)
            {
                var run = new W.Run(new W.Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve });
                if (bold)
                {
                    run.PrependChild(new W.RunProperties(new W.Bold()));
                }

                return new W.TableCell(new W.Paragraph(run));
            }

            private static string? CellText(S.Cell cell, S.SharedStringTable? shared)
            {
                if (cell.DataType is { } type)
                {
                    if (type.Value == S.CellValues.SharedString)
                    {
                        return int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var item) && shared is not null && item < shared.ChildElements.Count
                            ? shared.ChildElements[item].InnerText
                            : null;
                    }

                    if (type.Value == S.CellValues.InlineString)
                    {
                        return cell.InlineString?.InnerText;
                    }

                    if (type.Value == S.CellValues.Boolean)
                    {
                        return cell.CellValue?.Text switch { "1" => "true", "0" => "false", var other => other };
                    }
                }

                return cell.CellValue?.Text;
            }

            /// <summary>Zero-based column of a cell reference such as "C7", or null when the reference is missing or malformed.</summary>
            private static int? ColumnIndex(string? reference)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    return null;
                }

                var column = 0;
                var letters = 0;
                foreach (var c in reference)
                {
                    if (c is >= 'A' and <= 'Z')
                    {
                        column = column * 26 + (c - 'A' + 1);
                        letters++;
                    }
                    else if (c is >= 'a' and <= 'z')
                    {
                        column = column * 26 + (c - 'a' + 1);
                        letters++;
                    }
                    else
                    {
                        break;
                    }
                }

                return letters == 0 || column > 16384 ? null : column - 1;
            }

            /// <summary>The reference of a cell, such as "C7" for the third column of row 7. Cells that are left out leave gaps, so every cell has one.</summary>
            private static string Reference(int column, uint row)
            {
                var name = string.Empty;
                for (var n = column + 1; n > 0; n = (n - 1) / 26)
                {
                    name = (char)('A' + ((n - 1) % 26)) + name;
                }

                return name + row.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>Excel sheet names are at most 31 characters and cannot hold : \ / ? * [ ].</summary>
            private static string SheetName(string name)
            {
                var clean = new string(name.Where(c => !":\\/?*[]".Contains(c)).ToArray()).Trim();
                return clean.Length == 0 ? "Sheet1" : clean.Length > 31 ? clean[..31] : clean;
            }

            /// <summary>Drops the characters XML cannot hold.</summary>
            private static string Clean(string text)
            {
                var sb = new StringBuilder(text.Length);
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (c is '\t' or '\n' or '\r' || (c >= ' ' && c < '\uD800') || (c >= '' && c < '￾'))
                    {
                        sb.Append(c);
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        sb.Append(c).Append(text[++i]);
                    }
                }

                return sb.ToString();
            }

            /// <summary>Refuses a file that unpacks to more than 256 MB (a zip bomb).</summary>
            private static void Guard(byte[] data)
            {
                using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    total += entry.Length;
                    if (total > MaxUnpackedBytes)
                    {
                        throw new InvalidDataException("The file is too large when unpacked.");
                    }
                }
            }
        }
        """;
}
