using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WebAppForge.Desktop.Views;

/// <summary>The sidebar icon of a section: the geometry resource "Nav.{section name}", or none.</summary>
public sealed class NavIconConverter : IValueConverter
{
    public static NavIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && Application.Current?.TryGetResource("Nav." + name, null, out var icon) == true ? icon as Geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
