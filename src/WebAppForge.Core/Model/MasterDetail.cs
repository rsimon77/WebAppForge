namespace WebAppForge.Core.Model;

// Master/detail and multi-page form contract shared by the generator and the editor.
// Defaults reproduce the output generated before these features existed.

/// <summary>What happens to detail rows when their master row is deleted.</summary>
public enum DeleteRule
{
    /// <summary>Refuse to delete the master while detail rows exist and say so on the page.</summary>
    Restrict,

    /// <summary>Delete the detail rows together with the master, in one transaction.</summary>
    Cascade,

    /// <summary>Set the detail link columns to NULL (they must be nullable) in one transaction.</summary>
    SetNull,

    /// <summary>Do nothing; the database's own foreign key rules (if any) apply.</summary>
    None,
}

/// <summary>What happens to detail rows when a linked master field changes.</summary>
public enum UpdateRule
{
    None,

    /// <summary>Update the detail link columns in the same transaction. Only relevant for editable (non-key) link fields.</summary>
    Cascade,
}

/// <summary>How the detail rows of a master record are previewed on the master's list page.</summary>
public enum DetailPreview
{
    None,

    /// <summary>A row under the master record expands to show the detail rows.</summary>
    ExpandableRow,

    Modal,

    Offcanvas,
}

/// <summary>Pairs a master field with the detail field that references it.</summary>
public sealed class KeyLink
{
    public string MasterField { get; set; } = string.Empty;

    public string DetailField { get; set; } = string.Empty;
}

/// <summary>
/// A detail table of a master table. A detail row belongs to a master row when every link pair is equal.
/// One master table can have several detail links (multiple master/detail).
/// </summary>
public sealed class DetailLink
{
    public string DetailTable { get; set; } = string.Empty;

    public List<KeyLink> Links { get; } = [];

    /// <summary>Heading of the detail grid. Empty = the detail table's caption.</summary>
    public string Caption { get; set; } = string.Empty;

    /// <summary>Show the detail grid on the master's view page.</summary>
    public bool ShowOnView { get; set; } = true;

    /// <summary>Show the detail grid (with row actions) on the master's edit page.</summary>
    public bool ShowOnEdit { get; set; } = true;

    /// <summary>After a new master record is saved, open its edit page so detail rows can be added.</summary>
    public bool ShowOnAdd { get; set; }

    public DetailPreview Preview { get; set; } = DetailPreview.None;

    public DeleteRule OnDelete { get; set; } = DeleteRule.Restrict;

    public UpdateRule OnUpdate { get; set; } = UpdateRule.None;

    /// <summary>Detail fields shown in the grid, in order. Empty = the detail fields with ShowInList.</summary>
    public List<string> Columns { get; } = [];

    public int PageSize { get; set; } = 10;

    /// <summary>Row actions available from the master pages. Authorization of the detail table still applies.</summary>
    public bool AllowAdd { get; set; } = true;

    public bool AllowEdit { get; set; } = true;

    public bool AllowDelete { get; set; } = true;

    /// <summary>A quick filter box, sortable column headers and a reset link above the grid on the master's view page.</summary>
    public bool FilterAndSort { get; set; }

    /// <summary>Name of the form page (see <see cref="FormPageDef"/>) that hosts the grid. Empty = below the form.</summary>
    public string FormPage { get; set; } = string.Empty;
}

/// <summary>A foreign key read from the database (metadata, used to suggest master/detail links and lookups).</summary>
public sealed class ForeignKeyInfo
{
    /// <summary>Columns of the table that holds the key (the detail side).</summary>
    public List<string> Columns { get; } = [];

    public string ReferencedTable { get; set; } = string.Empty;

    /// <summary>Referenced columns in the same order as <see cref="Columns"/>.</summary>
    public List<string> ReferencedColumns { get; } = [];

    public string OnDelete { get; set; } = string.Empty;

    public string OnUpdate { get; set; } = string.Empty;
}

/// <summary>How the fields of the view, add, copy and edit pages are split into pages.</summary>
public enum MultiPageMode
{
    /// <summary>One page with every field (the default).</summary>
    None,

    Tabs,

    Pills,

    Accordion,

    /// <summary>Step by step: Previous and Next buttons, a step indicator, and Save on the last step.</summary>
    Wizard,
}

/// <summary>One page of a multi-page form. Fields choose a page through <see cref="FieldUi.FormPage"/>.</summary>
public sealed class FormPageDef
{
    public string Name { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    /// <summary>Bootstrap Icons name (without the "bi-" prefix). Empty = none.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Muted text under the page heading.</summary>
    public string Description { get; set; } = string.Empty;
}
