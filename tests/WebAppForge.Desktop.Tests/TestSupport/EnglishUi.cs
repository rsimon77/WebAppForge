using System;
using System.Runtime.CompilerServices;
using WebAppForge.Desktop.Localization;
using WebAppForge.Desktop.Services;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>
/// Runs the tests in English whatever the machine's UI language: the tests compare English texts. The environment variable
/// also wins over the UiLanguage setting when a test applies settings through AvaloniaAppearance.
/// </summary>
internal static class EnglishUi
{
    [ModuleInitializer]
    internal static void Pin()
    {
        Environment.SetEnvironmentVariable(AvaloniaAppearance.UiLanguageVariable, "en");
        Localizer.Instance.SetCulture(Localizer.NeutralCulture);
    }
}

/// <summary>Tests that switch the app-wide UI language run alone, so no other test sees the other language.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiLanguageCollection
{
    public const string Name = "UI language";
}
