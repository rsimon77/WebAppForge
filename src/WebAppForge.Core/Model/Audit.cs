namespace WebAppForge.Core.Model;

/// <summary>Which column values an audit entry keeps.</summary>
public enum AuditValues
{
    /// <summary>Only who changed which record, and when.</summary>
    None,

    /// <summary>The values of a new or deleted record, and the old and new value of each field an update changed.</summary>
    Changed,

    /// <summary>The values of a new or deleted record, and the old and new value of every field on an update.</summary>
    All,
}

/// <summary>
/// The audit trail of the generated site: every insert, update and delete of the audited tables writes one entry (time, user,
/// table, action, record key and the values) to <see cref="LogTable"/>, in the same transaction as the change.
/// </summary>
public sealed class AuditSettings
{
    public const string DefaultLogTable = "audit_trail";

    public bool Enabled { get; set; }

    /// <summary>Name of the table that holds the entries.</summary>
    public string LogTable { get; set; } = DefaultLogTable;

    /// <summary>The site creates <see cref="LogTable"/> at startup when it does not exist.</summary>
    public bool CreateTable { get; set; } = true;

    public AuditValues Values { get; set; } = AuditValues.Changed;

    /// <summary>Also store the client's IP address.</summary>
    public bool RecordIpAddress { get; set; }

    /// <summary>A page that lists the entries, with filters, at /audit-trail.</summary>
    public bool ShowLogPage { get; set; } = true;

    /// <summary>Lowest user level that may open the log page (with User Level security). 0 means any signed-in user.</summary>
    public int LogPageMinLevel { get; set; }

    public bool IsDefault =>
        !Enabled && LogTable == DefaultLogTable && CreateTable && Values == AuditValues.Changed && !RecordIpAddress && ShowLogPage && LogPageMinLevel == 0;
}

/// <summary>The audit options of one table. Used only when <see cref="AuditSettings.Enabled"/> is set.</summary>
public sealed class TableAudit
{
    public bool Enabled { get; set; }

    public bool Inserts { get; set; } = true;

    public bool Updates { get; set; } = true;

    public bool Deletes { get; set; } = true;

    /// <summary>Fields whose values are never written to the trail (secret fields never are). The change itself is still logged.</summary>
    public List<string> ExcludedFields { get; } = [];

    public bool IsDefault => !Enabled && Inserts && Updates && Deletes && ExcludedFields.Count == 0;
}
