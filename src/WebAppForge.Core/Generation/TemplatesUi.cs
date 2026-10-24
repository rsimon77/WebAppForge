using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Generation;

/// <summary>
/// Generator side of field, table and project customization (see Model\Customization.cs). Every function here is reached
/// only for a field, table or project that sets something; defaults take the original code paths so their output is unchanged.
/// </summary>
internal static partial class Templates
{
    // ---- what is customized ---------------------------------------------------------------------

    /// <summary>True when the field sets anything in <see cref="FieldUi"/>.</summary>
    internal static bool IsCustom(FieldInfo field) => !field.Ui.IsDefaultExceptFormPage();

    /// <summary>
    /// True when the table or any of its fields sets anything, or its list page has a design. Only such a table gets the customized
    /// list page, which is the one that offers sorting (column headers, or the sort bar of the Cards layout). <see cref="FieldUi.Sortable"/>
    /// is true by default, so sorting cannot be on for tables that customize and design nothing.
    /// </summary>
    internal static bool IsCustomTable(TableInfo table) => !table.Ui.IsDefault() || table.Fields.Any(IsCustom) || !table.Designs.List.IsDefaultExceptMultiPage();

    /// <summary>The lookup of a field when it names a table, a key column and a display column; otherwise null.</summary>
    private static FieldLookup? LookupOf(FieldInfo field)
    {
        var lookup = field.Ui.Lookup;
        return lookup is not null && lookup.Table.Length > 0 && lookup.KeyField.Length > 0 && lookup.DisplayField.Length > 0 ? lookup : null;
    }

    /// <summary>The edit control a field really gets. Boolean fields are check boxes (or read-only text); other types never get a check box.</summary>
    private static EditControl ControlOf(FieldInfo field)
    {
        var control = field.Ui.Control;
        if (field.Type == FieldType.Boolean)
        {
            return control == EditControl.ReadOnly ? control : EditControl.Checkbox;
        }

        if (control == EditControl.Checkbox)
        {
            control = EditControl.Auto;
        }

        if ((LookupOf(field) is not null || field.Ui.Options.Count > 0)
            && (control == EditControl.Auto || control == EditControl.TextBox || control == EditControl.Select))
        {
            return EditControl.Select;
        }

        return control;
    }

    private static string HtmlControl(EditControl control, FieldType type) => control switch
    {
        EditControl.TextArea => "textarea",
        EditControl.Password => "password",
        EditControl.Email => "email",
        EditControl.Url => "url",
        EditControl.Phone => "tel",
        EditControl.Number => "number",
        EditControl.Date => "date",
        EditControl.DateTime => "datetime-local",
        EditControl.Time => "time",
        EditControl.Checkbox => "checkbox",
        EditControl.Select => "select",
        EditControl.Radio => "radio",
        EditControl.Color => "color",
        EditControl.Hidden => "hidden",
        EditControl.ReadOnly => "readonly",
        _ => type switch
        {
            FieldType.Integer or FieldType.Decimal => "number",
            FieldType.Date => "date",
            FieldType.DateTime => "datetime-local",
            _ => "text",
        },
    };

    /// <summary>True when list, view and read-only output goes through a <c>FieldView</c> instead of plain <c>Html.Encode</c>.</summary>
    private static bool NeedsFmt(FieldInfo field)
    {
        var ui = field.Ui;
        return ui.Format != ViewFormat.Auto
            || ui.TrueText.Length > 0
            || ui.FalseText.Length > 0
            || ui.BadgeRules.Count > 0
            || (LookupOf(field) is not null);
    }

    private static bool ShowsLookupText(FieldInfo field) => LookupOf(field) is { ShowDisplayInView: true };

    private static string Fmt(string prop) => "Fmt_" + prop;

    private static string LookupName(string prop) => "Lookup_" + prop;

    private static string OptionsName(string prop) => "Options_" + prop;

    // ---- encoding helpers -----------------------------------------------------------------------

    /// <summary>Encodes text for HTML at generation time; the result is then emitted as a C# literal.</summary>
    private static string HtmlText(string value) => WebUtility.HtmlEncode(value);

    /// <summary>A Bootstrap name (primary, danger, ...) or icon name reduced to letters, digits, dash and underscore.</summary>
    internal static string Token(string value, string fallback = "")
    {
        var clean = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return clean.Length == 0 ? fallback : clean;
    }

    // ---- statics emitted in the pages class -----------------------------------------------------

    /// <summary>
    /// A static of a pages class. One that holds a translated text is built on each use, so it is in the language of the
    /// request; every other static is created once.
    /// </summary>
    internal static string Static(string type, string name, string init) => Localized && init.Contains("Strings.", StringComparison.Ordinal)
        ? $"    private static {type} {name} => {init};"
        : $"    private static readonly {type} {name} = {init};";

    /// <summary>Format objects, lookups, static options, sort whitelist and row styles used by the customized parts of a table's pages.</summary>
    private static void AppendCustomStatics(StringBuilder sb, string className, TableInfo table)
    {
        foreach (var field in table.Fields.Where(IsCustom))
        {
            var prop = PropName(className, field);
            var ui = field.Ui;
            if (NeedsFmt(field))
            {
                sb.AppendLine(Static("FieldView", Fmt(prop), FmtInit(field)));
            }

            if (LookupOf(field) is { } lookup)
            {
                var order = lookup.OrderField.Length > 0 ? lookup.OrderField : lookup.DisplayField;
                sb.AppendLine($"    private static readonly LookupSource {LookupName(prop)} = new({Lit(QuoteIdent(lookup.Table))}, {Lit(QuoteIdent(lookup.KeyField))}, "
                    + $"{Lit(QuoteIdent(lookup.DisplayField))}, {Lit(lookup.Filter)}, {Lit(QuoteIdent(order))});");
            }
            else if (ui.Options.Count > 0)
            {
                var options = ui.Options.Select(o => $"new({Lit(o.Value)}, {(o.Text.Trim().Length > 0 ? FieldUiText(field, "option." + o.Value, o.Text) : Lit(o.Text))})");
                sb.AppendLine(Static("KeyValuePair<string, string>[]", OptionsName(prop), $"[{string.Join(", ", options)}]"));
            }
        }

        if (!IsCustomTable(table))
        {
            return;
        }

        // Only fields the list shows, that may be sorted and are not binary can be sorted. The names are compared with
        // ordinal equality and the matching quoted column is used in the query, so no request text reaches the SQL.
        var sortable = table.Fields.Where(f => f.ShowInList && f.Ui.Sortable && f.Type != FieldType.Binary).ToList();
        sb.AppendLine($"    private static readonly string[] SortFields = {StringArray(sortable.Select(f => f.Name))};");
        sb.AppendLine($"    private static readonly string[] SortColumns = {StringArray(sortable.Select(f => QuoteIdent(f.Name)))};");
        var keys = table.PrimaryKeys;
        sb.AppendLine($"    private const string SortTieBreak = {Lit(keys.Count == 0 ? string.Empty : ", " + string.Join(", ", keys.Select(k => QuoteIdent(k.Name) + " ASC")))};");
    }

    /// <summary>The <c>new() { ... }</c> initializer of the FieldView of a field that needs one.</summary>
    private static string FmtInit(FieldInfo field)
    {
        var ui = field.Ui;
        // Badge rules and true/false texts imply their format when none is chosen.
        var kind = ui.Format;
        if (kind == ViewFormat.Auto && ui.BadgeRules.Count > 0)
        {
            kind = ViewFormat.Badge;
        }
        else if (kind == ViewFormat.Auto && field.Type == FieldType.Boolean && (ui.TrueText.Length > 0 || ui.FalseText.Length > 0))
        {
            kind = ViewFormat.Boolean;
        }

        var parts = new List<string> { $"Kind = {Lit(kind.ToString())}" };
        if (ui.FormatString.Length > 0)
        {
            parts.Add($"FormatString = {Lit(ui.FormatString)}");
        }

        if (ui.TrueText.Length > 0)
        {
            parts.Add($"TrueText = {FieldUiText(field, "true", ui.TrueText)}");
        }

        if (ui.FalseText.Length > 0)
        {
            parts.Add($"FalseText = {FieldUiText(field, "false", ui.FalseText)}");
        }

        if (ui.BadgeRules.Count > 0)
        {
            var rules = ui.BadgeRules.Select((r, i) =>
                $"new({Lit(r.Operator.ToString())}, {Lit(RuleValue(field, r.Value))}, {Lit(Token(r.Style, "secondary"))}, "
                + $"{(r.Text.Trim().Length > 0 ? ProjectText(LocalizationKeys.FieldBadge(_localizedTable ?? string.Empty, field.Name, i), r.Text) : Lit(r.Text))})");
            parts.Add("Rules = new FieldView.Rule[] { " + string.Join(", ", rules) + " }");
        }

        return $"new() {{ {string.Join(", ", parts)} }}";
    }

    /// <summary>Row condition values for Boolean fields are written as 1 or 0, which is what <c>FieldView.Raw</c> gives for a bool.</summary>
    private static string RuleValue(FieldInfo field, string value)
    {
        if (field.Type != FieldType.Boolean)
        {
            return value;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "1" => "1",
            "false" or "no" or "0" => "0",
            _ => value,
        };
    }

    /// <summary>The RowClass method of the list page, or nothing when no row style rule names a field of the table.</summary>
    private static void AppendRowClass(StringBuilder sb, string className, TableInfo table)
    {
        var rules = ValidRowStyles(table);
        if (rules.Count == 0)
        {
            return;
        }

        // Cards: the card gets a coloured border and the subtle background of the contextual colour (border-danger bg-danger-subtle),
        // which stays readable with the normal text colour in light and dark mode. The result is appended to the card's class list.
        var cards = Designed(table) && table.Designs.List.ListLayout == ListLayout.Cards;

        sb.AppendLine($"    private static string RowClass({className} item)");
        sb.AppendLine("    {");
        foreach (var (rule, field) in rules)
        {
            var color = Token(rule.Style, "warning");
            var result = cards ? " border-" + color + " bg-" + color + "-subtle" : " class=\"table-" + color + "\"";
            var raw = $"FieldView.Raw(item.{PropName(className, field)})";
            sb.AppendLine($"        if (FieldView.Test({Lit(rule.Operator.ToString())}, {raw}, {Lit(RuleValue(field, rule.Value))})) return {Lit(result)};");
        }

        sb.AppendLine("        return string.Empty;");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static List<(RowStyleRule Rule, FieldInfo Field)> ValidRowStyles(TableInfo table)
    {
        var result = new List<(RowStyleRule, FieldInfo)>();
        foreach (var rule in table.Ui.RowStyles)
        {
            var field = table.Fields.FirstOrDefault(f => string.Equals(f.Name, rule.Field, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                result.Add((rule, field));
            }
        }

        return result;
    }

    // ---- list and view --------------------------------------------------------------------------

    private static string Align(TextAlign align) => align switch
    {
        TextAlign.Left => "text-start",
        TextAlign.Center => "text-center",
        TextAlign.Right => "text-end",
        _ => string.Empty,
    };

    /// <summary>class and style attributes (HTML-encoded) for a list cell, plus the width on header cells.</summary>
    private static string CellAttributes(FieldInfo field, bool header, string width = "", bool tooltip = true)
    {
        var ui = field.Ui;
        var cssWidth = width.Length > 0 ? width : ui.Width.Trim();
        var classes = string.Join(" ", new[] { Align(ui.Align), ui.CssClass.Trim() }.Where(c => c.Length > 0));
        var styles = new List<string>();
        if (header && cssWidth.Length > 0)
        {
            styles.Add("width:" + cssWidth);
        }

        if (ui.Style.Trim().Length > 0)
        {
            styles.Add(ui.Style.Trim().TrimEnd(';'));
        }

        var sb = new StringBuilder();
        if (classes.Length > 0)
        {
            sb.Append(" class=\"").Append(HtmlText(classes)).Append('"');
        }

        if (styles.Count > 0)
        {
            sb.Append(" style=\"").Append(HtmlText(string.Join(";", styles))).Append('"');
        }

        if (tooltip && header && ui.Tooltip.Length > 0)
        {
            sb.Append(" title=\"").Append(HtmlText(ui.Tooltip)).Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// C# expression of the attributes of a header cell: a literal, or, for a localized site with a column tooltip, the
    /// literal followed by the translated title.
    /// </summary>
    private static string HeaderAttributes(FieldInfo field, string width, string prefix = "", string suffix = "")
    {
        if (!Localized || field.Ui.Tooltip.Length == 0)
        {
            return Lit(prefix + CellAttributes(field, true, width) + suffix);
        }

        return $"{Lit(prefix + CellAttributes(field, true, width, tooltip: false) + " title=\"")} + Html.Attr({FieldUiText(field, "tooltip", field.Ui.Tooltip)}) + {Lit("\"" + suffix)}";
    }

    /// <summary>The header statement of a list column of a customized table.</summary>
    private static string ListHeaderLine(FieldInfo field, string width = "")
    {
        if (field.Ui.Sortable && field.Type != FieldType.Binary)
        {
            return $"        body.Append(FieldView.SortHeader(request, Route, {Lit(field.Name)}, {FieldText(field)}, {HeaderAttributes(field, width)}));";
        }

        return $"        body.Append({HeaderAttributes(field, width, "<th", ">")}).Append(Html.Encode({FieldText(field)})).Append(\"</th>\");";
    }

    /// <summary>The value HTML of a field on the list and view pages.</summary>
    private static string DisplayExpr(FieldInfo field, string prop)
    {
        if (!NeedsFmt(field))
        {
            return $"Html.Encode(item.{prop})";
        }

        var display = ShowsLookupText(field) ? $", LookupSource.Pick(lk_{prop}, item.{prop})" : string.Empty;
        return $"{Fmt(prop)}.Render(item.{prop}{display})";
    }

    private static string ListCellLine(FieldInfo field, string prop) =>
        $"            body.Append({Lit("<td" + CellAttributes(field, false) + ">")}).Append({DisplayExpr(field, prop)}).Append(\"</td>\");";

    /// <summary>Loads the display texts of the lookups shown on the list (page items) or view (one item) page.</summary>
    private static void AppendDisplayLookups(StringBuilder sb, string className, IEnumerable<FieldInfo> fields, bool list)
    {
        foreach (var field in fields.Where(f => ShowsLookupText(f)))
        {
            var prop = PropName(className, field);
            var keys = list ? $"items.Select(x => (object?)x.{prop})" : $"new object?[] {{ item.{prop} }}";
            sb.AppendLine($"        var lk_{prop} = await {LookupName(prop)}.TextsAsync(connection, {keys});");
        }
    }

    /// <summary>Description and icon shown under the heading of the list page.</summary>
    private static string ListIntro(TableInfo table)
    {
        var ui = table.Ui;
        var icon = Token(ui.Icon);
        if (ui.Description.Length == 0 && icon.Length == 0)
        {
            return string.Empty;
        }

        var html = "<p class=\"text-muted\">"
            + (icon.Length > 0 ? "<i class=\"bi bi-" + icon + " me-2\"></i>" : string.Empty)
            + HtmlText(ui.Description) + "</p>";
        return $"        body.Append({Lit(html)});";
    }

    // ---- forms ----------------------------------------------------------------------------------

    private static bool IsLookupFormField(FieldInfo field) => IsFormField(field) && IsCustom(field) && LookupOf(field) is not null;

    /// <summary>True when the form must load lookup rows, which makes <c>RenderForm</c> asynchronous.</summary>
    private static bool HasFormLookups(TableInfo table) => table.Fields.Any(IsLookupFormField);

    /// <summary>A call of RenderForm; awaited and given the connection string when the form loads lookups.</summary>
    private static string RenderCall(TableInfo table, string arguments, string extra = "") =>
        HasFormLookups(table) ? $"await RenderForm({arguments}, connectionString{extra})" : $"RenderForm({arguments}{extra})";

    private static string ValueExpr(FieldInfo field, string prop, EditControl control)
    {
        var nullable = !field.IsRequired;
        if (control == EditControl.Password)
        {
            return "string.Empty";
        }

        string? dateFormat = control switch
        {
            EditControl.Date => "yyyy-MM-dd",
            EditControl.DateTime => "yyyy-MM-ddTHH:mm",
            EditControl.Time => "HH:mm",
            _ => null,
        };
        return field.Type switch
        {
            FieldType.Integer or FieldType.Decimal => NumberText(prop, nullable),
            FieldType.Date => DateText(prop, nullable, dateFormat ?? "yyyy-MM-dd"),
            FieldType.DateTime => DateText(prop, nullable, dateFormat ?? "yyyy-MM-ddTHH:mm"),
            FieldType.Boolean => "string.Empty",
            _ => TextText(prop, nullable),
        };
    }

    private static string ReadOnlyExpr(FieldInfo field, string prop)
    {
        if (LookupOf(field) is not null)
        {
            return $"{Fmt(prop)}.Render(item.{prop}, LookupSource.Find(opt_{prop}, LookupSource.KeyOf(item.{prop})))";
        }

        return NeedsFmt(field) ? $"{Fmt(prop)}.Render(item.{prop})" : $"Html.Encode(item.{prop})";
    }

    /// <summary>The <c>FieldEditor.Spec</c> object initializer for a customized field.</summary>
    private static string SpecCode(FieldInfo field, string prop, bool forceReadOnly)
    {
        var ui = field.Ui;
        var real = ControlOf(field);
        var control = forceReadOnly ? EditControl.ReadOnly : real;
        var parts = new List<string>
        {
            $"Name = {Lit(field.Name)}",
            $"Label = {FieldText(field)}",
            $"Control = {Lit(HtmlControl(control, field.Type))}",
        };

        if (control == EditControl.Checkbox)
        {
            parts.Add($"Checked = item.{prop} == true");
        }
        else if (control == EditControl.ReadOnly)
        {
            parts.Add($"ReadOnlyHtml = {ReadOnlyExpr(field, prop)}");
        }
        else
        {
            parts.Add($"Value = {ValueExpr(field, prop, real)}");
        }

        if (field.IsRequired && control != EditControl.Checkbox)
        {
            parts.Add("Required = true");
        }

        if (ui.AutoFocus)
        {
            parts.Add("AutoFocus = true");
        }

        AddFieldText(parts, field, "Help", "help", ui.HelpText);
        AddFieldText(parts, field, "Placeholder", "placeholder", ui.Placeholder);
        AddFieldText(parts, field, "Tooltip", "tooltip", ui.Tooltip);
        AddText(parts, "CssClass", ui.CssClass);
        AddText(parts, "Style", ui.Style);
        AddText(parts, "Min", ui.Min);
        AddText(parts, "Max", ui.Max);
        AddText(parts, "Step", ui.Step.Length == 0 && field.Type == FieldType.Decimal ? "any" : ui.Step);
        if (IsValidPattern(ui.Pattern))
        {
            AddText(parts, "Pattern", ui.Pattern);
        }

        if (control == EditControl.TextArea)
        {
            parts.Add($"Rows = {Math.Max(1, ui.Rows)}");
        }

        if (ui.MaxLength > 0)
        {
            parts.Add($"MaxLength = {ui.MaxLength}");
        }

        if (ui.MinLength > 0)
        {
            parts.Add($"MinLength = {ui.MinLength}");
        }

        if (control is EditControl.Select or EditControl.Radio)
        {
            parts.Add(LookupOf(field) is not null ? $"Options = opt_{prop}" : $"Options = {OptionsName(prop)}");
        }

        return "new FieldEditor.Spec { " + string.Join(", ", parts) + " }";
    }

    /// <summary>Adds a property with a field text the project owner wrote, translated when the site is localized.</summary>
    private static void AddFieldText(List<string> parts, FieldInfo field, string name, string part, string value)
    {
        if (value.Length > 0)
        {
            parts.Add($"{name} = {FieldUiText(field, part, value)}");
        }
    }

    private static void AddText(List<string> parts, string name, string value)
    {
        if (value.Length > 0)
        {
            parts.Add($"{name} = {Lit(value)}");
        }
    }

    /// <summary>The form statement of a customized field.</summary>
    private static string CustomFormLine(FieldInfo field, string prop)
    {
        var spec = SpecCode(field, prop, false);
        if (field.Ui.ReadOnlyOnEdit && FormModes(field).Edit)
        {
            return $"body.Append(edit ? FieldEditor.Render({SpecCode(field, prop, true)}) : FieldEditor.Render({spec}));";
        }

        return $"body.Append(FieldEditor.Render({spec}));";
    }

    /// <summary>Loads the lookup rows a form needs, at the top of RenderForm.</summary>
    private static void AppendFormLookups(StringBuilder sb, string className, TableInfo table)
    {
        foreach (var field in table.Fields.Where(IsLookupFormField))
        {
            var prop = PropName(className, field);
            sb.AppendLine($"        var opt_{prop} = await {LookupName(prop)}.OptionsAsync(connectionString);");
        }
    }

    /// <summary>The fieldset legend lines that surround fields with a group; call once per form field in order.</summary>
    private static IEnumerable<string> GroupTransition(string? previous, FieldInfo field)
    {
        var group = field.Ui.Group;
        if (string.Equals(previous ?? string.Empty, group, StringComparison.Ordinal))
        {
            yield break;
        }

        if (!string.IsNullOrEmpty(previous))
        {
            yield return "        body.Append(FieldEditor.GroupEnd);";
        }

        if (group.Length > 0)
        {
            yield return $"        body.Append(FieldEditor.GroupStart({ProjectText(LocalizationKeys.FieldGroup(_localizedTable ?? string.Empty, group), group)}));";
        }
    }

    /// <summary>The object creation for the add form with the configured default values, or a plain new object.</summary>
    private static string AddItemExpr(string className, TableInfo table)
    {
        var assignments = new List<string>();
        foreach (var field in table.Fields.Where(f => IsCustom(f) && f.Ui.DefaultValue.Length > 0 && FormModes(f).Add))
        {
            if (DefaultLiteral(field) is { } literal)
            {
                assignments.Add($"{PropName(className, field)} = {literal}");
            }
        }

        return assignments.Count == 0 ? $"new {className}()" : $"new {className} {{ {string.Join(", ", assignments)} }}";
    }

    /// <summary>C# literal for the default value of a field, or null when the text does not fit the field type (it is then ignored).</summary>
    private static string? DefaultLiteral(FieldInfo field)
    {
        var text = field.Ui.DefaultValue;
        switch (field.Type)
        {
            case FieldType.Text:
                return Lit(text);
            case FieldType.Integer:
                return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture) + "L"
                    : null;
            case FieldType.Decimal:
                return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString(CultureInfo.InvariantCulture) + "m"
                    : null;
            case FieldType.Boolean:
                return text.Trim().ToLowerInvariant() switch
                {
                    "true" or "yes" or "1" => "true",
                    "false" or "no" or "0" => "false",
                    _ => null,
                };
            case FieldType.Date:
            case FieldType.DateTime:
                var lower = text.Trim().ToLowerInvariant();
                if (lower == "today")
                {
                    return "DateTime.Today";
                }

                if (lower == "now")
                {
                    return field.Type == FieldType.Date ? "DateTime.Today" : "DateTime.Now";
                }

                return DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? $"new DateTime({date.Ticks}L)"
                    : null;
            default:
                return null;
        }
    }

    // ---- binding and validation -----------------------------------------------------------------

    private static bool IsValidPattern(string pattern)
    {
        if (pattern.Length == 0)
        {
            return false;
        }

        try
        {
            _ = new Regex("^(?:" + pattern + ")$");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The modes a field is bound in: a field that is read-only on edit, or read-only, is not bound there.</summary>
    private static (bool Add, bool Edit) BindModes(FieldInfo field)
    {
        var (add, edit) = FormModes(field);
        if (!IsCustom(field))
        {
            return (add, edit);
        }

        if (ControlOf(field) == EditControl.ReadOnly)
        {
            return (false, false);
        }

        return (add, edit && !field.Ui.ReadOnlyOnEdit);
    }

    private static string RequiredText(FieldInfo field) =>
        field.Ui.RequiredMessage.Length > 0 ? FieldUiText(field, "required", field.Ui.RequiredMessage) : FieldMessage("required", field);

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Server-side checks for a text value in the variable <c>value</c>.</summary>
    private static List<string> TextChecks(FieldInfo field, string prop)
    {
        var ui = field.Ui;
        var label = FieldText(field);
        var checks = new List<string>();
        if (ui.MaxLength > 0)
        {
            checks.Add($"if (value.Length > {ui.MaxLength}) errors.Add({CheckMessage("checkMaxLength", label, ui.MaxLength.ToString(CultureInfo.InvariantCulture))});");
        }

        if (ui.MinLength > 0)
        {
            checks.Add($"if (value.Length < {ui.MinLength}) errors.Add({CheckMessage("checkMinLength", label, ui.MinLength.ToString(CultureInfo.InvariantCulture))});");
        }

        if (IsValidPattern(ui.Pattern))
        {
            var message = ui.PatternMessage.Length > 0 ? FieldUiText(field, "pattern", ui.PatternMessage) : CheckMessage("checkPattern", label, string.Empty);
            checks.Add($"if (!FieldEditor.Matches(value, {Lit(ui.Pattern)})) errors.Add({message});");
        }

        if (LookupOf(field) is null && ui.Options.Count > 0)
        {
            checks.Add($"if (!Array.Exists({OptionsName(prop)}, o => o.Key == value)) errors.Add({CheckMessage("checkChoice", label, string.Empty)});");
        }

        return checks;
    }

    /// <summary>Bind code of a customized text field.</summary>
    private static string CustomTextBind(FieldInfo field, string prop)
    {
        var sb = new StringBuilder();
        var name = Lit(field.Name);
        var password = ControlOf(field) == EditControl.Password;
        var checks = TextChecks(field, prop);
        // A password box is never filled in, so an empty value on edit keeps the stored one. Passwords are not trimmed.
        sb.AppendLine("        {");
        sb.AppendLine(password ? $"            var value = form[{name}].ToString();" : $"            var value = form[{name}].ToString().Trim();");
        var inner = password ? "                " : "            ";
        if (password)
        {
            sb.AppendLine("            if (!edit || value.Length > 0)");
            sb.AppendLine("            {");
        }

        if (field.IsRequired)
        {
            sb.AppendLine($"{inner}if (value.Length == 0) errors.Add({RequiredText(field)});");
            if (checks.Count > 0)
            {
                sb.AppendLine($"{inner}else");
                sb.AppendLine($"{inner}{{");
                foreach (var check in checks)
                {
                    sb.AppendLine($"{inner}    {check}");
                }

                sb.AppendLine($"{inner}}}");
            }

            sb.AppendLine($"{inner}item.{prop} = value;");
        }
        else
        {
            if (checks.Count > 0)
            {
                sb.AppendLine($"{inner}if (value.Length > 0)");
                sb.AppendLine($"{inner}{{");
                foreach (var check in checks)
                {
                    sb.AppendLine($"{inner}    {check}");
                }

                sb.AppendLine($"{inner}}}");
            }

            sb.AppendLine($"{inner}item.{prop} = value.Length == 0 ? null : value;");
        }

        if (password)
        {
            sb.AppendLine("            }");
        }

        sb.AppendLine("        }");
        return sb.ToString();
    }

    /// <summary>Range and option checks run after a number was parsed into <c>parsed</c> (the raw text is in <c>raw</c>).</summary>
    private static List<string> NumberChecks(FieldInfo field, string prop)
    {
        var ui = field.Ui;
        var label = FieldText(field);
        var checks = new List<string>();
        if (TryDecimal(ui.Min, out var min))
        {
            checks.Add($"if (parsed < {min.ToString(CultureInfo.InvariantCulture)}m) errors.Add({CheckMessage("checkMin", label, ui.Min.Trim())});");
        }

        if (TryDecimal(ui.Max, out var max))
        {
            checks.Add($"if (parsed > {max.ToString(CultureInfo.InvariantCulture)}m) errors.Add({CheckMessage("checkMax", label, ui.Max.Trim())});");
        }

        if (LookupOf(field) is null && ui.Options.Count > 0)
        {
            checks.Add($"if (!Array.Exists({OptionsName(prop)}, o => o.Key == raw)) errors.Add({CheckMessage("checkChoice", label, string.Empty)});");
        }

        return checks;
    }

    private static List<string> DateChecks(FieldInfo field)
    {
        var ui = field.Ui;
        var label = FieldText(field);
        var checks = new List<string>();
        if (DateTime.TryParse(ui.Min.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var min))
        {
            checks.Add($"if (parsed < new DateTime({min.Ticks}L)) errors.Add({CheckMessage("checkNotBefore", label, ui.Min.Trim())});");
        }

        if (DateTime.TryParse(ui.Max.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var max))
        {
            checks.Add($"if (parsed > new DateTime({max.Ticks}L)) errors.Add({CheckMessage("checkNotAfter", label, ui.Max.Trim())});");
        }

        return checks;
    }

    // ---- menu and layout ------------------------------------------------------------------------

    /// <summary>Tables shown in the menu, ordered by MenuOrder and then by their order in the project.</summary>
    private static List<TableInfo> MenuTables(IReadOnlyList<TableInfo> tables) => tables
        .Select((table, index) => (table, index))
        .Where(x => x.table.Ui.ShowInMenu)
        .OrderBy(x => x.table.Ui.MenuOrder)
        .ThenBy(x => x.index)
        .Select(x => x.table)
        .ToList();

    /// <summary>True when any table changes its place or look in the home page menu.</summary>
    private static bool MenuCustomized(IReadOnlyList<TableInfo> tables) => tables.Any(t =>
        t.Ui.MenuGroup.Length > 0 || !t.Ui.ShowInMenu || t.Ui.MenuOrder != 0 || Token(t.Ui.Icon).Length > 0)
        || MenuReports().Any(r => r.MenuGroup.Length > 0);

    /// <summary>True when the shared layout gets a navigation menu: some table sets a menu group or hides itself.</summary>
    private static bool NavCustomized(IReadOnlyList<TableInfo> tables) => tables.Any(t => t.Ui.MenuGroup.Length > 0 || !t.Ui.ShowInMenu);

    private static string FileExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return "." + Token(extension.TrimStart('.'), "png");
    }

    internal static string FaviconFile(ProjectMetadata meta) => "favicon" + FileExtension(meta.FaviconPath);

    internal static string LogoFile(ProjectMetadata meta) => "logo" + FileExtension(meta.LogoPath);

    /// <summary>
    /// Applies the project metadata to the text of the Html helper by replacing anchors that exist both in the plain and
    /// in the localized variant. Does nothing for default metadata and tables without menu settings.
    /// </summary>
    private static string ApplyLayout(string text, ProjectMetadata meta, IReadOnlyList<TableInfo> tables)
    {
        if (!string.Equals(meta.NavbarStyle, "light", StringComparison.OrdinalIgnoreCase) && meta.NavbarStyle.Length > 0)
        {
            var style = Token(meta.NavbarStyle, "light");
            var theme = style is "light" or "warning" or "info" ? "light" : "dark";
            text = ReplaceOnce(text, """<nav class=\"navbar navbar-expand bg-body-tertiary mb-4\">""",
                $"<nav class=\\\"navbar navbar-expand bg-{style} mb-4\\\" data-bs-theme=\\\"{theme}\\\">");
        }

        if (meta.LogoPath.Length > 0)
        {
            text = ReplaceOnce(text, """<a class=\"navbar-brand\" href=\"/\">""",
                """<a class=\"navbar-brand\" href=\"/\">""" + $"<img src=\\\"/{LogoFile(meta)}\\\" alt=\\\"\\\" height=\\\"28\\\" class=\\\"me-2\\\">");
        }

        var nav = NavCustomized(tables);
        if (nav)
        {
            var menu = new StringBuilder();
            menu.AppendLine("    private static string Nav()");
            menu.AppendLine("    {");
            menu.AppendLine("        var items = new (string Group, string Caption, string Href)[]");
            menu.AppendLine("        {");
            foreach (var table in MenuTables(tables))
            {
                menu.AppendLine($"            ({MenuGroupText(table.Ui.MenuGroup)}, {TableText(table)}, {Lit("/" + Identifier.Route(table.Name))}),");
            }

            foreach (var report in MenuReports())
            {
                menu.AppendLine($"            ({MenuGroupText(report.MenuGroup)}, {ReportText(report)}, {Lit(ReportRoute(report))}),");
            }

            menu.AppendLine("        };");
            menu.AppendLine("""        var sb = new StringBuilder("<ul class=\"navbar-nav me-auto\">");""");
            menu.AppendLine("        foreach (var group in items.GroupBy(item => item.Group))");
            menu.AppendLine("        {");
            menu.AppendLine("            if (group.Key.Length == 0)");
            menu.AppendLine("            {");
            menu.AppendLine("                foreach (var item in group)");
            menu.AppendLine("                {");
            menu.AppendLine("""                    sb.Append("<li class=\"nav-item\"><a class=\"nav-link\" href=\"").Append(Attr(item.Href)).Append("\">").Append(Encode(item.Caption)).Append("</a></li>");""");
            menu.AppendLine("                }");
            menu.AppendLine();
            menu.AppendLine("                continue;");
            menu.AppendLine("            }");
            menu.AppendLine();
            menu.AppendLine("""            sb.Append("<li class=\"nav-item dropdown\"><a class=\"nav-link dropdown-toggle\" href=\"#\" role=\"button\" data-bs-toggle=\"dropdown\" aria-expanded=\"false\">").Append(Encode(group.Key)).Append("</a><ul class=\"dropdown-menu\">");""");
            menu.AppendLine("            foreach (var item in group)");
            menu.AppendLine("            {");
            menu.AppendLine("""                sb.Append("<li><a class=\"dropdown-item\" href=\"").Append(Attr(item.Href)).Append("\">").Append(Encode(item.Caption)).Append("</a></li>");""");
            menu.AppendLine("            }");
            menu.AppendLine();
            menu.AppendLine("""            sb.Append("</ul></li>");""");
            menu.AppendLine("        }");
            menu.AppendLine();
            menu.AppendLine("""        return sb.Append("</ul>").ToString();""");
            menu.AppendLine("    }");
            menu.AppendLine();
            text = ReplaceOnce(text, "    public static string Menu(IEnumerable<(string Caption, string Href)> items)", menu + "    public static string Menu(IEnumerable<(string Caption, string Href)> items)");

            // The menu goes right after the brand link. The localized layout puts the language switcher there, so the menu comes first.
            var plain = """</a></div></nav>""";
            if (text.Contains(plain, StringComparison.Ordinal))
            {
                text = ReplaceOnce(text, plain, "</a>\" + Nav() + \"</div></nav>");
            }
            else
            {
                text = ReplaceOnce(text, "\"</a>\" + Strings.Switcher()", "\"</a>\" + Nav() + Strings.Switcher()");
            }
        }

        if (MenuCustomized(tables))
        {
            const string grouped = """
                    public static string GroupedMenu(IEnumerable<(string Group, string Caption, string Href, string Icon)> items)
                    {
                        var sb = new StringBuilder();
                        foreach (var group in items.GroupBy(item => item.Group))
                        {
                            if (group.Key.Length > 0)
                            {
                                sb.Append("<h2 class=\"h5 mt-4\">").Append(Encode(group.Key)).Append("</h2>");
                            }

                            sb.Append("<ul class=\"list-group mb-3\">");
                            foreach (var item in group)
                            {
                                var icon = item.Icon.Length == 0 ? string.Empty : "<i class=\"bi bi-" + Attr(item.Icon) + " me-2\"></i>";
                                sb.Append("<li class=\"list-group-item\">").Append(icon).Append("<a href=\"").Append(Attr(item.Href)).Append("\">")
                                    .Append(Encode(item.Caption)).Append("</a></li>");
                            }

                            sb.Append("</ul>");
                        }

                        return sb.ToString();
                    }


                """;
            text = ReplaceOnce(text, "    public static string Menu(IEnumerable<(string Caption, string Href)> items)", grouped + "    public static string Menu(IEnumerable<(string Caption, string Href)> items)");
        }

        if (string.Equals(meta.ColorMode, "light", StringComparison.OrdinalIgnoreCase) || string.Equals(meta.ColorMode, "dark", StringComparison.OrdinalIgnoreCase))
        {
            text = ReplaceOnce(text, """><head><meta charset=\"utf-8\">""",
                $" data-bs-theme=\\\"{meta.ColorMode.ToLowerInvariant()}\\\"" + """><head><meta charset=\"utf-8\">""");
        }

        var head = new StringBuilder();
        if (tables.Any(t => Token(t.Ui.Icon).Length > 0 || UsesFormPageIcons(t)))
        {
            head.Append("<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css\">");
        }

        if (meta.FaviconPath.Length > 0)
        {
            head.Append("<link rel=\"icon\" href=\"/").Append(FaviconFile(meta)).Append("\">");
        }

        if (meta.GlobalCss.Length > 0)
        {
            // GlobalCss is written by the project owner and emitted as written.
            head.Append("<style>").Append(meta.GlobalCss).Append("</style>");
        }

        if (head.Length > 0)
        {
            text = ReplaceOnce(text, "+ \"</head><body>\"", "+ " + Lit(head.ToString()) + "\n        + \"</head><body>\"");
        }

        var tail = new StringBuilder();
        // A localized footer is looked up per request: its text is a placeholder here, replaced by the lookup below.
        const string footerSlot = "footer";
        var localizedFooter = Localized && meta.FooterText.Trim().Length > 0;
        if (meta.FooterText.Length > 0)
        {
            tail.Append("<footer class=\"container text-muted small py-4\">").Append(localizedFooter ? footerSlot : HtmlText(meta.FooterText)).Append("</footer>");
        }

        if (nav)
        {
            tail.Append("<script src=\"https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js\"></script>");
        }

        if (meta.GlobalScript.Length > 0)
        {
            // GlobalScript is written by the project owner and emitted as written.
            tail.Append("<script>").Append(meta.GlobalScript).Append("</script>");
        }

        if (tail.Length > 0)
        {
            var tailExpr = Lit(tail.ToString());
            if (localizedFooter)
            {
                var halves = tail.ToString().Split(footerSlot);
                tailExpr = Lit(halves[0]) + " + Encode(" + ProjectText(LocalizationKeys.SiteFooter, meta.FooterText.Trim()) + ") + " + Lit(halves[1]);
            }

            text = ReplaceOnce(text, "\"</main></body></html>\"", "\"</main>\" + " + tailExpr + " + \"</body></html>\"");
        }

        return text;
    }

    // ---- project-level files --------------------------------------------------------------------

    private static string LogLevelOf(string value)
    {
        foreach (var level in new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" })
        {
            if (string.Equals(level, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return "Information";
    }

    private static string XmlText(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    /// <summary>True when the generated site also needs response compression: it is requested and the metadata is not all default.</summary>
    internal static bool UsesCompression(ProjectMetadata meta) =>
        // Response compression is on by default in the model, so on its own it must not change the generated output. It is
        // generated only when at least one other metadata setting has been changed.
        meta.EnableResponseCompression && !meta.IsDefaultExceptCompression();

    /// <summary>The ASPNETCORE_ENVIRONMENT the generated site sets, or null for the default (Development), which sets nothing.</summary>
    internal static string? EnvironmentName(ProjectMetadata meta)
    {
        var name = Token(meta.Environment?.Trim() ?? string.Empty, "Development");
        return string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    /// <summary>Properties/launchSettings.json, written only when the environment is not the default.</summary>
    internal static string LaunchSettings(string ns, ProjectMetadata meta, string environment)
    {
        var url = (meta.UseHttps ? $"https://localhost:{Math.Clamp(meta.HttpsPort, 1, 65535)};" : string.Empty)
            + $"http://localhost:{Math.Clamp(meta.HttpPort, 1, 65535)}";
        return $$"""
            {
              "profiles": {
                "{{ns}}": {
                  "commandName": "Project",
                  "dotnetRunMessages": true,
                  "launchBrowser": false,
                  "applicationUrl": "{{url}}",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "{{environment}}"
                  }
                }
              }
            }

            """;
    }

    internal static bool UsesStaticFiles(ProjectMetadata meta) => meta.FaviconPath.Length > 0 || meta.LogoPath.Length > 0;

    /// <summary>Assembly name, root namespace, version and descriptive properties in the csproj; nothing is added for default metadata.</summary>
    private static string CsprojProperties(string text, ProjectMetadata? meta)
    {
        if (meta is null)
        {
            return text;
        }

        var d = new ProjectMetadata();
        var lines = new StringBuilder();
        void Add(string element, string value, string fallback = "")
        {
            if (value.Trim().Length > 0 && !string.Equals(value, fallback, StringComparison.Ordinal))
            {
                lines.Append("    <").Append(element).Append('>').Append(XmlText(value.Trim())).Append("</").Append(element).Append(">\n");
            }
        }

        Add("AssemblyName", meta.AssemblyName);
        Add("RootNamespace", meta.RootNamespace);
        Add("Version", meta.Version, d.Version);
        Add("Description", meta.Description);
        Add("Authors", meta.Author);
        Add("Company", meta.Company);
        Add("Copyright", meta.Copyright);
        return lines.Length == 0
            ? text
            : ReplaceOnce(text, "    <ImplicitUsings>enable</ImplicitUsings>\n", "    <ImplicitUsings>enable</ImplicitUsings>\n" + lines);
    }

    /// <summary>Log level and Kestrel endpoints in appsettings.json; nothing is added for default metadata.</summary>
    private static string AppSettingsMeta(string text, ProjectMetadata? meta)
    {
        if (meta is null)
        {
            return text;
        }

        var d = new ProjectMetadata();
        var level = LogLevelOf(meta.LogLevel);
        if (level != "Information")
        {
            text = ReplaceOnce(text, "\"Default\": \"Information\"", "\"Default\": \"" + level + "\"");
        }

        if (meta.HttpPort != d.HttpPort || meta.HttpsPort != d.HttpsPort || meta.UseHttps)
        {
            // Endpoints written here replace ASPNETCORE_URLS. HTTPS needs a certificate (the development certificate, or Kestrel:Certificates).
            var endpoints = new StringBuilder();
            endpoints.Append("  \"Kestrel\": {\n    \"Endpoints\": {\n      \"Http\": {\n        \"Url\": \"http://localhost:")
                .Append(Math.Clamp(meta.HttpPort, 1, 65535)).Append("\"\n      }");
            if (meta.UseHttps)
            {
                endpoints.Append(",\n      \"Https\": {\n        \"Url\": \"https://localhost:").Append(Math.Clamp(meta.HttpsPort, 1, 65535)).Append("\"\n      }");
            }

            endpoints.Append("\n    }\n  },\n");
            text = ReplaceOnce(text, "  \"Logging\": {", endpoints + "  \"Logging\": {");
        }

        return text;
    }
}
