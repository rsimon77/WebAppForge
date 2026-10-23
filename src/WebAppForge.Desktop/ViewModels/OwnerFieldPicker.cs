using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The owner-field picker of the Tables tab. The list is "(none)" followed by the Text fields of the table.
/// "(none)" stands for an empty <see cref="TableInfo.OwnerField"/>.
/// </summary>
public static class OwnerFieldPicker
{
    public const string NoneLabel = "(none)";

    /// <summary>"(none)" followed by the names of the Text fields of <paramref name="table"/>.</summary>
    public static IReadOnlyList<string> Choices(TableInfo table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new[] { NoneLabel }
            .Concat(table.Fields.Where(f => f.Type == FieldType.Text).Select(f => f.Name))
            .ToList();
    }

    /// <summary>The text shown for a stored owner field.</summary>
    public static string Display(string? ownerField) => string.IsNullOrEmpty(ownerField) ? NoneLabel : ownerField;

    /// <summary>The value stored for a chosen item. "(none)" becomes an empty string.</summary>
    public static string Value(string? choice) => string.IsNullOrEmpty(choice) || choice == NoneLabel ? string.Empty : choice;
}

/// <summary>XAML: the picker items for a table (the binding source is the TableInfo).</summary>
public sealed class OwnerFieldChoicesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TableInfo table ? OwnerFieldPicker.Choices(table) : Array.Empty<string>();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("The picker writes through TablesViewModel.SetOwnerField.");
    }
}

/// <summary>XAML: the selected item for a stored owner field (the binding source is the OwnerField string).</summary>
public sealed class OwnerFieldDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return OwnerFieldPicker.Display(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("The picker writes through TablesViewModel.SetOwnerField.");
    }
}
