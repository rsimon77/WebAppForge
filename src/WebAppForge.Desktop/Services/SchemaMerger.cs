using System;
using System.Collections.Generic;
using System.Linq;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.Services;

public sealed record SchemaMergeResult(int Added, IReadOnlyList<string> Removed, int Total);

/// <summary>
/// Merges tables read from the database into a project. Existing tables keep their settings;
/// existing fields keep their caption, type and display flags, and take key/identity/required
/// flags from the database. Tables that no longer exist are dropped.
/// </summary>
public static class SchemaMerger
{
    public static SchemaMergeResult Merge(Project project, IReadOnlyList<TableInfo> discovered)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(discovered);

        var existing = project.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var discoveredNames = discovered.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var merged = new List<TableInfo>(discovered.Count);
        var added = 0;
        foreach (var fresh in discovered)
        {
            if (existing.TryGetValue(fresh.Name, out var kept))
            {
                MergeFields(kept, fresh);
                merged.Add(kept);
            }
            else
            {
                merged.Add(fresh);
                added++;
            }
        }

        var removed = existing.Keys
            .Where(name => !discoveredNames.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        project.Tables.Clear();
        project.Tables.AddRange(merged);

        return new SchemaMergeResult(added, removed, merged.Count);
    }

    private static void MergeFields(TableInfo kept, TableInfo fresh)
    {
        var previous = kept.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldInfo>(fresh.Fields.Count);

        foreach (var field in fresh.Fields)
        {
            if (previous.TryGetValue(field.Name, out var settings))
            {
                settings.IsPrimaryKey = field.IsPrimaryKey;
                settings.KeyPosition = field.KeyPosition;
                settings.IsAutoIncrement = field.IsAutoIncrement;
                settings.IsRequired = field.IsRequired;
                fields.Add(settings);
            }
            else
            {
                fields.Add(field);
            }
        }

        kept.Fields.Clear();
        kept.Fields.AddRange(fields);
        kept.Sort.RemoveAll(sort => !fields.Any(f => string.Equals(f.Name, sort.Field, StringComparison.OrdinalIgnoreCase)));
    }
}
