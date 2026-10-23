using System;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>The row category of a table on the Tables tab. Each category has its own background.</summary>
public enum TableRowCategory
{
    /// <summary>Generated table with a primary key.</summary>
    Normal,

    /// <summary>Generate is off: the table is not written, so its row is muted.</summary>
    NotGenerated,

    /// <summary>Generated table with no primary key: the generated pages cannot edit or delete single rows.</summary>
    NoPrimaryKey,
}

public static class TableRowRules
{
    /// <summary>
    /// A table that is not generated is muted whatever its key. Otherwise a table with no primary key is warned.
    /// </summary>
    public static TableRowCategory Categorize(TableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!table.Generate)
        {
            return TableRowCategory.NotGenerated;
        }

        return table.PrimaryKey is null ? TableRowCategory.NoPrimaryKey : TableRowCategory.Normal;
    }
}
