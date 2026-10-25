using System;
using System.Collections.Generic;
using System.Text;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.Services;

/// <summary>One line of the translations CSV: key, language and translated text.</summary>
public sealed record TranslationCsvRecord(string Key, string Language, string Text);

/// <summary>Reads and writes the translations CSV (header key,language,text; RFC 4180 quoting, so texts may hold commas and line breaks).</summary>
public static class TranslationCsv
{
    public const string Header = "key,language,text";

    public static string Write(IEnumerable<TranslationCsvRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append(Header).Append("\r\n");
        foreach (var record in records)
        {
            builder.Append(Quote(record.Key)).Append(',').Append(Quote(record.Language)).Append(',').Append(Quote(record.Text)).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>Parses CSV text. The header row is skipped. A malformed line (fewer than three columns) is reported in <paramref name="problems"/>.</summary>
    public static IReadOnlyList<TranslationCsvRecord> Parse(string csv, out IReadOnlyList<string> problems)
    {
        var records = new List<TranslationCsvRecord>();
        var issues = new List<string>();
        var rows = ReadRows(csv ?? string.Empty);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 1 && row[0].Length == 0)
            {
                continue;
            }

            if (i == 0 && row.Count >= 3
                && string.Equals(row[0].Trim().TrimStart('﻿'), "key", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row[1].Trim(), "language", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row.Count < 3)
            {
                issues.Add(Localizer.Instance.Format("TranslationCsv_RowHasColumnSExpectedKey", i + 1, row.Count));
                continue;
            }

            records.Add(new TranslationCsvRecord(row[0].Trim(), row[1].Trim(), row[2]));
        }

        problems = issues;
        return records;
    }

    private static string Quote(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || value.StartsWith(' ') || value.EndsWith(' ')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static List<List<string>> ReadRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    any = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    any = false;
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }

        if (any || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
