using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>The page types a table can design. The order is the order of the page type selector.</summary>
public enum PageKind
{
    List,
    View,
    Add,
    Copy,
    Edit,
    Delete,
    Search,
}

/// <summary>Which sections of the page designer apply to a page type.</summary>
public static class PageRules
{
    /// <summary>Add, copy, edit and search are forms with submit and cancel buttons.</summary>
    public static bool IsFormPage(PageKind kind) => kind is PageKind.Add or PageKind.Copy or PageKind.Edit or PageKind.Search;

    /// <summary>Every page type except the list can open as a modal dialog or an offcanvas panel from the list.</summary>
    public static bool CanChooseContainer(PageKind kind) => kind != PageKind.List;

    public static bool IsListPage(PageKind kind) => kind == PageKind.List;

    public static PageDesign DesignOf(TableInfo table, PageKind kind)
    {
        ArgumentNullException.ThrowIfNull(table);
        var designs = table.Designs;
        return kind switch
        {
            PageKind.List => designs.List,
            PageKind.View => designs.View,
            PageKind.Add => designs.Add,
            PageKind.Copy => designs.Copy,
            PageKind.Edit => designs.Edit,
            PageKind.Delete => designs.Delete,
            _ => designs.Search,
        };
    }
}

/// <summary>A Bootstrap context name with its colour, for the swatches next to style pickers.</summary>
public sealed record ContextStyle(string Name, string Hex)
{
    public IBrush Brush => new SolidColorBrush(Color.Parse(Hex));

    public override string ToString() => Name;
}

/// <summary>XAML: turns a Bootstrap context name into a swatch brush.</summary>
public sealed class StyleSwatchConverter : IValueConverter
{
    public static StyleSwatchConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var style = ContextStyles.All.FirstOrDefault(s => s.Name == value as string);
        return style?.Brush ?? Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The fixed choice lists of the customization pages (used by XAML through x:Static).</summary>
public static class ContextStyles
{
    public static IReadOnlyList<ContextStyle> All { get; } =
    [
        new("primary", "#0D6EFD"),
        new("secondary", "#6C757D"),
        new("success", "#198754"),
        new("danger", "#DC3545"),
        new("warning", "#FFC107"),
        new("info", "#0DCAF0"),
        new("light", "#F8F9FA"),
        new("dark", "#212529"),
    ];

    public static IReadOnlyList<string> Names { get; } = All.Select(s => s.Name).ToList();
}

public static class CustomizationChoices
{
    public const string NoneLabel = "(none)";

    public static IReadOnlyList<EditControl> EditControls { get; } = Enum.GetValues<EditControl>();

    public static IReadOnlyList<ViewFormat> ViewFormats { get; } = Enum.GetValues<ViewFormat>();

    public static IReadOnlyList<TextAlign> TextAligns { get; } = Enum.GetValues<TextAlign>();

    public static IReadOnlyList<ConditionOperator> Operators { get; } = Enum.GetValues<ConditionOperator>();

    public static IReadOnlyList<FormLayout> FormLayouts { get; } = Enum.GetValues<FormLayout>();

    public static IReadOnlyList<ListLayout> ListLayouts { get; } = Enum.GetValues<ListLayout>();

    public static IReadOnlyList<PageContainer> Containers { get; } = Enum.GetValues<PageContainer>();

    public static IReadOnlyList<ButtonPlacement> ButtonPlacements { get; } = Enum.GetValues<ButtonPlacement>();

    public static IReadOnlyList<PageKind> PageKinds { get; } = Enum.GetValues<PageKind>();

    public static IReadOnlyList<string> Environments { get; } = ["Development", "Production"];

    public static IReadOnlyList<string> LogLevels { get; } = ["Trace", "Debug", "Information", "Warning", "Error"];

    public static IReadOnlyList<string> ColorModes { get; } = ["auto", "light", "dark"];

    public static IReadOnlyList<string> NavbarStyles { get; } = ["primary", "secondary", "success", "danger", "warning", "info", "light", "dark"];

    public static IReadOnlyList<string> Themes { get; } = ["bootstrap"];

    public static IReadOnlyList<string> Configurations { get; } = ["Debug", "Release"];

    /// <summary>Common Bootstrap Icons names (without the "bi-" prefix). Any other name can be typed.</summary>
    public static IReadOnlyList<string> Icons { get; } =
    [
        "table", "people", "person", "person-circle", "box", "box-seam", "cart", "bag", "basket", "tag", "tags",
        "building", "shop", "house", "folder", "file-earmark-text", "journal", "book", "calendar", "calendar-event",
        "clock", "envelope", "chat", "bell", "star", "heart", "gear", "wrench", "tools", "clipboard",
        "clipboard-check", "list-check", "list-ul", "card-list", "grid", "bar-chart", "pie-chart", "graph-up",
        "currency-dollar", "cash-coin", "credit-card", "receipt", "truck", "geo-alt", "globe", "map", "image",
        "camera", "music-note", "film", "key", "lock", "shield-check", "bookmark", "flag", "inbox", "archive",
        "database", "hdd", "cpu", "phone", "laptop", "printer", "wallet2", "award", "briefcase", "lightning",
    ];
}
