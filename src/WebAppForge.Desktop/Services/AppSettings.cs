using System;
using System.Collections.Generic;
using System.Linq;

namespace WebAppForge.Desktop.Services;

/// <summary>Light or dark chrome, or whatever the operating system uses.</summary>
public enum AppearanceMode
{
    Light,
    Dark,
    System,
}

/// <summary>Build configuration offered after generating. Stored only; the generate step does not build yet.</summary>
public enum BuildConfiguration
{
    Debug,
    Release,
}

/// <summary>Which service translates text. Stored only; no translation feature reads it yet.</summary>
public enum TranslationProvider
{
    None,
    Google,
    Claude,
}

/// <summary>Verbosity of the application log. Stored only; the application does not write a log yet.</summary>
public enum AppLogLevel
{
    Error,
    Warn,
    Info,
    Debug,
}

/// <summary>
/// Per-user settings, kept in the app data folder (not in the project file). API keys are never part of the
/// settings: they are read from environment variables only.
/// </summary>
public sealed class AppSettings
{
    public const int DefaultTimeoutMinutes = 30;
    public const int MinTimeoutMinutes = 1;
    public const int MaxTimeoutMinutes = 1440;

    public const int DefaultRecentFilesLength = 10;
    public const int MinRecentFilesLength = 5;
    public const int MaxRecentFilesLength = 20;

    public const int MinAutosaveMinutes = 0;
    public const int MaxAutosaveMinutes = 120;

    public const int DefaultUiFontSize = 13;
    public const int MinUiFontSize = 10;
    public const int MaxUiFontSize = 20;

    public const int DefaultRequestTimeoutSeconds = 30;
    public const int MinRequestTimeoutSeconds = 0;
    public const int MaxRequestTimeoutSeconds = 600;

    public const string DefaultOutputPattern = "generated";
    public const string DefaultClaudeModelId = "claude-haiku-5-5";

    /// <summary>The page sizes the generated pages offer.</summary>
    public static IReadOnlyList<int> PageSizeChoices { get; } = [10, 20, 50, 100];

    // General

    /// <summary>Idle minutes before the "Session idle. Save now?" notice appears.</summary>
    public int SessionTimeoutMinutes { get; set; } = DefaultTimeoutMinutes;

    public bool ReopenLastProject { get; set; }

    /// <summary>Path of the project that was open last (set by Open and Save). Used when <see cref="ReopenLastProject"/> is on.</summary>
    public string? LastProjectPath { get; set; }

    public int RecentFilesLength { get; set; } = DefaultRecentFilesLength;

    /// <summary>Most recently used project files, newest first, at most <see cref="RecentFilesLength"/> long.</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Ask Save, Discard or Cancel on exit when the project has unsaved changes.</summary>
    public bool ConfirmOnExit { get; set; } = true;

    /// <summary>Minutes between backup copies (&lt;name&gt;.autosave.xml next to the project file). 0 turns it off.</summary>
    public int AutosaveMinutes { get; set; }

    // Appearance

    /// <summary>Name of a <see cref="ThemePalettes"/> entry. Applied as the accent colour.</summary>
    public string ThemeName { get; set; } = ThemePalettes.Default.Name;

    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;

    public int UiFontSize { get; set; } = DefaultUiFontSize;

    public bool CompactTables { get; set; }

    /// <summary>Absolute path of an AXAML resource dictionary, or null for none.</summary>
    public string? CustomStylesPath { get; set; }

    // Generate

    public string DefaultOutputFolderPattern { get; set; } = DefaultOutputPattern;

    public int DefaultPageSize { get; set; } = 20;

    public bool OpenOutputAfterGenerate { get; set; }

    /// <summary>Delete the output folder's contents before generating. Off by default. Stored only.</summary>
    public bool ClearOutputBeforeGenerate { get; set; }

    public bool BuildAfterGenerate { get; set; }

    public BuildConfiguration DefaultBuildConfiguration { get; set; } = BuildConfiguration.Debug;

    // Tools and paths (empty or null = use PATH)

    public string? DotnetPath { get; set; }

    public string? NodePath { get; set; }

    public string? DockerPath { get; set; }

    // Translation and AI

    public TranslationProvider TranslationProvider { get; set; } = TranslationProvider.None;

    public string ClaudeModelId { get; set; } = DefaultClaudeModelId;

    // Network

    public string? HttpProxyUrl { get; set; }

    /// <summary>Seconds. 0 = no limit. Limits translation requests and, as the BuildTools timeout, dotnet build and publish.</summary>
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    // Advanced

    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Info;

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.RecentFiles = [.. RecentFiles];
        return copy;
    }

    /// <summary>A copy with out-of-range values corrected: numbers are clamped, unknown names fall back to the default.</summary>
    public AppSettings Normalized()
    {
        var copy = Clone();
        copy.SessionTimeoutMinutes = Math.Clamp(copy.SessionTimeoutMinutes, MinTimeoutMinutes, MaxTimeoutMinutes);
        copy.RecentFilesLength = Math.Clamp(copy.RecentFilesLength, MinRecentFilesLength, MaxRecentFilesLength);
        copy.AutosaveMinutes = Math.Clamp(copy.AutosaveMinutes, MinAutosaveMinutes, MaxAutosaveMinutes);
        copy.UiFontSize = Math.Clamp(copy.UiFontSize, MinUiFontSize, MaxUiFontSize);
        copy.RequestTimeoutSeconds = Math.Clamp(copy.RequestTimeoutSeconds, MinRequestTimeoutSeconds, MaxRequestTimeoutSeconds);
        if (!PageSizeChoices.Contains(copy.DefaultPageSize))
        {
            copy.DefaultPageSize = new AppSettings().DefaultPageSize;
        }

        copy.ThemeName = ThemePalettes.Find(copy.ThemeName).Name;
        copy.CustomStylesPath = NullIfBlank(copy.CustomStylesPath);
        copy.LastProjectPath = NullIfBlank(copy.LastProjectPath);
        copy.DotnetPath = NullIfBlank(copy.DotnetPath);
        copy.NodePath = NullIfBlank(copy.NodePath);
        copy.DockerPath = NullIfBlank(copy.DockerPath);
        copy.HttpProxyUrl = NullIfBlank(copy.HttpProxyUrl);
        if (string.IsNullOrWhiteSpace(copy.DefaultOutputFolderPattern))
        {
            copy.DefaultOutputFolderPattern = DefaultOutputPattern;
        }

        if (string.IsNullOrWhiteSpace(copy.ClaudeModelId))
        {
            copy.ClaudeModelId = DefaultClaudeModelId;
        }

        copy.RecentFiles = (copy.RecentFiles ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(copy.RecentFilesLength)
            .ToList();
        return copy;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
