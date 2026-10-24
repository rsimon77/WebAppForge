namespace WebAppForge.Core.Generation;

internal static partial class Templates
{
    private const string DataExchangeText = """
        using System.Collections.Concurrent;
        using System.Globalization;
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using System.Text.RegularExpressions;
        using System.Xml;
        using System.Xml.Linq;
        using Dapper;
        using Microsoft.AspNetCore.Antiforgery;
        using Microsoft.AspNetCore.Http.Features;
        using Microsoft.Data.Sqlite;
        @@USING_LOCALIZATION@@

        namespace @@NS@@.Web;

        /// <summary>
        /// Export and import of the tables of the generated site. An export writes the rows of the list (search, filters and sort
        /// included, every page) as CSV, Excel, JSON, XML or Word. An import uploads a file, maps its columns to fields, checks every
        /// row and adds or updates the rows in one transaction. Table and column names come from the project, values are always parameters.
        /// </summary>
        public static class DataExchange
        {
            public enum Kind { Text, Integer, Decimal, Boolean, Date, DateTime }

            public enum Fmt { Csv, Xlsx, Json, Xml, Word }

            public sealed record Choice(string Value, string Text);

            public sealed record Lookup(string Table, string Key, string Display, string Filter);

            public sealed record Col(string Name, string Caption, string Column, Kind Kind)
            {
                public bool Required { get; init; }

                public bool IsKey { get; init; }

                public bool IsAuto { get; init; }

                public bool HasDefault { get; init; }

                public int MaxLength { get; init; }

                public int MinLength { get; init; }

                public string Min { get; init; } = string.Empty;

                public string Max { get; init; } = string.Empty;

                public string Pattern { get; init; } = string.Empty;

                public string PatternMessage { get; init; } = string.Empty;

                public Lookup? Lookup { get; init; }

                public IReadOnlyList<Choice> Choices { get; init; } = Array.Empty<Choice>();
            }

            public sealed record Spec(string Route, string Caption, string Table, string OwnerColumn, bool Upsert, string SelectTop, string LimitTail, Col[] Columns);

            public sealed record Settings(int MaxUploadMb, char Delimiter, bool CaptionHeaders, bool IncludeHeader, string DateFormat, string FileName, Fmt[] Export, Fmt[] Import);

            public sealed class TooManyRowsException : Exception
            {
            }

            public static readonly Settings Config = @@CONFIG@@;

            private const int MaxRows = 50000;
            private const int PreviewRows = 10;
            private const int ErrorsShown = 50;
            private const int PendingPerUser = 5;
            private static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(30);
            private static readonly Regex TokenPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
            private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
            private static readonly ConcurrentDictionary<string, Pending> Uploads = new();

            private sealed record Pending(string Route, string User, Fmt Format, bool HasHeader, string FileName, string Path, DateTime Created);

            // ---- names and small helpers -------------------------------------------------------------

            private static string Extension(Fmt format) => format switch { Fmt.Csv => ".csv", Fmt.Xlsx => ".xlsx", Fmt.Json => ".json", Fmt.Xml => ".xml", _ => ".docx" };

            private static string ContentType(Fmt format) => format switch
            {
                Fmt.Csv => "text/csv; charset=utf-8",
                Fmt.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Fmt.Json => "application/json",
                Fmt.Xml => "application/xml",
                _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            };

            private static string FormatName(Fmt format) => format switch { Fmt.Csv => "CSV", Fmt.Xlsx => "Excel", Fmt.Json => "JSON", Fmt.Xml => "XML", _ => "Word" };

            private static Fmt? FromExtension(string fileName) => System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" or ".tsv" or ".txt" => Fmt.Csv,
                ".xlsx" => Fmt.Xlsx,
                ".json" => Fmt.Json,
                ".xml" => Fmt.Xml,
                ".docx" => Fmt.Word,
                _ => null,
            };

            /// <summary>The export format named by the request, when the project offers it.</summary>
            public static Fmt? ExportFormat(string? name) =>
                Enum.TryParse<Fmt>(name, true, out var format) && Enum.IsDefined(format) && Config.Export.Contains(format) ? format : null;

            private static string HeaderOf(Col column) => Config.CaptionHeaders ? column.Caption : column.Name;

            private static string User(HttpContext context) => context.User.Identity?.Name ?? string.Empty;

            private static string Owner(Spec spec, HttpContext context) => spec.OwnerColumn.Length > 0 ? User(context) : string.Empty;

            /// <summary>The export file name without extension: the pattern of the project with letters, digits, dot, dash and underscore only.</summary>
            private static string BaseName(Spec spec)
            {
                var now = DateTime.Now;
                var name = Config.FileName
                    .Replace("{table}", spec.Route.TrimStart('/'))
                    .Replace("{datetime}", now.ToString("yyyyMMdd-HHmmss", Invariant))
                    .Replace("{date}", now.ToString("yyyyMMdd", Invariant));
                name = Regex.Replace(name, "[^A-Za-z0-9._-]", "_").Trim('.');
                return name.Length == 0 ? "export" : name;
            }

            /// <summary>Lower-case letters and digits of a name, so "Order Date", "order_date" and "OrderDate" match.</summary>
            private static string Key(string? name) => new((name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

            // ---- values ------------------------------------------------------------------------------

            /// <summary>A database value as long, decimal, bool, DateTime, string or null. A value that does not fit the kind stays text.</summary>
            private static object? Normalize(Kind kind, object? raw)
            {
                if (raw is null or DBNull)
                {
                    return null;
                }

                try
                {
                    switch (kind)
                    {
                        case Kind.Integer:
                            if (raw is double or float or decimal && Convert.ToDecimal(raw, Invariant) != Math.Truncate(Convert.ToDecimal(raw, Invariant)))
                            {
                                return Convert.ToString(raw, Invariant);
                            }

                            return Convert.ToInt64(raw, Invariant);
                        case Kind.Decimal:
                            return Convert.ToDecimal(raw, Invariant);
                        case Kind.Boolean:
                            return raw switch
                            {
                                bool flag => flag,
                                string text => text.Trim() is "1" || text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                                _ => Convert.ToInt64(raw, Invariant) != 0,
                            };
                        case Kind.Date:
                        case Kind.DateTime:
                            return raw is DateTimeOffset offset ? offset.DateTime : Convert.ToDateTime(raw, Invariant);
                        default:
                            return Convert.ToString(raw, Invariant);
                    }
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    return Convert.ToString(raw, Invariant);
                }
            }

            /// <summary>The text of a normalized value, as CSV, Word and XML write it.</summary>
            private static string Cell(Col column, object? value) => value switch
            {
                null => string.Empty,
                long number => number.ToString(Invariant),
                decimal number => number.ToString(Invariant),
                bool flag => flag ? "true" : "false",
                DateTime date => date.ToString(column.Kind == Kind.DateTime ? Config.DateFormat + " HH:mm:ss" : Config.DateFormat, Invariant),
                _ => Convert.ToString(value, Invariant) ?? string.Empty,
            };

            // ---- writers -----------------------------------------------------------------------------

            private static byte[] Write(Fmt format, Spec spec, IReadOnlyList<object?[]> rows, bool header)
            {
                var columns = spec.Columns;
                string Text(int index, object? value) => Cell(columns[index], value);
                var headers = header ? columns.Select(HeaderOf).ToList() : new List<string>();
                return format switch
                {
                    Fmt.Csv => WriteCsv(columns, rows, header),
                    Fmt.Json => WriteJson(columns, rows),
                    Fmt.Xml => WriteXml(spec, rows),
                    Fmt.Xlsx => OpenXmlFormats.WriteXlsx(spec.Caption, headers, rows, Text),
                    _ => OpenXmlFormats.WriteWord(spec.Caption, headers, rows, Text),
                };
            }

            /// <summary>Text that starts like a formula would run in a spreadsheet; a leading apostrophe keeps it text. The import takes it off again.</summary>
            private static string Guard(string text) =>
                text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

            private static byte[] WriteCsv(Col[] columns, IReadOnlyList<object?[]> rows, bool header)
            {
                var sb = new StringBuilder();
                var delimiter = Config.Delimiter;
                void Append(IEnumerable<string> cells)
                {
                    var first = true;
                    foreach (var cell in cells)
                    {
                        if (!first)
                        {
                            sb.Append(delimiter);
                        }

                        first = false;
                        var quote = cell.Contains(delimiter) || cell.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || cell.StartsWith(' ') || cell.EndsWith(' ');
                        sb.Append(quote ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell);
                    }

                    sb.Append("\r\n");
                }

                if (header)
                {
                    Append(columns.Select(c => Guard(HeaderOf(c))));
                }

                foreach (var row in rows)
                {
                    Append(Enumerable.Range(0, columns.Length).Select(i => columns[i].Kind == Kind.Text ? Guard(Cell(columns[i], row[i])) : Cell(columns[i], row[i])));
                }

                var encoding = new UTF8Encoding(true);
                return encoding.GetPreamble().Concat(encoding.GetBytes(sb.ToString())).ToArray();
            }

            private static byte[] WriteJson(Col[] columns, IReadOnlyList<object?[]> rows)
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartArray();
                    foreach (var row in rows)
                    {
                        writer.WriteStartObject();
                        for (var i = 0; i < columns.Length; i++)
                        {
                            var name = columns[i].Name;
                            switch (row[i])
                            {
                                case null:
                                    writer.WriteNull(name);
                                    break;
                                case long number:
                                    writer.WriteNumber(name, number);
                                    break;
                                case decimal number:
                                    writer.WriteNumber(name, number);
                                    break;
                                case bool flag:
                                    writer.WriteBoolean(name, flag);
                                    break;
                                default:
                                    writer.WriteString(name, Cell(columns[i], row[i]));
                                    break;
                            }
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                return stream.ToArray();
            }

            private static byte[] WriteXml(Spec spec, IReadOnlyList<object?[]> rows)
            {
                var root = new XElement("rows", new XAttribute("table", spec.Route.TrimStart('/')));
                foreach (var row in rows)
                {
                    var element = new XElement("row");
                    for (var i = 0; i < spec.Columns.Length; i++)
                    {
                        if (row[i] is not null)
                        {
                            element.Add(new XElement(XmlConvert.EncodeLocalName(spec.Columns[i].Name), XmlText(Cell(spec.Columns[i], row[i]))));
                        }
                    }

                    root.Add(element);
                }

                using var stream = new MemoryStream();
                new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(stream);
                return stream.ToArray();
            }

            /// <summary>Drops the characters XML cannot hold.</summary>
            private static string XmlText(string text)
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

            // ---- readers: every format gives rows of text, the first row holds the names for JSON and XML --------

            private static List<string?[]> Read(Fmt format, byte[] data) => format switch
            {
                Fmt.Csv => ReadCsv(data),
                Fmt.Json => ReadJson(data),
                Fmt.Xml => ReadXml(data),
                Fmt.Xlsx => OpenXmlFormats.ReadXlsx(data, MaxRows),
                _ => OpenXmlFormats.ReadWord(data, MaxRows),
            };

            private static string Decode(byte[] data)
            {
                var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
                try
                {
                    return new UTF8Encoding(false, true).GetString(data, start, data.Length - start);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.Latin1.GetString(data, start, data.Length - start);
                }
            }

            /// <summary>The separator that occurs most in the first line outside quotes; the configured one wins a tie.</summary>
            private static char DetectDelimiter(string text)
            {
                var candidates = new[] { Config.Delimiter, ',', ';', '\t', '|' };
                var counts = new int[candidates.Length];
                var quoted = false;
                for (var i = 0; i < text.Length && (quoted || text[i] is not ('\r' or '\n')); i++)
                {
                    if (text[i] == '"')
                    {
                        quoted = !quoted;
                    }
                    else if (!quoted)
                    {
                        var index = Array.IndexOf(candidates, text[i]);
                        if (index >= 0)
                        {
                            counts[index]++;
                        }
                    }
                }

                var best = 0;
                for (var i = 1; i < candidates.Length; i++)
                {
                    if (counts[i] > counts[best])
                    {
                        best = i;
                    }
                }

                return counts[best] == 0 ? Config.Delimiter : candidates[best];
            }

            private static List<string?[]> ReadCsv(byte[] data)
            {
                var text = Decode(data);
                var delimiter = DetectDelimiter(text);
                var rows = new List<string?[]>();
                var row = new List<string?>();
                var cell = new StringBuilder();
                var quoted = false;
                var wasQuoted = false;

                void EndCell()
                {
                    var value = cell.ToString();
                    if (value.Length >= 2 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' or '\t' or '\r')
                    {
                        value = value[1..];
                    }

                    row.Add(value);
                    cell.Clear();
                    wasQuoted = false;
                }

                void EndRow()
                {
                    EndCell();
                    if (row.Any(c => !string.IsNullOrEmpty(c)))
                    {
                        if (rows.Count > MaxRows)
                        {
                            throw new TooManyRowsException();
                        }

                        rows.Add(row.ToArray());
                    }

                    row.Clear();
                }

                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    if (quoted)
                    {
                        if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else if (c == '"')
                        {
                            quoted = false;
                        }
                        else
                        {
                            cell.Append(c);
                        }
                    }
                    else if (c == '"' && cell.Length == 0 && !wasQuoted)
                    {
                        quoted = true;
                        wasQuoted = true;
                    }
                    else if (c == delimiter)
                    {
                        EndCell();
                    }
                    else if (c is '\r' or '\n')
                    {
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            i++;
                        }

                        EndRow();
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }

                if (cell.Length > 0 || wasQuoted || row.Count > 0)
                {
                    EndRow();
                }

                return rows;
            }

            private static string? JsonText(JsonElement value) => value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };

            /// <summary>Rows from a list of names and a list of name/value maps: the names first, in order of appearance.</summary>
            private static List<string?[]> FromObjects(List<string> names, List<Dictionary<string, string?>> objects)
            {
                var rows = new List<string?[]> { names.Cast<string?>().ToArray() };
                foreach (var item in objects)
                {
                    rows.Add(names.Select(n => item.TryGetValue(n, out var value) ? value : null).ToArray());
                }

                return rows;
            }

            private static List<string?[]> ReadJson(byte[] data)
            {
                var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
                using var document = JsonDocument.Parse(data.AsMemory(start), new JsonDocumentOptions { MaxDepth = 16 });
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    root = root.EnumerateObject().Select(p => p.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
                }

                if (root.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("The JSON has no list of rows.");
                }

                var names = new List<string>();
                var known = new HashSet<string>(StringComparer.Ordinal);
                var objects = new List<Dictionary<string, string?>>();
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException("A row is not an object.");
                    }

                    if (objects.Count >= MaxRows)
                    {
                        throw new TooManyRowsException();
                    }

                    var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var property in item.EnumerateObject())
                    {
                        if (known.Add(property.Name))
                        {
                            names.Add(property.Name);
                        }

                        map[property.Name] = JsonText(property.Value);
                    }

                    objects.Add(map);
                }

                return FromObjects(names, objects);
            }

            private static List<string?[]> ReadXml(byte[] data)
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 200_000_000 };
                using var stream = new MemoryStream(data);
                using var reader = XmlReader.Create(stream, settings);
                var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("The XML has no root element.");
                var names = new List<string>();
                var known = new HashSet<string>(StringComparer.Ordinal);
                var objects = new List<Dictionary<string, string?>>();
                foreach (var row in root.Elements())
                {
                    if (objects.Count >= MaxRows)
                    {
                        throw new TooManyRowsException();
                    }

                    var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var field in row.Elements())
                    {
                        var name = XmlConvert.DecodeName(field.Name.LocalName);
                        if (known.Add(name))
                        {
                            names.Add(name);
                        }

                        map[name] = field.Value;
                    }

                    objects.Add(map);
                }

                return FromObjects(names, objects);
            }

            // ---- export ------------------------------------------------------------------------------

            /// <summary>The rows of the list (<paramref name="where"/> and <paramref name="order"/> are those of the list page) as a file download.</summary>
            public static async Task<IResult> Export(Spec spec, Fmt format, SqliteConnection connection, string where, string order, DynamicParameters args)
            {
                var sql = "SELECT " + spec.SelectTop + string.Join(", ", spec.Columns.Select(c => c.Column)) + " FROM " + spec.Table + where + order + spec.LimitTail;
                var rows = new List<object?[]>();
                using (var reader = await connection.ExecuteReaderAsync(sql, args))
                {
                    while (reader.Read())
                    {
                        var row = new object?[spec.Columns.Length];
                        for (var i = 0; i < row.Length; i++)
                        {
                            row[i] = Normalize(spec.Columns[i].Kind, reader.IsDBNull(i) ? null : reader.GetValue(i));
                        }

                        rows.Add(row);
                    }
                }

                return Results.File(Write(format, spec, rows, Config.IncludeHeader), ContentType(format), BaseName(spec) + Extension(format));
            }

            /// <summary>A CSV file with the header row of the import columns.</summary>
            public static IResult Template(Spec spec) =>
                Results.File(WriteCsv(spec.Columns, Array.Empty<object?[]>(), true), ContentType(Fmt.Csv), BaseName(spec) + "-template.csv");

            private static string QueryOf(HttpRequest request)
            {
                var sb = new StringBuilder();
                foreach (var (key, values) in request.Query)
                {
                    if (key is "page" or "format" or "partial")
                    {
                        continue;
                    }

                    foreach (var value in values)
                    {
                        sb.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
                    }
                }

                return sb.ToString();
            }

            /// <summary>The export links and the import button of a list page. An export keeps the search, filters and sort of the page.</summary>
            public static string Toolbar(HttpRequest request, Spec spec, bool exports, bool imports)
            {
                var sb = new StringBuilder("<div class=\"mb-3 d-flex flex-wrap gap-2 align-items-center\">");
                if (exports)
                {
                    var query = QueryOf(request);
                    sb.Append("<span class=\"text-muted small\">").Append(Html.Encode(@@UI:impExport@@)).Append(":</span><div class=\"btn-group btn-group-sm\">");
                    foreach (var format in Config.Export)
                    {
                        sb.Append("<a class=\"btn btn-outline-secondary\" href=\"").Append(Html.Attr(spec.Route + "/export?format=" + format + query)).Append("\">")
                            .Append(Html.Encode(FormatName(format))).Append("</a>");
                    }

                    sb.Append("</div>");
                }

                if (imports)
                {
                    sb.Append("<a class=\"btn btn-sm btn-outline-secondary\" href=\"").Append(Html.Attr(spec.Route + "/import")).Append("\">")
                        .Append(Html.Encode(@@UI:impImport@@)).Append("</a>");
                }

                return sb.Append("</div>").ToString();
            }

            // ---- import: upload ----------------------------------------------------------------------

            public static IResult UploadPage(Spec spec, HttpContext context, IAntiforgery antiforgery, string? error = null)
            {
                var body = new StringBuilder();
                if (error is not null)
                {
                    body.Append("<div class=\"alert alert-danger\">").Append(Html.Encode(error)).Append("</div>");
                }

                var accept = string.Join(",", Config.Import.Select(Extension).Append(Config.Import.Contains(Fmt.Csv) ? ".tsv" : string.Empty).Where(e => e.Length > 0));
                body.Append("<form method=\"post\" action=\"").Append(Html.Attr(spec.Route + "/import")).Append("\" enctype=\"multipart/form-data\">");
                body.Append(Html.AntiforgeryField(context, antiforgery));
                body.Append("<div class=\"mb-3\"><label class=\"form-label\" for=\"file\">").Append(Html.Encode(@@UI:impFile@@))
                    .Append("</label><input class=\"form-control\" type=\"file\" id=\"file\" name=\"file\" accept=\"").Append(Html.Attr(accept)).Append("\" required></div>");
                body.Append(Html.Checkbox("header", @@UI:impHeaderRow@@, Config.IncludeHeader));
                body.Append("<p class=\"text-muted\">").Append(Html.Encode(string.Format(@@UI:impFormats@@, string.Join(", ", Config.Import.Select(FormatName))))).Append("</p>");
                body.Append("<button type=\"submit\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:impUpload@@)).Append("</button> ");
                if (Config.Import.Contains(Fmt.Csv))
                {
                    body.Append(Html.Link(spec.Route + "/import/template", @@UI:impTemplate@@, "btn btn-outline-secondary")).Append(' ');
                }

                body.Append(Html.Link(spec.Route, @@UI:backToList@@, "btn btn-outline-secondary"));
                body.Append("</form>");
                return Html.Page(string.Format(@@UI:impTitle@@, spec.Caption), body.ToString());
            }

            /// <summary>Takes the uploaded file, keeps it for the user until the import (or 30 minutes) and shows the column mapping.</summary>
            public static async Task<IResult> Upload(Spec spec, HttpContext context, IAntiforgery antiforgery, string connectionString)
            {
                // A file over the limit is read (up to four times the limit) and refused with a message below: answering before the
                // browser has sent the body would reset the connection. A body beyond that is refused unread.
                var limit = (long)Config.MaxUploadMb * 1024 * 1024;
                var readLimit = limit * 4 + 64 * 1024;
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                {
                    feature.MaxRequestBodySize = readLimit;
                }

                if (context.Request.ContentLength > readLimit)
                {
                    return UploadPage(spec, context, antiforgery, string.Format(@@UI:impTooLarge@@, Config.MaxUploadMb));
                }

                IFormCollection form;
                try
                {
                    if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                    {
                        return Results.BadRequest();
                    }

                    form = await context.Request.ReadFormAsync();
                }
                catch (Exception ex) when (ex is BadHttpRequestException or InvalidDataException or IOException)
                {
                    return UploadPage(spec, context, antiforgery, string.Format(@@UI:impTooLarge@@, Config.MaxUploadMb));
                }

                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                {
                    return UploadPage(spec, context, antiforgery, @@UI:impNoFile@@);
                }

                if (file.Length > limit)
                {
                    return UploadPage(spec, context, antiforgery, string.Format(@@UI:impTooLarge@@, Config.MaxUploadMb));
                }

                if (FromExtension(file.FileName) is not { } format || !Config.Import.Contains(format))
                {
                    return UploadPage(spec, context, antiforgery, @@UI:impBadType@@);
                }

                byte[] data;
                using (var copy = new MemoryStream())
                {
                    await using var input = file.OpenReadStream();
                    await input.CopyToAsync(copy);
                    data = copy.ToArray();
                }

                var hasHeader = format is Fmt.Json or Fmt.Xml || form["header"].ToString().Length > 0;
                List<string?[]> rows;
                try
                {
                    rows = Read(format, data);
                }
                catch (TooManyRowsException)
                {
                    return UploadPage(spec, context, antiforgery, string.Format(@@UI:impTooMany@@, MaxRows));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    return UploadPage(spec, context, antiforgery, string.Format(@@UI:impUnreadable@@, FormatName(format)));
                }

                if (rows.Count - (hasHeader ? 1 : 0) < 1)
                {
                    return UploadPage(spec, context, antiforgery, @@UI:impNoRows@@);
                }

                Sweep();
                var user = User(context);
                foreach (var old in Uploads.Where(u => u.Value.User == user).OrderBy(u => u.Value.Created).SkipLast(PendingPerUser - 1).ToList())
                {
                    Discard(old.Key);
                }

                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                var folder = UploadFolder;
                Directory.CreateDirectory(folder);
                var path = System.IO.Path.Combine(folder, token + ".bin");
                await File.WriteAllBytesAsync(path, data);
                var pending = new Pending(spec.Route, user, format, hasHeader, System.IO.Path.GetFileName(file.FileName), path, DateTime.UtcNow);
                Uploads[token] = pending;
                return await Preview(spec, context, antiforgery, connectionString, token, pending, rows, DefaultMap(spec, rows, hasHeader), false);
            }

            private static string UploadFolder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "@@NS@@-import");

            /// <summary>Forgets uploads older than their lifetime and deletes leftover files of earlier runs.</summary>
            private static void Sweep()
            {
                foreach (var (token, pending) in Uploads)
                {
                    if (DateTime.UtcNow - pending.Created > UploadLifetime)
                    {
                        Discard(token);
                    }
                }

                try
                {
                    if (Directory.Exists(UploadFolder))
                    {
                        foreach (var file in Directory.EnumerateFiles(UploadFolder).Where(f => DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromHours(1)))
                        {
                            File.Delete(file);
                        }
                    }
                }
                catch (IOException)
                {
                }
            }

            private static void Discard(string token)
            {
                if (Uploads.TryRemove(token, out var pending))
                {
                    try
                    {
                        File.Delete(pending.Path);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            /// <summary>The file column of each field: the one whose name matches the header, or the same position without a header. -1 leaves a field out.</summary>
            private static int[] DefaultMap(Spec spec, List<string?[]> rows, bool hasHeader)
            {
                var map = new int[spec.Columns.Length];
                Array.Fill(map, -1);
                if (!hasHeader)
                {
                    for (var i = 0; i < map.Length && i < rows.Max(r => r.Length); i++)
                    {
                        map[i] = i;
                    }

                    return map;
                }

                var names = rows[0].Select(Key).ToArray();
                var used = new HashSet<int>();
                for (var i = 0; i < map.Length; i++)
                {
                    foreach (var candidate in new[] { HeaderOf(spec.Columns[i]), spec.Columns[i].Name, spec.Columns[i].Caption })
                    {
                        var wanted = Key(candidate);
                        var index = wanted.Length == 0 ? -1 : Array.IndexOf(names, wanted);
                        if (index >= 0 && used.Add(index))
                        {
                            map[i] = index;
                            break;
                        }
                    }
                }

                return map;
            }

            // ---- import: check and commit ------------------------------------------------------------

            /// <summary>Shows the mapping again (action check) or imports (action import) the file of an earlier upload.</summary>
            public static async Task<IResult> Apply(Spec spec, HttpContext context, IAntiforgery antiforgery, string connectionString)
            {
                if (!await Html.IsAntiforgeryValidAsync(context, antiforgery))
                {
                    return Results.BadRequest();
                }

                var form = await context.Request.ReadFormAsync();
                var token = form["token"].ToString();
                if (!TokenPattern.IsMatch(token) || !Uploads.TryGetValue(token, out var pending)
                    || pending.Route != spec.Route || pending.User != User(context) || !File.Exists(pending.Path))
                {
                    return UploadPage(spec, context, antiforgery, @@UI:impExpired@@);
                }

                List<string?[]> rows;
                try
                {
                    rows = Read(pending.Format, await File.ReadAllBytesAsync(pending.Path));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Discard(token);
                    return UploadPage(spec, context, antiforgery, @@UI:impExpired@@);
                }

                var width = rows.Max(r => r.Length);
                var map = new int[spec.Columns.Length];
                for (var i = 0; i < map.Length; i++)
                {
                    map[i] = int.TryParse(form["map" + i], NumberStyles.Integer, Invariant, out var column) && column >= 0 && column < width ? column : -1;
                }

                var skip = form["skip"].ToString().Length > 0;
                if (form["action"].ToString() != "import")
                {
                    return await Preview(spec, context, antiforgery, connectionString, token, pending, rows, map, skip);
                }

                return await Run(spec, context, antiforgery, connectionString, token, pending, rows, map, skip);
            }

            private sealed class RowPlan
            {
                public int Row { get; init; }

                public object?[] Values { get; init; } = Array.Empty<object?>();

                public bool Update { get; set; }

                public List<string> Errors { get; } = new();
            }

            private sealed class Analysis
            {
                public List<string> Blocking { get; } = new();

                public List<RowPlan> Rows { get; } = new();

                public int Valid => Rows.Count(r => r.Errors.Count == 0);

                public int Invalid => Rows.Count - Valid;
            }

            /// <summary>The values a field may hold: the keys of a lookup or the values of a list of options, and the texts that stand for them.</summary>
            private sealed class Allowed
            {
                private readonly HashSet<string> _ambiguous = new();

                public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

                public Dictionary<string, string> Display { get; } = new(StringComparer.Ordinal);

                public void AddDisplay(string text, string key)
                {
                    var lower = text.Trim().ToLowerInvariant();
                    if (lower.Length == 0 || _ambiguous.Contains(lower))
                    {
                        return;
                    }

                    if (Display.TryGetValue(lower, out var existing) && existing != key)
                    {
                        Display.Remove(lower);
                        _ambiguous.Add(lower);
                    }
                    else
                    {
                        Display[lower] = key;
                    }
                }
            }

            private static async Task<Allowed?> LoadAllowed(SqliteConnection connection, Col column)
            {
                if (column.Lookup is { } lookup)
                {
                    var allowed = new Allowed();
                    var sql = "SELECT " + lookup.Key + (lookup.Display.Length > 0 ? ", " + lookup.Display : string.Empty) + " FROM " + lookup.Table
                        + (lookup.Filter.Length > 0 ? " WHERE (" + lookup.Filter + ")" : string.Empty);
                    using var reader = await connection.ExecuteReaderAsync(sql);
                    while (reader.Read())
                    {
                        var key = Convert.ToString(reader.GetValue(0), Invariant) ?? string.Empty;
                        allowed.Keys.Add(key);
                        if (lookup.Display.Length > 0 && !reader.IsDBNull(1))
                        {
                            allowed.AddDisplay(Convert.ToString(reader.GetValue(1), Invariant) ?? string.Empty, key);
                        }
                    }

                    return allowed;
                }

                if (column.Choices.Count > 0)
                {
                    var allowed = new Allowed();
                    foreach (var choice in column.Choices)
                    {
                        allowed.Keys.Add(choice.Value);
                        allowed.AddDisplay(choice.Text, choice.Value);
                    }

                    return allowed;
                }

                return null;
            }

            private static async Task<long> CountRows(Spec spec, SqliteConnection connection, int[] keys, object?[] values, string? owner)
            {
                var args = new DynamicParameters();
                var where = new List<string>();
                for (var n = 0; n < keys.Length; n++)
                {
                    where.Add(spec.Columns[keys[n]].Column + " = @k" + n);
                    args.Add("@k" + n, values[keys[n]]);
                }

                if (owner is not null)
                {
                    where.Add(spec.OwnerColumn + " = @owner");
                    args.Add("@owner", owner);
                }

                return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM " + spec.Table + " WHERE " + string.Join(" AND ", where), args);
            }

            /// <summary>Reads every row of the file with the mapping: converts and checks the values, and finds out which rows add and which update.</summary>
            private static async Task<Analysis> Analyze(Spec spec, List<string?[]> rows, bool hasHeader, int[] map, SqliteConnection connection, string owner)
            {
                var result = new Analysis();
                var columns = spec.Columns;
                if (!Enumerable.Range(0, columns.Length).Any(i => map[i] >= 0 && !columns[i].IsAuto))
                {
                    result.Blocking.Add(@@UI:impNothingMapped@@);
                    return result;
                }

                for (var i = 0; i < columns.Length; i++)
                {
                    if (map[i] >= 0)
                    {
                        continue;
                    }

                    if (spec.Upsert && columns[i].IsKey)
                    {
                        result.Blocking.Add(string.Format(@@UI:impKeyNotMapped@@, columns[i].Caption));
                    }
                    else if (!spec.Upsert && NeedsValue(columns[i]))
                    {
                        result.Blocking.Add(string.Format(@@UI:impNotMapped@@, columns[i].Caption));
                    }
                }

                if (result.Blocking.Count > 0)
                {
                    return result;
                }

                var allowed = new Allowed?[columns.Length];
                for (var i = 0; i < columns.Length; i++)
                {
                    if (map[i] >= 0)
                    {
                        allowed[i] = await LoadAllowed(connection, columns[i]);
                    }
                }

                // The columns that identify a row: every key column to update, the keys the file gives (not an auto-increment one) to add.
                var matchColumns = Enumerable.Range(0, columns.Length).Where(i => columns[i].IsKey && (spec.Upsert || !columns[i].IsAuto)).ToArray();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var r = hasHeader ? 1 : 0; r < rows.Count; r++)
                {
                    var row = rows[r];
                    var plan = new RowPlan { Row = r + 1, Values = new object?[columns.Length] };
                    var any = false;
                    for (var i = 0; i < columns.Length; i++)
                    {
                        if (map[i] < 0)
                        {
                            continue;
                        }

                        var raw = map[i] < row.Length ? row[map[i]] : null;
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            if (NeedsValue(columns[i]))
                            {
                                plan.Errors.Add(string.Format(@@UI:required@@, columns[i].Caption));
                            }

                            continue;
                        }

                        any = true;
                        plan.Values[i] = ConvertCell(columns[i], raw, allowed[i], plan.Errors);
                    }

                    if (!any)
                    {
                        continue;
                    }

                    if (matchColumns.Length > 0 && matchColumns.All(i => map[i] >= 0 && plan.Values[i] is not null))
                    {
                        var keyText = string.Join("\u001f", matchColumns.Select(i => Cell(columns[i], plan.Values[i])));
                        if (!seen.Add(keyText))
                        {
                            plan.Errors.Add(@@UI:impDuplicate@@);
                        }
                        else if (await CountRows(spec, connection, matchColumns, plan.Values, null) > 0)
                        {
                            if (!spec.Upsert || (spec.OwnerColumn.Length > 0 && await CountRows(spec, connection, matchColumns, plan.Values, owner) == 0))
                            {
                                plan.Errors.Add(@@UI:impKeyExists@@);
                            }
                            else
                            {
                                plan.Update = true;
                            }
                        }
                    }
                    else if (spec.Upsert)
                    {
                        foreach (var i in matchColumns.Where(m => map[m] >= 0 && plan.Values[m] is null && !columns[m].Required && !columns[m].IsAuto))
                        {
                            plan.Errors.Add(string.Format(@@UI:impKeyMissing@@, columns[i].Caption));
                        }
                    }

                    if (spec.Upsert && !plan.Update)
                    {
                        for (var i = 0; i < columns.Length; i++)
                        {
                            if (map[i] < 0 && NeedsValue(columns[i]))
                            {
                                plan.Errors.Add(string.Format(@@UI:impNotMapped@@, columns[i].Caption));
                            }
                        }
                    }

                    result.Rows.Add(plan);
                }

                return result;
            }

            /// <summary>True for a field that must have a value in a new row: required, with no default and no automatic value.</summary>
            private static bool NeedsValue(Col column) => column.Required && !column.HasDefault && !column.IsAuto;

            private static object? ConvertCell(Col column, string raw, Allowed? allowed, List<string> errors)
            {
                object? value;
                if (allowed is not null)
                {
                    if (TryKind(column, raw, out value) && allowed.Keys.Contains(Canonical(column, value)))
                    {
                    }
                    else if (allowed.Display.TryGetValue(raw.Trim().ToLowerInvariant(), out var key) && TryKind(column, key, out value))
                    {
                    }
                    else
                    {
                        errors.Add(string.Format(@@UI:impLookup@@, column.Caption, raw.Trim()));
                        return null;
                    }
                }
                else if (!TryKind(column, raw, out value))
                {
                    errors.Add(string.Format(column.Kind switch { Kind.Integer or Kind.Decimal => @@UI:notNumber@@, Kind.Boolean => @@UI:impBool@@, _ => @@UI:notDate@@ }, column.Caption));
                    return null;
                }

                if (Constraint(column, value) is { } problem)
                {
                    errors.Add(problem);
                }

                return value;
            }

            private static string Canonical(Col column, object? value) => value switch
            {
                DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", Invariant),
                bool flag => flag ? "1" : "0",
                _ => Convert.ToString(value, Invariant) ?? string.Empty,
            };

            private static bool TryKind(Col column, string raw, out object? value)
            {
                value = null;
                var text = raw.Trim();
                const NumberStyles number = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
                switch (column.Kind)
                {
                    case Kind.Text:
                        value = raw;
                        return true;
                    case Kind.Integer:
                        if (long.TryParse(text, NumberStyles.AllowLeadingSign, Invariant, out var whole))
                        {
                            value = whole;
                            return true;
                        }

                        if (decimal.TryParse(text, number, Invariant, out var exact) && exact == Math.Truncate(exact) && exact >= long.MinValue && exact <= long.MaxValue)
                        {
                            value = (long)exact;
                            return true;
                        }

                        return false;
                    case Kind.Decimal:
                        if (decimal.TryParse(text, number, Invariant, out var fraction))
                        {
                            value = fraction;
                            return true;
                        }

                        return false;
                    case Kind.Boolean:
                        if (TryBool(text, out var flag))
                        {
                            value = flag;
                            return true;
                        }

                        return false;
                    default:
                        if (TryDate(text, out var date))
                        {
                            value = date;
                            return true;
                        }

                        return false;
                }
            }

            private static bool TryBool(string text, out bool value)
            {
                var lower = text.ToLowerInvariant();
                if (lower is "1" or "true" or "yes" or "y" or "t" or "on" || lower == @@UI:yes@@.ToLowerInvariant())
                {
                    value = true;
                    return true;
                }

                if (lower is "0" or "false" or "no" or "n" or "f" or "off" || lower == @@UI:no@@.ToLowerInvariant())
                {
                    value = false;
                    return true;
                }

                value = false;
                return false;
            }

            /// <summary>A date in the format of the project, ISO 8601, any date the generated forms accept, or an Excel date number.</summary>
            private static bool TryDate(string text, out DateTime value)
            {
                if (double.TryParse(text, NumberStyles.Float, Invariant, out var serial))
                {
                    if (serial >= 1 && serial < 2958466)
                    {
                        value = DateTime.FromOADate(serial);
                        return true;
                    }
                }

                var formats = new[]
                {
                    Config.DateFormat, Config.DateFormat + " HH:mm:ss", Config.DateFormat + " HH:mm", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
                    "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyyMMdd",
                };
                return DateTime.TryParseExact(text, formats, Invariant, DateTimeStyles.None, out value)
                    || DateTime.TryParse(text, Invariant, DateTimeStyles.None, out value);
            }

            private static string? Constraint(Col column, object? value)
            {
                switch (value)
                {
                    case string text:
                        if (column.MaxLength > 0 && text.Length > column.MaxLength)
                        {
                            return string.Format(@@UI:impTooLong@@, column.Caption, column.MaxLength);
                        }

                        if (column.MinLength > 0 && text.Length < column.MinLength)
                        {
                            return string.Format(@@UI:impTooShort@@, column.Caption, column.MinLength);
                        }

                        if (column.Pattern.Length > 0 && !Matches(column.Pattern, text))
                        {
                            return column.PatternMessage.Length > 0 ? column.PatternMessage : string.Format(@@UI:impPattern@@, column.Caption);
                        }

                        return null;
                    case long or decimal:
                        var number = Convert.ToDecimal(value, Invariant);
                        if (decimal.TryParse(column.Min, NumberStyles.Float, Invariant, out var min) && number < min)
                        {
                            return string.Format(@@UI:impMin@@, column.Caption, column.Min);
                        }

                        return decimal.TryParse(column.Max, NumberStyles.Float, Invariant, out var max) && number > max
                            ? string.Format(@@UI:impMax@@, column.Caption, column.Max)
                            : null;
                    case DateTime date:
                        if (DateTime.TryParse(column.Min, Invariant, DateTimeStyles.None, out var from) && date < from)
                        {
                            return string.Format(@@UI:impMin@@, column.Caption, column.Min);
                        }

                        return DateTime.TryParse(column.Max, Invariant, DateTimeStyles.None, out var to) && date > to
                            ? string.Format(@@UI:impMax@@, column.Caption, column.Max)
                            : null;
                    default:
                        return null;
                }
            }

            /// <summary>The pattern of a field must match the whole text, as the pattern attribute of a form does. A pattern that cannot be read is not applied.</summary>
            private static bool Matches(string pattern, string text)
            {
                try
                {
                    return Regex.IsMatch(text, "^(?:" + pattern + ")$", RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    return true;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            private static async Task InsertRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner)
            {
                var args = new DynamicParameters();
                var names = new List<string>();
                var marks = new List<string>();
                for (var i = 0; i < spec.Columns.Length; i++)
                {
                    var column = spec.Columns[i];
                    if (map[i] < 0 || column.IsAuto || (plan.Values[i] is null && column.HasDefault))
                    {
                        continue;
                    }

                    names.Add(column.Column);
                    marks.Add("@p" + i);
                    args.Add("@p" + i, plan.Values[i]);
                }

                if (spec.OwnerColumn.Length > 0)
                {
                    names.Add(spec.OwnerColumn);
                    marks.Add("@owner");
                    args.Add("@owner", owner);
                }

                await connection.ExecuteAsync("INSERT INTO " + spec.Table + " (" + string.Join(", ", names) + ") VALUES (" + string.Join(", ", marks) + ")", args);
            }

            private static async Task UpdateRow(Spec spec, SqliteConnection connection, int[] map, RowPlan plan, string owner)
            {
                var args = new DynamicParameters();
                var sets = new List<string>();
                var where = new List<string>();
                var key = 0;
                for (var i = 0; i < spec.Columns.Length; i++)
                {
                    var column = spec.Columns[i];
                    if (column.IsKey)
                    {
                        where.Add(column.Column + " = @k" + key);
                        args.Add("@k" + key++, plan.Values[i]);
                    }
                    else if (map[i] >= 0)
                    {
                        sets.Add(column.Column + " = @p" + i);
                        args.Add("@p" + i, plan.Values[i]);
                    }
                }

                if (sets.Count == 0)
                {
                    return;
                }

                if (spec.OwnerColumn.Length > 0)
                {
                    where.Add(spec.OwnerColumn + " = @owner");
                    args.Add("@owner", owner);
                }

                await connection.ExecuteAsync("UPDATE " + spec.Table + " SET " + string.Join(", ", sets) + " WHERE " + string.Join(" AND ", where), args);
            }

            /// <summary>Adds and updates the valid rows in one transaction. Any database error rolls everything back.</summary>
            private static async Task<IResult> Run(Spec spec, HttpContext context, IAntiforgery antiforgery, string connectionString, string token, Pending pending, List<string?[]> rows, int[] map, bool skip)
            {
                var owner = Owner(spec, context);
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync("BEGIN IMMEDIATE");
                var added = 0;
                var updated = 0;
                var failedRow = 0;
                Analysis analysis;
                try
                {
                    analysis = await Analyze(spec, rows, pending.HasHeader, map, connection, owner);
                    if (analysis.Blocking.Count > 0 || (!skip && analysis.Invalid > 0))
                    {
                        await connection.ExecuteAsync("ROLLBACK");
                        Discard(token);
                        return Result(spec, analysis, 0, 0, nothing: analysis.Blocking.Count == 0);
                    }

                    foreach (var plan in analysis.Rows.Where(r => r.Errors.Count == 0))
                    {
                        failedRow = plan.Row;
                        if (plan.Update)
                        {
                            await UpdateRow(spec, connection, map, plan, owner);
                            updated++;
                        }
                        else
                        {
                            await InsertRow(spec, connection, map, plan, owner);
                            added++;
                        }
                    }

                    await connection.ExecuteAsync("COMMIT");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    try
                    {
                        await connection.ExecuteAsync("ROLLBACK");
                    }
                    catch (Exception)
                    {
                    }

                    Discard(token);
                    return Result(spec, new Analysis(), 0, 0, failedRow);
                }

                Discard(token);
                return Result(spec, analysis, added, updated);
            }

            // ---- import: pages -----------------------------------------------------------------------

            private static void AppendErrors(StringBuilder body, Analysis analysis)
            {
                var invalid = analysis.Rows.Where(r => r.Errors.Count > 0).ToList();
                if (invalid.Count == 0)
                {
                    return;
                }

                body.Append("<ul class=\"small\">");
                foreach (var plan in invalid.Take(ErrorsShown))
                {
                    body.Append("<li>").Append(Html.Encode(string.Format(@@UI:impRowError@@, plan.Row, string.Join(" ", plan.Errors)))).Append("</li>");
                }

                body.Append("</ul>");
                if (invalid.Count > ErrorsShown)
                {
                    body.Append("<p class=\"small text-muted\">").Append(Html.Encode(string.Format(@@UI:impMoreErrors@@, invalid.Count - ErrorsShown))).Append("</p>");
                }
            }

            /// <summary>The page after an import. <paramref name="failedRow"/> is 0 or more when the database refused the import; <paramref name="nothing"/> when errors stopped it.</summary>
            private static IResult Result(Spec spec, Analysis analysis, int added, int updated, int failedRow = -1, bool nothing = false)
            {
                var body = new StringBuilder();
                if (failedRow >= 0)
                {
                    body.Append("<div class=\"alert alert-danger\">").Append(Html.Encode(string.Format(@@UI:impFailed@@, failedRow > 0 ? failedRow.ToString(Invariant) : "-"))).Append("</div>");
                }
                else if (analysis.Blocking.Count > 0)
                {
                    foreach (var message in analysis.Blocking)
                    {
                        body.Append("<div class=\"alert alert-warning\">").Append(Html.Encode(message)).Append("</div>");
                    }
                }
                else if (nothing)
                {
                    body.Append("<div class=\"alert alert-warning\">").Append(Html.Encode(@@UI:impNothing@@)).Append("</div>");
                    AppendErrors(body, analysis);
                }
                else
                {
                    body.Append("<div class=\"alert alert-success\">").Append(Html.Encode(string.Format(@@UI:impResult@@, added, updated, analysis.Invalid))).Append("</div>");
                    AppendErrors(body, analysis);
                }

                body.Append(Html.Link(spec.Route, @@UI:backToList@@, "btn btn-primary")).Append(' ')
                    .Append(Html.Link(spec.Route + "/import", @@UI:impAnother@@, "btn btn-outline-secondary"));
                return Html.Page(@@UI:impDone@@, body.ToString());
            }

            private static string Shorten(string? text) => text is null ? string.Empty : text.Length > 60 ? text[..60] + "..." : text;

            private static async Task<IResult> Preview(Spec spec, HttpContext context, IAntiforgery antiforgery, string connectionString, string token, Pending pending, List<string?[]> rows, int[] map, bool skip)
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                var analysis = await Analyze(spec, rows, pending.HasHeader, map, connection, Owner(spec, context));
                var width = rows.Max(r => r.Length);
                string Label(int index) => pending.HasHeader && !string.IsNullOrWhiteSpace(rows[0][index]) ? rows[0][index]!.Trim() : "#" + (index + 1);

                var body = new StringBuilder();
                body.Append("<p>").Append(Html.Encode(string.Format(@@UI:impRowsFound@@, rows.Count - (pending.HasHeader ? 1 : 0), pending.FileName))).Append("</p>");
                if (analysis.Blocking.Count > 0)
                {
                    foreach (var message in analysis.Blocking)
                    {
                        body.Append("<div class=\"alert alert-warning\">").Append(Html.Encode(message)).Append("</div>");
                    }
                }
                else
                {
                    body.Append("<div class=\"alert alert-").Append(analysis.Invalid == 0 ? "success" : "warning").Append("\">")
                        .Append(Html.Encode(string.Format(@@UI:impSummary@@, analysis.Valid, analysis.Invalid))).Append("</div>");
                    AppendErrors(body, analysis);
                }

                body.Append("<form method=\"post\" action=\"").Append(Html.Attr(spec.Route + "/import/apply")).Append("\">");
                body.Append(Html.AntiforgeryField(context, antiforgery)).Append(Html.Hidden("token", token));
                body.Append("<h2 class=\"h5\">").Append(Html.Encode(@@UI:impMapping@@)).Append("</h2>");
                body.Append("<table class=\"table table-sm w-auto\"><thead><tr><th>").Append(Html.Encode(@@UI:impField@@)).Append("</th><th>")
                    .Append(Html.Encode(@@UI:impColumn@@)).Append("</th></tr></thead><tbody>");
                for (var i = 0; i < spec.Columns.Length; i++)
                {
                    body.Append("<tr><td>").Append(Html.Encode(spec.Columns[i].Caption)).Append(spec.Columns[i].Required ? " *" : string.Empty)
                        .Append("</td><td><select class=\"form-select form-select-sm\" name=\"map").Append(i).Append("\"><option value=\"-1\">")
                        .Append(Html.Encode(@@UI:impMapNone@@)).Append("</option>");
                    for (var j = 0; j < width; j++)
                    {
                        body.Append("<option value=\"").Append(j).Append('"').Append(map[i] == j ? " selected" : string.Empty).Append('>').Append(Html.Encode(Label(j))).Append("</option>");
                    }

                    body.Append("</select></td></tr>");
                }

                body.Append("</tbody></table>");
                body.Append(Html.Checkbox("skip", @@UI:impSkipInvalid@@, skip));
                body.Append("<button type=\"submit\" name=\"action\" value=\"check\" class=\"btn btn-outline-primary\">").Append(Html.Encode(@@UI:impCheck@@)).Append("</button> ");
                body.Append("<button type=\"submit\" name=\"action\" value=\"import\" class=\"btn btn-primary\">").Append(Html.Encode(@@UI:impImport@@)).Append("</button> ");
                body.Append(Html.Link(spec.Route, @@UI:cancel@@, "btn btn-outline-secondary"));
                body.Append("</form>");

                body.Append("<h2 class=\"h5 mt-4\">").Append(Html.Encode(@@UI:impPreview@@)).Append("</h2>");
                body.Append("<div class=\"table-responsive\"><table class=\"table table-sm table-bordered\"><thead><tr>");
                for (var j = 0; j < width; j++)
                {
                    body.Append("<th>").Append(Html.Encode(Label(j))).Append("</th>");
                }

                body.Append("</tr></thead><tbody>");
                foreach (var row in rows.Skip(pending.HasHeader ? 1 : 0).Take(PreviewRows))
                {
                    body.Append("<tr>");
                    for (var j = 0; j < width; j++)
                    {
                        body.Append("<td>").Append(Html.Encode(Shorten(j < row.Length ? row[j] : null))).Append("</td>");
                    }

                    body.Append("</tr>");
                }

                body.Append("</tbody></table></div>");
                return Html.Page(string.Format(@@UI:impTitle@@, spec.Caption), body.ToString());
            }
        }
        """;
}
