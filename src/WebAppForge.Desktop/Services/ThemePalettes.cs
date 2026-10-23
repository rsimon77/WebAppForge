using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace WebAppForge.Desktop.Services;

/// <summary>A named accent colour.</summary>
public sealed record ThemePalette(string Name, Color Color);

/// <summary>
/// Nineteen named palettes: ten hues (the Bootstrap 5.3 hues; Blue, Green, Cyan, Yellow and Red are the primary,
/// success, info, warning and danger roles) and nine neutral steps. The colours are approximations of the
/// Bootstrap 5.3 palette family, not an exact copy of its CSS variables.
/// </summary>
public static class ThemePalettes
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new("Blue (primary)", Color.Parse("#0D6EFD")),
        new("Indigo", Color.Parse("#6610F2")),
        new("Purple", Color.Parse("#6F42C1")),
        new("Pink", Color.Parse("#D63384")),
        new("Red (danger)", Color.Parse("#DC3545")),
        new("Orange", Color.Parse("#FD7E14")),
        new("Yellow (warning)", Color.Parse("#FFC107")),
        new("Green (success)", Color.Parse("#198754")),
        new("Teal", Color.Parse("#20C997")),
        new("Cyan (info)", Color.Parse("#0DCAF0")),
        new("Neutral 100", Color.Parse("#F8F9FA")),
        new("Neutral 200", Color.Parse("#E9ECEF")),
        new("Neutral 300", Color.Parse("#DEE2E6")),
        new("Neutral 400", Color.Parse("#CED4DA")),
        new("Neutral 500", Color.Parse("#ADB5BD")),
        new("Neutral 600", Color.Parse("#6C757D")),
        new("Neutral 700", Color.Parse("#495057")),
        new("Neutral 800", Color.Parse("#343A40")),
        new("Neutral 900", Color.Parse("#212529")),
    ];

    public static ThemePalette Default => All[0];

    /// <summary>The palette with this name (case-insensitive), or the default when the name is unknown.</summary>
    public static ThemePalette Find(string? name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
