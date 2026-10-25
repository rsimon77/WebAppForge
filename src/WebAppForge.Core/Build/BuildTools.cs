namespace WebAppForge.Core.Build;

/// <summary>Outcome of <see cref="BuildTools.ProbeVersionAsync(string, CancellationToken)"/>.</summary>
public sealed record ToolVersionResult(bool Succeeded, string Version, string Error);

/// <summary>
/// Optional explicit tool paths (null or empty = look the tool up on PATH), plus an optional HTTP proxy for
/// <c>dotnet</c> and an optional per-run timeout.
/// </summary>
public sealed record BuildTools(string? DotnetPath = null, string? DockerPath = null, string? NodePath = null)
{
    public static BuildTools Default { get; } = new();

    /// <summary>HTTP proxy passed to dotnet as HTTP_PROXY/HTTPS_PROXY; null or empty = none.</summary>
    public string? HttpProxy { get; init; }

    /// <summary>Per-run timeout; null = none. A run that exceeds it is cancelled and its process tree killed.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Finds <paramref name="name"/> (for example "dotnet") on PATH; null when it is not found.</summary>
    public static string? Detect(string name) => ToolLocator.FindOnPath(name);

    /// <summary>Runs <c>&lt;exe&gt; --version</c> with a 5 second timeout and returns the first output line.</summary>
    public static Task<ToolVersionResult> ProbeVersionAsync(string executable, CancellationToken cancellationToken = default)
        => ProbeVersionAsync(new ProcessRunner(), executable, TimeSpan.FromSeconds(5), cancellationToken);

    internal static async Task<ToolVersionResult> ProbeVersionAsync(IProcessRunner runner, string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new ToolVersionResult(false, string.Empty, Localization.CoreMessages.Text("Build_NoExecutable"));
        }

        string? first = null;
        string? firstError = null;
        var directory = Path.GetDirectoryName(executable);
        var spec = new ProcessSpec
        {
            FileName = executable,
            Arguments = ["--version"],
            WorkingDirectory = !string.IsNullOrEmpty(directory) && Directory.Exists(directory) ? directory : Environment.CurrentDirectory,
        };

        void OnLine(OutputStream stream, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (stream == OutputStream.StdOut)
            {
                first ??= text.Trim();
            }
            else
            {
                firstError ??= text.Trim();
            }
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var exit = await runner.RunAsync(spec, OnLine, timeoutSource.Token).ConfigureAwait(false);
            if (exit != 0)
            {
                return new ToolVersionResult(false, string.Empty, firstError ?? first ?? Localization.CoreMessages.Format("Build_VersionExitCode", executable, exit));
            }

            return first is null
                ? new ToolVersionResult(false, string.Empty, Localization.CoreMessages.Format("Build_VersionNothing", executable))
                : new ToolVersionResult(true, first, string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ToolVersionResult(false, string.Empty, Localization.CoreMessages.Format("Build_VersionTimedOut", executable, ToolRun.FormatDuration(timeout)));
        }
        catch (OperationCanceledException)
        {
            return new ToolVersionResult(false, string.Empty, Localization.CoreMessages.Text("Build_Cancelled"));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new ToolVersionResult(false, string.Empty, Localization.CoreMessages.Format("Build_CouldNotStart", executable, ex.Message));
        }
    }

    /// <summary>Resolves a tool: an explicit path must exist as a file; empty falls back to <paramref name="locate"/>.</summary>
    internal static string? Resolve(string name, string? configured, Func<string, string?> locate, out BuildResult? failure)
    {
        failure = null;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = configured.Trim().Trim('"');
            if (File.Exists(path))
            {
                return path;
            }

            failure = BuildResult.MissingToolPath(Localization.CoreMessages.Format("Build_ConfiguredPathMissing", name, configured));
            return null;
        }

        return locate(name);
    }
}
