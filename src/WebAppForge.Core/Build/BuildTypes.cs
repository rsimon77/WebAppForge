namespace WebAppForge.Core.Build;

public enum OutputStream
{
    StdOut,
    StdErr,
}

/// <summary>One line of process output.</summary>
public sealed record BuildOutputLine(OutputStream Stream, string Text, DateTimeOffset Timestamp);

/// <summary>A diagnostic parsed from MSBuild output: path(line,col): error CODE: message.</summary>
public sealed record BuildDiagnostic(string File, int Line, string Code, string Message, bool IsError);

public sealed class BuildResult
{
    public bool Succeeded { get; init; }

    public int ExitCode { get; init; }

    public TimeSpan Duration { get; init; }

    public int Errors { get; init; }

    public int Warnings { get; init; }

    public bool Cancelled { get; init; }

    /// <summary>A short human readable summary, set for failures (missing tool, cancelled, exit code).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>True when a tool path set in the options does not exist; the editor then points to the options.</summary>
    public bool ToolPathMissing { get; init; }

    public IReadOnlyList<BuildDiagnostic> ParsedDiagnostics { get; init; } = [];

    /// <summary>Folder the output was published to, when a publish succeeded.</summary>
    public string? PublishPath { get; init; }

    public static BuildResult Failure(string message, int exitCode = -1, bool cancelled = false, TimeSpan duration = default) => new()
    {
        Succeeded = false,
        ExitCode = exitCode,
        Cancelled = cancelled,
        Message = message,
        Duration = duration,
    };

    /// <summary>A failure because a tool path set in the options does not exist (<see cref="ToolPathMissing"/>).</summary>
    public static BuildResult MissingToolPath(string message) => new()
    {
        Succeeded = false,
        ExitCode = -1,
        Message = message,
        ToolPathMissing = true,
    };
}
