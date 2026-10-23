namespace WebAppForge.Core.Model;

// Customization contract shared by the generator and the editor. Every property has a default that reproduces
// the output generated before customization existed, so projects that never set any of these are unchanged.

/// <summary>Which edit control a field uses in the add, copy and edit forms.</summary>
public enum EditControl
{
    /// <summary>Derived from the field type (text box, number, date, checkbox, ...).</summary>
    Auto,
    TextBox,
    TextArea,
    Password,
    Email,
    Url,
    Phone,
    Number,
    Date,
    DateTime,
    Time,
    Checkbox,
    /// <summary>Drop-down list; options come from <see cref="FieldUi.Options"/> or <see cref="FieldUi.Lookup"/>.</summary>
    Select,
    /// <summary>Radio button group; options come from <see cref="FieldUi.Options"/> or <see cref="FieldUi.Lookup"/>.</summary>
    Radio,
    Color,
    Hidden,
    /// <summary>Shown as text and not editable in the form.</summary>
    ReadOnly,
}

/// <summary>How a field value is rendered on the list and view pages.</summary>
public enum ViewFormat
{
    Auto,
    Text,
    /// <summary>Value is rendered as encoded text with line breaks kept.</summary>
    MultilineText,
    Number,
    Currency,
    Percent,
    Date,
    DateTime,
    Time,
    /// <summary>Yes/No (or the labels in <see cref="FieldUi.TrueText"/> and <see cref="FieldUi.FalseText"/>).</summary>
    Boolean,
    /// <summary>Value is an image URL; rendered as an img tag.</summary>
    Image,
    /// <summary>Value is a URL; rendered as a link.</summary>
    Link,
    /// <summary>Value is an e-mail address; rendered as a mailto link.</summary>
    EmailLink,
    /// <summary>Rendered as a coloured badge using <see cref="FieldUi.BadgeRules"/>.</summary>
    Badge,
}

public enum TextAlign
{
    Auto,
    Left,
    Center,
    Right,
}

public enum ConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    LessThan,
    IsEmpty,
    IsNotEmpty,
}

/// <summary>A static option for a select or radio field.</summary>
public sealed class FieldOption
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>Binds a select or radio field to rows of another table (a lookup).</summary>
public sealed class FieldLookup
{
    public string Table { get; set; } = string.Empty;

    /// <summary>Column in <see cref="Table"/> whose value is stored in this field.</summary>
    public string KeyField { get; set; } = string.Empty;

    /// <summary>Column in <see cref="Table"/> shown to the user.</summary>
    public string DisplayField { get; set; } = string.Empty;

    /// <summary>Optional SQL condition (without WHERE) restricting the lookup rows.</summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>Optional ORDER BY column; empty means the display field.</summary>
    public string OrderField { get; set; } = string.Empty;

    /// <summary>When true the list and view pages show the display text instead of the stored key.</summary>
    public bool ShowDisplayInView { get; set; } = true;
}

/// <summary>Colours a value on the list or view page when the condition matches.</summary>
public sealed class ValueRule
{
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    public string Value { get; set; } = string.Empty;

    /// <summary>Bootstrap contextual name (primary, secondary, success, danger, warning, info, light, dark).</summary>
    public string Style { get; set; } = "secondary";

    /// <summary>Optional display text replacing the value when the rule matches.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>Everything about how one field looks, behaves and validates.</summary>
public sealed class FieldUi
{
    // ---- editing ----
    public EditControl Control { get; set; } = EditControl.Auto;

    public string HelpText { get; set; } = string.Empty;

    public string Placeholder { get; set; } = string.Empty;

    public string Tooltip { get; set; } = string.Empty;

    /// <summary>Initial value in the add form (text; parsed according to the field type). Empty = none.</summary>
    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>When true the field is shown read-only in the edit form but editable when adding.</summary>
    public bool ReadOnlyOnEdit { get; set; }

    public bool AutoFocus { get; set; }

    /// <summary>Rows for a text area.</summary>
    public int Rows { get; set; } = 3;

    public List<FieldOption> Options { get; } = [];

    public FieldLookup? Lookup { get; set; }

    // ---- validation (server side; mirrored as HTML attributes) ----
    public int MaxLength { get; set; }

    public int MinLength { get; set; }

    /// <summary>Empty = no minimum / maximum.</summary>
    public string Min { get; set; } = string.Empty;

    public string Max { get; set; } = string.Empty;

    public string Step { get; set; } = string.Empty;

    /// <summary>Regular expression the value must match. Empty = none.</summary>
    public string Pattern { get; set; } = string.Empty;

    public string PatternMessage { get; set; } = string.Empty;

    public string RequiredMessage { get; set; } = string.Empty;

    // ---- display ----
    public ViewFormat Format { get; set; } = ViewFormat.Auto;

    /// <summary>.NET format string (for example "N2", "C", "yyyy-MM-dd"). Empty = the format's default.</summary>
    public string FormatString { get; set; } = string.Empty;

    public string TrueText { get; set; } = string.Empty;

    public string FalseText { get; set; } = string.Empty;

    public List<ValueRule> BadgeRules { get; } = [];

    /// <summary>CSS width such as "120px" or "20%". Empty = automatic.</summary>
    public string Width { get; set; } = string.Empty;

    public TextAlign Align { get; set; } = TextAlign.Auto;

    /// <summary>Extra CSS classes on the table cell and the input.</summary>
    public string CssClass { get; set; } = string.Empty;

    /// <summary>Inline style for the table cell and the input.</summary>
    public string Style { get; set; } = string.Empty;

    // ---- behaviour ----
    public bool Sortable { get; set; } = true;

    /// <summary>Column group heading shown above related fields on forms. Empty = no group.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Name of the form page this field is placed on (see TableInfo.FormPages). Empty = the first page.</summary>
    public string FormPage { get; set; } = string.Empty;
}

public sealed class RowStyleRule
{
    public string Field { get; set; } = string.Empty;

    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    public string Value { get; set; } = string.Empty;

    /// <summary>Bootstrap table row context (primary, success, danger, warning, info, secondary, light, dark).</summary>
    public string Style { get; set; } = "warning";
}

/// <summary>Table-level look and menu placement.</summary>
public sealed class TableUi
{
    public string Description { get; set; } = string.Empty;

    /// <summary>Bootstrap Icons name (without the "bi-" prefix), shown in the menu and page header.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Home page and menu group. Empty = ungrouped.</summary>
    public string MenuGroup { get; set; } = string.Empty;

    public bool ShowInMenu { get; set; } = true;

    public int MenuOrder { get; set; }

    public string CssClass { get; set; } = string.Empty;

    public List<RowStyleRule> RowStyles { get; } = [];
}

public enum FormLayout
{
    Vertical,
    Horizontal,
    TwoColumns,
    ThreeColumns,
}

public enum ListLayout
{
    Table,
    Cards,
    /// <summary>Table with smaller padding.</summary>
    Compact,
}

public enum PageContainer
{
    Page,
    /// <summary>Opened from the list page in a modal dialog.</summary>
    Modal,
    /// <summary>Opened from the list page in an offcanvas side panel.</summary>
    Offcanvas,
}

public enum ButtonPlacement
{
    Top,
    Bottom,
    Both,
}

/// <summary>A column on the list page, in display order.</summary>
public sealed class ListColumn
{
    public string Field { get; set; } = string.Empty;

    /// <summary>Overrides the field width for this page only. Empty = field setting.</summary>
    public string Width { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;
}

/// <summary>Designs one page type of a table. Defaults reproduce the standard generated page.</summary>
public sealed class PageDesign
{
    /// <summary>Overrides the page heading. Empty = the generated heading.</summary>
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    /// <summary>HTML placed before and after the page content. Authored by the project owner and emitted as written.</summary>
    public string HeaderHtml { get; set; } = string.Empty;

    public string FooterHtml { get; set; } = string.Empty;

    public string CssClass { get; set; } = string.Empty;

    /// <summary>CSS emitted in a style element on this page only.</summary>
    public string CustomCss { get; set; } = string.Empty;

    /// <summary>JavaScript emitted at the end of this page only.</summary>
    public string CustomScript { get; set; } = string.Empty;

    /// <summary>Only the add, edit, view, delete and search pages can open as a modal or offcanvas from the list page.</summary>
    public PageContainer Container { get; set; } = PageContainer.Page;

    // ---- forms (add, copy, edit, search) ----
    public FormLayout FormLayout { get; set; } = FormLayout.Vertical;

    public string SubmitText { get; set; } = string.Empty;

    public string CancelText { get; set; } = string.Empty;

    public bool ShowCancel { get; set; } = true;

    public string SubmitCssClass { get; set; } = string.Empty;

    public ButtonPlacement Buttons { get; set; } = ButtonPlacement.Bottom;

    /// <summary>Splits the view, add, copy and edit pages into the pages defined by TableInfo.FormPages.</summary>
    public MultiPageMode MultiPage { get; set; } = MultiPageMode.None;

    /// <summary>Message shown after a successful save (empty = none).</summary>
    public string SuccessMessage { get; set; } = string.Empty;

    /// <summary>Ask before saving.</summary>
    public bool ConfirmBeforeSave { get; set; }

    // ---- list page ----
    public ListLayout ListLayout { get; set; } = ListLayout.Table;

    /// <summary>Empty = every field with ShowInList, in field order.</summary>
    public List<ListColumn> Columns { get; } = [];

    public bool ShowRowNumbers { get; set; }

    public bool ShowPager { get; set; } = true;

    /// <summary>Page sizes the user can choose from. Empty = no selector.</summary>
    public List<int> PageSizeOptions { get; } = [];

    public bool StickyHeader { get; set; }

    public bool Striped { get; set; } = true;

    public bool Hover { get; set; } = true;

    public bool Bordered { get; set; }

    public bool ShowActionsFirst { get; set; }

    public string AddButtonText { get; set; } = string.Empty;

    public string EmptyText { get; set; } = string.Empty;
}

/// <summary>One design per page type of a table.</summary>
public sealed class PageDesigns
{
    public PageDesign List { get; set; } = new();

    public PageDesign View { get; set; } = new();

    public PageDesign Add { get; set; } = new();

    public PageDesign Copy { get; set; } = new();

    public PageDesign Edit { get; set; } = new();

    public PageDesign Delete { get; set; } = new();

    public PageDesign Search { get; set; } = new();
}

/// <summary>Project-wide metadata and runtime options for the generated site.</summary>
public sealed class ProjectMetadata
{
    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string Copyright { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    /// <summary>Empty = derived from the project name.</summary>
    public string RootNamespace { get; set; } = string.Empty;

    /// <summary>Empty = derived from the project name.</summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Development or Production.</summary>
    public string Environment { get; set; } = "Development";

    public int HttpPort { get; set; } = 5000;

    public int HttpsPort { get; set; } = 5001;

    public bool UseHttps { get; set; }

    public bool EnableHsts { get; set; }

    public bool EnableResponseCompression { get; set; } = true;

    /// <summary>Adds a /health endpoint that returns 200 and checks the database connection.</summary>
    public bool EnableHealthEndpoint { get; set; }

    /// <summary>Default log level written to appsettings.json (Trace, Debug, Information, Warning, Error).</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>Favicon path relative to the project file. Empty = none.</summary>
    public string FaviconPath { get; set; } = string.Empty;

    /// <summary>Logo path relative to the project file, shown in the page header. Empty = none.</summary>
    public string LogoPath { get; set; } = string.Empty;

    /// <summary>Footer text on every page.</summary>
    public string FooterText { get; set; } = string.Empty;

    /// <summary>Extra CSS emitted on every page.</summary>
    public string GlobalCss { get; set; } = string.Empty;

    /// <summary>Extra JavaScript emitted on every page.</summary>
    public string GlobalScript { get; set; } = string.Empty;

    /// <summary>Bootstrap colour mode used by the generated site: auto, light or dark.</summary>
    public string ColorMode { get; set; } = "auto";

    /// <summary>Bootstrap contextual colour for the navigation bar (primary, dark, light, ...).</summary>
    public string NavbarStyle { get; set; } = "light";
}
