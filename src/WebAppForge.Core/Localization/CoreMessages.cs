using System.Globalization;
using System.Resources;

namespace WebAppForge.Core.Localization;

/// <summary>
/// The messages Core gives the editor (checks of the project, generator, build and translation errors) in the UI language.
/// English is in Localization/Messages.resx; another language is a Messages.&lt;culture&gt;.resx beside it, built into a satellite
/// assembly. A key missing in a language falls back to English. Generated sites never contain these texts.
/// </summary>
public static class CoreMessages
{
    private static readonly ResourceManager Resources = new("WebAppForge.Core.Localization.Messages", typeof(CoreMessages).Assembly);

    /// <summary>The language of the messages; null means the current UI culture. The desktop app sets it to its UI language.</summary>
    public static CultureInfo? Culture { get; set; }

    /// <summary>The language the messages are written in.</summary>
    internal static CultureInfo Current =>Culture ?? CultureInfo.CurrentUICulture;

    /// <summary>The text of <paramref name="key"/>, or the key itself when it is missing everywhere.</summary>
    public static string Text(string key) => Resources.GetString(key, Current) ?? key;

    /// <summary>The text of <paramref name="key"/> with {0}, {1}... filled in, formatted for the message language.</summary>
    public static string Format(string key, params object?[] args) => string.Format(Current, Text(key), args);
}
