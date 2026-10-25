using System.IO;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.Services;

/// <summary>
/// Loads a user's AXAML resource dictionary. Only resources are accepted: the root must be a ResourceDictionary and
/// it must not declare a code-behind class (x:Class), so no code is compiled or run from the file.
/// </summary>
public static class CustomStyleLoader
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <exception cref="InvalidDataException">The file is not a valid resource dictionary.</exception>
    public static ResourceDictionary Load(string path)
    {
        var text = File.ReadAllText(path);
        var root = XDocument.Parse(text).Root
            ?? throw new InvalidDataException(Localizer.Instance["CustomStyleLoader_TheFileIsEmpty"]);

        if (root.Name.LocalName != nameof(ResourceDictionary))
        {
            throw new InvalidDataException(Localizer.Instance["CustomStyleLoader_TheRootElementMustBeResourceDictionary"]);
        }

        if (root.Attribute(XamlNamespace + "Class") is not null)
        {
            throw new InvalidDataException(Localizer.Instance["CustomStyleLoader_AFileWithXClassIs"]);
        }

        var loaded = AvaloniaRuntimeXamlLoader.Parse(text, typeof(CustomStyleLoader).Assembly);
        return loaded as ResourceDictionary
            ?? throw new InvalidDataException(Localizer.Instance["CustomStyleLoader_TheRootElementMustBeResourceDictionary"]);
    }
}
