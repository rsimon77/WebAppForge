using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WebAppForge.Core.Build;

/// <summary>Shared logic for running a CLI tool with progress and turning its output into a <see cref="BuildResult"/>.</summary>
internal static class ToolRun
{
    public static string FormatDuration(TimeSpan span) =>
        span.TotalSeconds < 120
            ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0.##} seconds")
            : string.Create(CultureInfo.InvariantCulture, $"{span.TotalMinutes:0.##} minutes");

    /// <summary>Proxy environment for dotnet; empty when no proxy is configured.</summary>
    public static IReadOnlyDictionary<string, string> ProxyEnvironment(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy))
        {
            return new Dictionary<string, string>();
        }

        var value = proxy.Trim();
        return new Dictionary<string, string>
        {
            ["HTTP_PROXY"] = value,
            ["HTTPS_PROXY"] = value,
        };
    }

    public static async Task<BuildResult> RunAsync(
        IProcessRunner runner,
        string tool,
        string toolPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IProgress<BuildOutputLine>? progress,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var lines = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var spec = new ProcessSpec
        {
            FileName = toolPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            Environment = environment ?? new Dictionary<string, string>(),
        };

        void OnLine(OutputStream stream, string text)
        {
            lock (lines)
            {
                lines.Add(text);
            }

            progress?.Report(new BuildOutputLine(stream, text, DateTimeOffset.Now));
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = timeout is { } t && t > TimeSpan.Zero ? t : (TimeSpan?)null;
        if (limit is { } l)
        {
            timeoutSource.CancelAfter(l);
        }

        int exitCode;
        try
        {
            exitCode = await runner.RunAsync(spec, OnLine, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && limit is not null)
        {
            return BuildResult.Failure($"{tool} timed out after {FormatDuration(limit.Value)} and was stopped.", duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return BuildResult.Failure($"{tool} was cancelled.", cancelled: true, duration: stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return BuildResult.Failure($"Could not start {tool}: {ex.Message}", duration: stopwatch.Elapsed);
        }

        string[] snapshot;
        lock (lines)
        {
            snapshot = [.. lines];
        }

        var parsed = MsBuildOutput.Parse(snapshot);
        return new BuildResult
        {
            Succeeded = exitCode == 0,
            ExitCode = exitCode,
            Duration = stopwatch.Elapsed,
            Errors = parsed.Errors,
            Warnings = parsed.Warnings,
            ParsedDiagnostics = parsed.Diagnostics,
            Message = exitCode == 0 ? string.Empty : $"{tool} failed with exit code {exitCode}.",
        };
    }
}

/// <summary>Parses MSBuild console output.</summary>
public static partial class MsBuildOutput
{
    public sealed record Parsed(int Errors, int Warnings, bool BuildSucceeded, IReadOnlyList<BuildDiagnostic> Diagnostics);

    [GeneratedRegex(@"^\s*(?<file>.+?)(?:\((?<line>\d+)(?:,\d+)*\))?:\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<msg>.*?)(?:\s+\[[^\]]+\])?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticRegex();

    [GeneratedRegex(@"^\s*(?<n>\d+)\s+Warning\(s\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WarningCountRegex();

    [GeneratedRegex(@"^\s*(?<n>\d+)\s+Error\(s\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCountRegex();

    /// <summary>Parses one line; null when it is not a diagnostic.</summary>
    public static BuildDiagnostic? ParseDiagnostic(string line)
    {
        var match = DiagnosticRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return new BuildDiagnostic(
            match.Groups["file"].Value.Trim(),
            match.Groups["line"].Success ? int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture) : 0,
            match.Groups["code"].Value,
            match.Groups["msg"].Value.Trim(),
            string.Equals(match.Groups["sev"].Value, "error", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses all lines. MSBuild prints each diagnostic twice (inline and in the summary); duplicates are removed.</summary>
    public static Parsed Parse(IEnumerable<string> lines)
    {
        int? errors = null;
        int? warnings = null;
        var succeeded = false;
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<BuildDiagnostic>();

        foreach (var line in lines)
        {
            if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase))
            {
                succeeded = true;
            }

            if (WarningCountRegex().Match(line) is { Success: true } w)
            {
                warnings = int.Parse(w.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
            else if (ErrorCountRegex().Match(line) is { Success: true } e)
            {
                errors = int.Parse(e.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
            else if (ParseDiagnostic(line) is { } diagnostic && seen.Add(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }
        }

        return new Parsed(
            errors ?? diagnostics.Count(d => d.IsError),
            warnings ?? diagnostics.Count(d => !d.IsError),
            succeeded,
            diagnostics);
    }
}
