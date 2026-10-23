using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Build;

namespace WebAppForge.Desktop.Services;

/// <summary>What detection found for one tool.</summary>
public sealed record ToolDetection(string Tool, bool Found, string? Path, string? Version, string Message);

/// <summary>
/// Finds a tool (a configured path, or the name on PATH) with <see cref="BuildTools.Detect(string)"/> and asks it for its
/// version with <see cref="BuildTools.ProbeVersionAsync(string, CancellationToken)"/> (5 second limit). Both calls can be replaced for tests.
/// </summary>
public sealed class ToolDetector
{
    private readonly Func<string, string?> _detect;
    private readonly Func<string, CancellationToken, Task<ToolVersionResult>> _probe;
    private readonly Func<string, bool> _fileExists;

    /// <param name="detect">Null uses <see cref="BuildTools.Detect(string)"/>.</param>
    /// <param name="probe">Null uses <see cref="BuildTools.ProbeVersionAsync(string, CancellationToken)"/>.</param>
    /// <param name="fileExists">Null uses the file system.</param>
    public ToolDetector(
        Func<string, string?>? detect = null,
        Func<string, CancellationToken, Task<ToolVersionResult>>? probe = null,
        Func<string, bool>? fileExists = null)
    {
        _detect = detect ?? BuildTools.Detect;
        _probe = probe ?? BuildTools.ProbeVersionAsync;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>The tool's path on PATH, or null.</summary>
    public string? ResolveOnPath(string tool) => _detect(tool);

    /// <summary>The configured path when the file exists, else null; with nothing configured, the PATH lookup.</summary>
    public string? Find(string tool, string? configuredPath)
        => string.IsNullOrWhiteSpace(configuredPath)
            ? ResolveOnPath(tool)
            : _fileExists(configuredPath.Trim().Trim('"')) ? configuredPath.Trim().Trim('"') : null;

    /// <param name="tool">The program name looked up on PATH, e.g. "dotnet".</param>
    /// <param name="configuredPath">A path chosen by the user, or empty to use PATH.</param>
    public async Task<ToolDetection> DetectAsync(string tool, string? configuredPath)
    {
        var configured = !string.IsNullOrWhiteSpace(configuredPath);
        var path = configured ? configuredPath!.Trim().Trim('"') : ResolveOnPath(tool);
        if (path is null)
        {
            return new ToolDetection(tool, false, null, null, $"{tool} was not found on PATH.");
        }

        if (configured && !_fileExists(path))
        {
            return new ToolDetection(tool, false, path, null, $"The file {path} does not exist.");
        }

        var result = await _probe(path, CancellationToken.None);
        return result.Succeeded
            ? new ToolDetection(tool, true, path, result.Version, $"{path}: {result.Version}")
            : new ToolDetection(tool, false, path, null, $"{path}: {result.Error}");
    }
}
