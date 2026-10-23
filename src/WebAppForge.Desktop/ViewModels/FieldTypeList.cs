using System;
using System.Collections.Generic;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>Static list of field types, used by XAML {x:Static} for the Type combo box.</summary>
public static class FieldTypeList
{
    public static IReadOnlyList<FieldType> Values { get; } = Enum.GetValues<FieldType>();
}
