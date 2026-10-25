using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WebAppForge.Desktop.Localization;

/// <summary>
/// Shows an id in the UI language: the text of the key ConverterParameter + id, for lists whose items are English ids
/// (such as the field designer sections). The id itself stays the bound value.
/// </summary>
public sealed class TrConverter : IValueConverter
{
    public static TrConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? null : Localizer.Instance[$"{parameter}{value}"];

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
