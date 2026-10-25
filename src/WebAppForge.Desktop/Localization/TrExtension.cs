using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace WebAppForge.Desktop.Localization;

/// <summary>
/// <c>{l:Tr Key}</c> in AXAML: the text of Key from Localization/Strings.resx in the current UI language. It is a binding,
/// so the text changes at once when the language is switched in Options.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding(nameof(LocalizedText.Value)) { Source = Localizer.Instance.Text(Key), Mode = BindingMode.OneWay };
}
