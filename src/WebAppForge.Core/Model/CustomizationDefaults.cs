namespace WebAppForge.Core.Model;

/// <summary>
/// Tells whether a customization object still has every property at its default. The generator and the project file use
/// this to leave defaults out, so a project that never customizes anything produces the same output and file as before.
/// </summary>
internal static class CustomizationDefaults
{
    public static bool IsDefault(this FieldUi ui) => ui.IsDefaultExceptFormPage() && ui.FormPage == new FieldUi().FormPage;

    /// <summary>The form page only matters to the multi-page forms, so by itself it does not make a field custom.</summary>
    public static bool IsDefaultExceptFormPage(this FieldUi ui)
    {
        var d = new FieldUi();
        return ui.Control == d.Control
            && ui.HelpText == d.HelpText
            && ui.Placeholder == d.Placeholder
            && ui.Tooltip == d.Tooltip
            && ui.DefaultValue == d.DefaultValue
            && ui.ReadOnlyOnEdit == d.ReadOnlyOnEdit
            && ui.AutoFocus == d.AutoFocus
            && ui.Rows == d.Rows
            && ui.Options.Count == 0
            && ui.Lookup is null
            && ui.MaxLength == d.MaxLength
            && ui.MinLength == d.MinLength
            && ui.Min == d.Min
            && ui.Max == d.Max
            && ui.Step == d.Step
            && ui.Pattern == d.Pattern
            && ui.PatternMessage == d.PatternMessage
            && ui.RequiredMessage == d.RequiredMessage
            && ui.Format == d.Format
            && ui.FormatString == d.FormatString
            && ui.TrueText == d.TrueText
            && ui.FalseText == d.FalseText
            && ui.BadgeRules.Count == 0
            && ui.Width == d.Width
            && ui.Align == d.Align
            && ui.CssClass == d.CssClass
            && ui.Style == d.Style
            && ui.Sortable == d.Sortable
            && ui.Group == d.Group;
    }

    public static bool IsDefault(this TableUi ui)
    {
        var d = new TableUi();
        return ui.Description == d.Description
            && ui.Icon == d.Icon
            && ui.MenuGroup == d.MenuGroup
            && ui.ShowInMenu == d.ShowInMenu
            && ui.MenuOrder == d.MenuOrder
            && ui.CssClass == d.CssClass
            && ui.RowStyles.Count == 0;
    }

    public static bool IsDefault(this PageDesign design) => design.IsDefaultExceptMultiPage() && design.MultiPage == new PageDesign().MultiPage;

    /// <summary>The multi-page mode only matters when the table defines form pages, so by itself it does not make a page designed.</summary>
    public static bool IsDefaultExceptMultiPage(this PageDesign design)
    {
        var d = new PageDesign();
        return design.Title == d.Title
            && design.Subtitle == d.Subtitle
            && design.HeaderHtml == d.HeaderHtml
            && design.FooterHtml == d.FooterHtml
            && design.CssClass == d.CssClass
            && design.CustomCss == d.CustomCss
            && design.CustomScript == d.CustomScript
            && design.Container == d.Container
            && design.FormLayout == d.FormLayout
            && design.SubmitText == d.SubmitText
            && design.CancelText == d.CancelText
            && design.ShowCancel == d.ShowCancel
            && design.SubmitCssClass == d.SubmitCssClass
            && design.Buttons == d.Buttons
            && design.SuccessMessage == d.SuccessMessage
            && design.ConfirmBeforeSave == d.ConfirmBeforeSave
            && design.ListLayout == d.ListLayout
            && design.Columns.Count == 0
            && design.ShowRowNumbers == d.ShowRowNumbers
            && design.ShowPager == d.ShowPager
            && design.PageSizeOptions.Count == 0
            && design.StickyHeader == d.StickyHeader
            && design.Striped == d.Striped
            && design.Hover == d.Hover
            && design.Bordered == d.Bordered
            && design.ShowActionsFirst == d.ShowActionsFirst
            && design.FilterBuilder == d.FilterBuilder
            && design.QuickFilter == d.QuickFilter
            && design.ResetButton == d.ResetButton
            && design.AddButtonText == d.AddButtonText
            && design.EmptyText == d.EmptyText;
    }

    public static bool IsDefault(this PageDesigns designs) =>
        designs.List.IsDefault() && designs.View.IsDefault() && designs.Add.IsDefault() && designs.Copy.IsDefault()
        && designs.Edit.IsDefault() && designs.Delete.IsDefault() && designs.Search.IsDefault();

    public static bool IsDefaultExceptMultiPage(this PageDesigns designs) =>
        designs.List.IsDefaultExceptMultiPage() && designs.View.IsDefaultExceptMultiPage() && designs.Add.IsDefaultExceptMultiPage()
        && designs.Copy.IsDefaultExceptMultiPage() && designs.Edit.IsDefaultExceptMultiPage() && designs.Delete.IsDefaultExceptMultiPage()
        && designs.Search.IsDefaultExceptMultiPage();

    public static bool IsDefault(this ProjectMetadata meta) => meta.IsDefaultExceptCompression()
        && meta.EnableResponseCompression == new ProjectMetadata().EnableResponseCompression;

    /// <summary>True when everything except <see cref="ProjectMetadata.EnableResponseCompression"/> is at its default.</summary>
    public static bool IsDefaultExceptCompression(this ProjectMetadata meta)
    {
        var d = new ProjectMetadata();
        return meta.Description == d.Description
            && meta.Author == d.Author
            && meta.Company == d.Company
            && meta.Copyright == d.Copyright
            && meta.Version == d.Version
            && meta.RootNamespace == d.RootNamespace
            && meta.AssemblyName == d.AssemblyName
            && meta.Environment == d.Environment
            && meta.HttpPort == d.HttpPort
            && meta.HttpsPort == d.HttpsPort
            && meta.UseHttps == d.UseHttps
            && meta.EnableHsts == d.EnableHsts
            && meta.EnableHealthEndpoint == d.EnableHealthEndpoint
            && meta.LogLevel == d.LogLevel
            && meta.FaviconPath == d.FaviconPath
            && meta.LogoPath == d.LogoPath
            && meta.FooterText == d.FooterText
            && meta.GlobalCss == d.GlobalCss
            && meta.GlobalScript == d.GlobalScript
            && meta.ColorMode == d.ColorMode
            && meta.NavbarStyle == d.NavbarStyle;
    }
}
