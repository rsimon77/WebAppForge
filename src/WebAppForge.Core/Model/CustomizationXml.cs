using System.Xml.Linq;

namespace WebAppForge.Core.Model;

/// <summary>
/// Project file form of the customization contract:
/// &lt;field&gt;&lt;ui .../&gt;&lt;/field&gt;, &lt;table&gt;&lt;tableUi/&gt;&lt;designs&gt;&lt;page name=""/&gt;&lt;/designs&gt;&lt;/table&gt; and &lt;project&gt;&lt;meta/&gt;&lt;/project&gt;.
/// Only values that differ from the default are written; missing attributes and elements read as defaults,
/// so project files from before customization existed load unchanged. Short values are attributes (camelCase),
/// multi-line texts (HTML, CSS, script) are element text.
/// </summary>
internal static class CustomizationXml
{
    private static readonly FieldUi FieldDefaults = new();
    private static readonly TableUi TableDefaults = new();
    private static readonly PageDesign PageDefaults = new();
    private static readonly ProjectMetadata MetaDefaults = new();

    // ---- field ----------------------------------------------------------------------------------

    public static void AppendFieldUi(XElement field, FieldUi ui)
    {
        if (ui.IsDefault())
        {
            return;
        }

        var d = FieldDefaults;
        var e = new XElement("ui");
        Enum(e, "control", ui.Control, d.Control);
        Str(e, "helpText", ui.HelpText, d.HelpText);
        Str(e, "placeholder", ui.Placeholder, d.Placeholder);
        Str(e, "tooltip", ui.Tooltip, d.Tooltip);
        Str(e, "defaultValue", ui.DefaultValue, d.DefaultValue);
        Bool(e, "readOnlyOnEdit", ui.ReadOnlyOnEdit, d.ReadOnlyOnEdit);
        Bool(e, "autoFocus", ui.AutoFocus, d.AutoFocus);
        Int(e, "rows", ui.Rows, d.Rows);
        Int(e, "maxLength", ui.MaxLength, d.MaxLength);
        Int(e, "minLength", ui.MinLength, d.MinLength);
        Str(e, "min", ui.Min, d.Min);
        Str(e, "max", ui.Max, d.Max);
        Str(e, "step", ui.Step, d.Step);
        Str(e, "pattern", ui.Pattern, d.Pattern);
        Str(e, "patternMessage", ui.PatternMessage, d.PatternMessage);
        Str(e, "requiredMessage", ui.RequiredMessage, d.RequiredMessage);
        Enum(e, "format", ui.Format, d.Format);
        Str(e, "formatString", ui.FormatString, d.FormatString);
        Str(e, "trueText", ui.TrueText, d.TrueText);
        Str(e, "falseText", ui.FalseText, d.FalseText);
        Str(e, "width", ui.Width, d.Width);
        Enum(e, "align", ui.Align, d.Align);
        Str(e, "cssClass", ui.CssClass, d.CssClass);
        Str(e, "style", ui.Style, d.Style);
        Bool(e, "sortable", ui.Sortable, d.Sortable);
        Str(e, "group", ui.Group, d.Group);
        Str(e, "formPage", ui.FormPage, d.FormPage);

        foreach (var option in ui.Options)
        {
            e.Add(new XElement("option", new XAttribute("value", option.Value), new XAttribute("text", option.Text)));
        }

        foreach (var rule in ui.BadgeRules)
        {
            e.Add(new XElement("badgeRule",
                new XAttribute("operator", rule.Operator.ToString()),
                new XAttribute("value", rule.Value),
                new XAttribute("style", rule.Style),
                new XAttribute("text", rule.Text)));
        }

        if (ui.Lookup is { } lookup)
        {
            e.Add(new XElement("lookup",
                new XAttribute("table", lookup.Table),
                new XAttribute("keyField", lookup.KeyField),
                new XAttribute("displayField", lookup.DisplayField),
                new XAttribute("filter", lookup.Filter),
                new XAttribute("orderField", lookup.OrderField),
                new XAttribute("showDisplayInView", lookup.ShowDisplayInView)));
        }

        field.Add(e);
    }

    public static FieldUi ReadFieldUi(XElement field)
    {
        var ui = new FieldUi();
        if (field.Element("ui") is not { } e)
        {
            return ui;
        }

        var d = FieldDefaults;
        ui.Control = ReadEnum(e, "control", d.Control);
        ui.HelpText = ReadStr(e, "helpText", d.HelpText);
        ui.Placeholder = ReadStr(e, "placeholder", d.Placeholder);
        ui.Tooltip = ReadStr(e, "tooltip", d.Tooltip);
        ui.DefaultValue = ReadStr(e, "defaultValue", d.DefaultValue);
        ui.ReadOnlyOnEdit = ReadBool(e, "readOnlyOnEdit", d.ReadOnlyOnEdit);
        ui.AutoFocus = ReadBool(e, "autoFocus", d.AutoFocus);
        ui.Rows = ReadInt(e, "rows", d.Rows);
        ui.MaxLength = ReadInt(e, "maxLength", d.MaxLength);
        ui.MinLength = ReadInt(e, "minLength", d.MinLength);
        ui.Min = ReadStr(e, "min", d.Min);
        ui.Max = ReadStr(e, "max", d.Max);
        ui.Step = ReadStr(e, "step", d.Step);
        ui.Pattern = ReadStr(e, "pattern", d.Pattern);
        ui.PatternMessage = ReadStr(e, "patternMessage", d.PatternMessage);
        ui.RequiredMessage = ReadStr(e, "requiredMessage", d.RequiredMessage);
        ui.Format = ReadEnum(e, "format", d.Format);
        ui.FormatString = ReadStr(e, "formatString", d.FormatString);
        ui.TrueText = ReadStr(e, "trueText", d.TrueText);
        ui.FalseText = ReadStr(e, "falseText", d.FalseText);
        ui.Width = ReadStr(e, "width", d.Width);
        ui.Align = ReadEnum(e, "align", d.Align);
        ui.CssClass = ReadStr(e, "cssClass", d.CssClass);
        ui.Style = ReadStr(e, "style", d.Style);
        ui.Sortable = ReadBool(e, "sortable", d.Sortable);
        ui.Group = ReadStr(e, "group", d.Group);
        ui.FormPage = ReadStr(e, "formPage", d.FormPage);

        foreach (var option in e.Elements("option"))
        {
            ui.Options.Add(new FieldOption { Value = ReadStr(option, "value", string.Empty), Text = ReadStr(option, "text", string.Empty) });
        }

        foreach (var rule in e.Elements("badgeRule"))
        {
            ui.BadgeRules.Add(new ValueRule
            {
                Operator = ReadEnum(rule, "operator", ConditionOperator.Equals),
                Value = ReadStr(rule, "value", string.Empty),
                Style = ReadStr(rule, "style", "secondary"),
                Text = ReadStr(rule, "text", string.Empty),
            });
        }

        if (e.Element("lookup") is { } lookup)
        {
            ui.Lookup = new FieldLookup
            {
                Table = ReadStr(lookup, "table", string.Empty),
                KeyField = ReadStr(lookup, "keyField", string.Empty),
                DisplayField = ReadStr(lookup, "displayField", string.Empty),
                Filter = ReadStr(lookup, "filter", string.Empty),
                OrderField = ReadStr(lookup, "orderField", string.Empty),
                ShowDisplayInView = ReadBool(lookup, "showDisplayInView", true),
            };
        }

        return ui;
    }

    // ---- table ----------------------------------------------------------------------------------

    public static void AppendTable(XElement table, TableInfo info)
    {
        if (!info.Ui.IsDefault())
        {
            var d = TableDefaults;
            var ui = info.Ui;
            var e = new XElement("tableUi");
            Str(e, "description", ui.Description, d.Description);
            Str(e, "icon", ui.Icon, d.Icon);
            Str(e, "menuGroup", ui.MenuGroup, d.MenuGroup);
            Bool(e, "showInMenu", ui.ShowInMenu, d.ShowInMenu);
            Int(e, "menuOrder", ui.MenuOrder, d.MenuOrder);
            Str(e, "cssClass", ui.CssClass, d.CssClass);
            foreach (var rule in ui.RowStyles)
            {
                e.Add(new XElement("rowStyle",
                    new XAttribute("field", rule.Field),
                    new XAttribute("operator", rule.Operator.ToString()),
                    new XAttribute("value", rule.Value),
                    new XAttribute("style", rule.Style)));
            }

            table.Add(e);
        }

        if (info.Designs.IsDefault())
        {
            return;
        }

        var designs = new XElement("designs");
        AppendPage(designs, "list", info.Designs.List);
        AppendPage(designs, "view", info.Designs.View);
        AppendPage(designs, "add", info.Designs.Add);
        AppendPage(designs, "copy", info.Designs.Copy);
        AppendPage(designs, "edit", info.Designs.Edit);
        AppendPage(designs, "delete", info.Designs.Delete);
        AppendPage(designs, "search", info.Designs.Search);
        table.Add(designs);
    }

    public static void ReadTable(XElement table, TableInfo info)
    {
        if (table.Element("tableUi") is { } e)
        {
            var d = TableDefaults;
            var ui = info.Ui;
            ui.Description = ReadStr(e, "description", d.Description);
            ui.Icon = ReadStr(e, "icon", d.Icon);
            ui.MenuGroup = ReadStr(e, "menuGroup", d.MenuGroup);
            ui.ShowInMenu = ReadBool(e, "showInMenu", d.ShowInMenu);
            ui.MenuOrder = ReadInt(e, "menuOrder", d.MenuOrder);
            ui.CssClass = ReadStr(e, "cssClass", d.CssClass);
            foreach (var rule in e.Elements("rowStyle"))
            {
                ui.RowStyles.Add(new RowStyleRule
                {
                    Field = ReadStr(rule, "field", string.Empty),
                    Operator = ReadEnum(rule, "operator", ConditionOperator.Equals),
                    Value = ReadStr(rule, "value", string.Empty),
                    Style = ReadStr(rule, "style", "warning"),
                });
            }
        }

        if (table.Element("designs") is not { } designs)
        {
            return;
        }

        foreach (var page in designs.Elements("page"))
        {
            var target = (page.Attribute("name")?.Value ?? string.Empty).ToLowerInvariant() switch
            {
                "list" => info.Designs.List,
                "view" => info.Designs.View,
                "add" => info.Designs.Add,
                "copy" => info.Designs.Copy,
                "edit" => info.Designs.Edit,
                "delete" => info.Designs.Delete,
                "search" => info.Designs.Search,
                _ => null,
            };
            if (target is not null)
            {
                ReadPage(page, target);
            }
        }
    }

    private static void AppendPage(XElement designs, string name, PageDesign design)
    {
        if (design.IsDefault())
        {
            return;
        }

        var d = PageDefaults;
        var e = new XElement("page", new XAttribute("name", name));
        Str(e, "title", design.Title, d.Title);
        Str(e, "subtitle", design.Subtitle, d.Subtitle);
        Str(e, "cssClass", design.CssClass, d.CssClass);
        Enum(e, "container", design.Container, d.Container);
        Enum(e, "formLayout", design.FormLayout, d.FormLayout);
        Str(e, "submitText", design.SubmitText, d.SubmitText);
        Str(e, "cancelText", design.CancelText, d.CancelText);
        Bool(e, "showCancel", design.ShowCancel, d.ShowCancel);
        Str(e, "submitCssClass", design.SubmitCssClass, d.SubmitCssClass);
        Enum(e, "buttons", design.Buttons, d.Buttons);
        Enum(e, "multiPage", design.MultiPage, d.MultiPage);
        Str(e, "successMessage", design.SuccessMessage, d.SuccessMessage);
        Bool(e, "confirmBeforeSave", design.ConfirmBeforeSave, d.ConfirmBeforeSave);
        Enum(e, "listLayout", design.ListLayout, d.ListLayout);
        Bool(e, "showRowNumbers", design.ShowRowNumbers, d.ShowRowNumbers);
        Bool(e, "showPager", design.ShowPager, d.ShowPager);
        Bool(e, "stickyHeader", design.StickyHeader, d.StickyHeader);
        Bool(e, "striped", design.Striped, d.Striped);
        Bool(e, "hover", design.Hover, d.Hover);
        Bool(e, "bordered", design.Bordered, d.Bordered);
        Bool(e, "showActionsFirst", design.ShowActionsFirst, d.ShowActionsFirst);
        Str(e, "addButtonText", design.AddButtonText, d.AddButtonText);
        Str(e, "emptyText", design.EmptyText, d.EmptyText);

        foreach (var column in design.Columns)
        {
            e.Add(new XElement("column",
                new XAttribute("field", column.Field),
                new XAttribute("width", column.Width),
                new XAttribute("visible", column.Visible)));
        }

        foreach (var size in design.PageSizeOptions)
        {
            e.Add(new XElement("pageSizeOption", new XAttribute("value", size)));
        }

        Text(e, "headerHtml", design.HeaderHtml);
        Text(e, "footerHtml", design.FooterHtml);
        Text(e, "customCss", design.CustomCss);
        Text(e, "customScript", design.CustomScript);
        designs.Add(e);
    }

    private static void ReadPage(XElement e, PageDesign design)
    {
        var d = PageDefaults;
        design.Title = ReadStr(e, "title", d.Title);
        design.Subtitle = ReadStr(e, "subtitle", d.Subtitle);
        design.CssClass = ReadStr(e, "cssClass", d.CssClass);
        design.Container = ReadEnum(e, "container", d.Container);
        design.FormLayout = ReadEnum(e, "formLayout", d.FormLayout);
        design.SubmitText = ReadStr(e, "submitText", d.SubmitText);
        design.CancelText = ReadStr(e, "cancelText", d.CancelText);
        design.ShowCancel = ReadBool(e, "showCancel", d.ShowCancel);
        design.SubmitCssClass = ReadStr(e, "submitCssClass", d.SubmitCssClass);
        design.Buttons = ReadEnum(e, "buttons", d.Buttons);
        design.MultiPage = ReadEnum(e, "multiPage", d.MultiPage);
        design.SuccessMessage = ReadStr(e, "successMessage", d.SuccessMessage);
        design.ConfirmBeforeSave = ReadBool(e, "confirmBeforeSave", d.ConfirmBeforeSave);
        design.ListLayout = ReadEnum(e, "listLayout", d.ListLayout);
        design.ShowRowNumbers = ReadBool(e, "showRowNumbers", d.ShowRowNumbers);
        design.ShowPager = ReadBool(e, "showPager", d.ShowPager);
        design.StickyHeader = ReadBool(e, "stickyHeader", d.StickyHeader);
        design.Striped = ReadBool(e, "striped", d.Striped);
        design.Hover = ReadBool(e, "hover", d.Hover);
        design.Bordered = ReadBool(e, "bordered", d.Bordered);
        design.ShowActionsFirst = ReadBool(e, "showActionsFirst", d.ShowActionsFirst);
        design.AddButtonText = ReadStr(e, "addButtonText", d.AddButtonText);
        design.EmptyText = ReadStr(e, "emptyText", d.EmptyText);

        foreach (var column in e.Elements("column"))
        {
            design.Columns.Add(new ListColumn
            {
                Field = ReadStr(column, "field", string.Empty),
                Width = ReadStr(column, "width", string.Empty),
                Visible = ReadBool(column, "visible", true),
            });
        }

        foreach (var size in e.Elements("pageSizeOption"))
        {
            if (int.TryParse(size.Attribute("value")?.Value, out var value))
            {
                design.PageSizeOptions.Add(value);
            }
        }

        design.HeaderHtml = ReadText(e, "headerHtml");
        design.FooterHtml = ReadText(e, "footerHtml");
        design.CustomCss = ReadText(e, "customCss");
        design.CustomScript = ReadText(e, "customScript");
    }

    // ---- project metadata -----------------------------------------------------------------------

    public static void AppendMeta(XElement root, ProjectMetadata meta)
    {
        if (meta.IsDefault())
        {
            return;
        }

        var d = MetaDefaults;
        var e = new XElement("meta");
        Str(e, "description", meta.Description, d.Description);
        Str(e, "author", meta.Author, d.Author);
        Str(e, "company", meta.Company, d.Company);
        Str(e, "copyright", meta.Copyright, d.Copyright);
        Str(e, "version", meta.Version, d.Version);
        Str(e, "rootNamespace", meta.RootNamespace, d.RootNamespace);
        Str(e, "assemblyName", meta.AssemblyName, d.AssemblyName);
        Str(e, "environment", meta.Environment, d.Environment);
        Int(e, "httpPort", meta.HttpPort, d.HttpPort);
        Int(e, "httpsPort", meta.HttpsPort, d.HttpsPort);
        Bool(e, "useHttps", meta.UseHttps, d.UseHttps);
        Bool(e, "enableHsts", meta.EnableHsts, d.EnableHsts);
        Bool(e, "enableResponseCompression", meta.EnableResponseCompression, d.EnableResponseCompression);
        Bool(e, "enableHealthEndpoint", meta.EnableHealthEndpoint, d.EnableHealthEndpoint);
        Str(e, "logLevel", meta.LogLevel, d.LogLevel);
        Str(e, "faviconPath", meta.FaviconPath, d.FaviconPath);
        Str(e, "logoPath", meta.LogoPath, d.LogoPath);
        Str(e, "footerText", meta.FooterText, d.FooterText);
        Str(e, "colorMode", meta.ColorMode, d.ColorMode);
        Str(e, "navbarStyle", meta.NavbarStyle, d.NavbarStyle);
        Text(e, "globalCss", meta.GlobalCss);
        Text(e, "globalScript", meta.GlobalScript);
        root.Add(e);
    }

    public static void ReadMeta(XElement root, Project project)
    {
        // Older project files have no <meta> element; the defaults reproduce the earlier output.
        if (root.Element("meta") is not { } e)
        {
            return;
        }

        var d = MetaDefaults;
        project.Meta = new ProjectMetadata
        {
            Description = ReadStr(e, "description", d.Description),
            Author = ReadStr(e, "author", d.Author),
            Company = ReadStr(e, "company", d.Company),
            Copyright = ReadStr(e, "copyright", d.Copyright),
            Version = ReadStr(e, "version", d.Version),
            RootNamespace = ReadStr(e, "rootNamespace", d.RootNamespace),
            AssemblyName = ReadStr(e, "assemblyName", d.AssemblyName),
            Environment = ReadStr(e, "environment", d.Environment),
            HttpPort = ReadInt(e, "httpPort", d.HttpPort),
            HttpsPort = ReadInt(e, "httpsPort", d.HttpsPort),
            UseHttps = ReadBool(e, "useHttps", d.UseHttps),
            EnableHsts = ReadBool(e, "enableHsts", d.EnableHsts),
            EnableResponseCompression = ReadBool(e, "enableResponseCompression", d.EnableResponseCompression),
            EnableHealthEndpoint = ReadBool(e, "enableHealthEndpoint", d.EnableHealthEndpoint),
            LogLevel = ReadStr(e, "logLevel", d.LogLevel),
            FaviconPath = ReadStr(e, "faviconPath", d.FaviconPath),
            LogoPath = ReadStr(e, "logoPath", d.LogoPath),
            FooterText = ReadStr(e, "footerText", d.FooterText),
            ColorMode = ReadStr(e, "colorMode", d.ColorMode),
            NavbarStyle = ReadStr(e, "navbarStyle", d.NavbarStyle),
            GlobalCss = ReadText(e, "globalCss"),
            GlobalScript = ReadText(e, "globalScript"),
        };
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static void Str(XElement e, string name, string value, string fallback)
    {
        if (!string.Equals(value, fallback, StringComparison.Ordinal))
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Bool(XElement e, string name, bool value, bool fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Int(XElement e, string name, int value, int fallback)
    {
        if (value != fallback)
        {
            e.Add(new XAttribute(name, value));
        }
    }

    private static void Enum<T>(XElement e, string name, T value, T fallback)
        where T : struct, System.Enum
    {
        if (!EqualityComparer<T>.Default.Equals(value, fallback))
        {
            e.Add(new XAttribute(name, value.ToString()));
        }
    }

    private static void Text(XElement e, string name, string value)
    {
        if (value.Length > 0)
        {
            e.Add(new XElement(name, value));
        }
    }

    private static string ReadStr(XElement e, string name, string fallback) => e.Attribute(name)?.Value ?? fallback;

    private static bool ReadBool(XElement e, string name, bool fallback) =>
        bool.TryParse(e.Attribute(name)?.Value, out var value) ? value : fallback;

    private static int ReadInt(XElement e, string name, int fallback) =>
        int.TryParse(e.Attribute(name)?.Value, out var value) ? value : fallback;

    private static T ReadEnum<T>(XElement e, string name, T fallback)
        where T : struct, System.Enum =>
        System.Enum.TryParse<T>(e.Attribute(name)?.Value, out var value) && System.Enum.IsDefined(value) ? value : fallback;

    private static string ReadText(XElement e, string name) => e.Element(name)?.Value ?? string.Empty;
}
