using System;
using System.Diagnostics;
using System.IO;

namespace WebAppForge.Desktop.Services;

/// <summary>
/// The parts of the machine the Options dialog touches: environment variables (for the key status), tool detection
/// and opening a folder. Tests replace them.
/// </summary>
public sealed class OptionsEnvironment
{
    public const string GoogleKeyVariable = "GOOGLE_TRANSLATE_API_KEY";
    public const string AnthropicKeyVariable = "ANTHROPIC_API_KEY";

    /// <summary>Reads an environment variable. Only used to tell whether a key is set; the value is never shown.</summary>
    public Func<string, string?> GetVariable { get; init; } = name => Environment.GetEnvironmentVariable(name);

    public ToolDetector Detector { get; init; } = new();

    /// <summary>Opens a folder in the file manager.</summary>
    public Action<string> OpenFolder { get; init; } = OpenFolderInShell;

    public string SettingsFolder { get; init; } = Path.GetDirectoryName(SettingsStore.DefaultFilePath)!;

    private static void OpenFolderInShell(string folder)
    {
        var program = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false };
        startInfo.ArgumentList.Add(folder);
        using var process = Process.Start(startInfo);
    }
}
