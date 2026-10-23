using System.Diagnostics;
using System.Text;

namespace WebAppForge.Core.Build;

/// <summary>A process to start. Arguments are passed as a list, never as a shell string.</summary>
internal sealed class ProcessSpec
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public required string WorkingDirectory { get; init; }

    /// <summary>Extra environment variables for the child process.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Called with the process id right after the process started.</summary>
    public Action<int>? OnStarted { get; init; }
}

internal interface IProcessRunner
{
    /// <summary>
    /// Runs the process, reporting each output line, and returns its exit code. When <paramref name="cancellationToken"/>
    /// is cancelled the whole process tree is killed and <see cref="OperationCanceledException"/> is thrown.
    /// A process that cannot be started throws.
    /// </summary>
    Task<int> RunAsync(ProcessSpec spec, Action<OutputStream, string> onLine, CancellationToken cancellationToken);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(ProcessSpec spec, Action<OutputStream, string> onLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo(spec.FileName)
        {
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var pair in spec.Environment)
        {
            info.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine(OutputStream.StdOut, e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine(OutputStream.StdErr, e.Data);
            }
        };

        process.Start();
        process.StandardInput.Close();
        spec.OnStarted?.Invoke(process.Id);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            process.WaitForExit(5000);
            throw;
        }

        return process.ExitCode;
    }
}

/// <summary>Finds an executable on PATH.</summary>
internal static class ToolLocator
{
    public static string? FindOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { tool + ".exe", tool + ".cmd", tool + ".bat" }
            : new[] { tool };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Invalid characters in a PATH entry: skip it.
                }
            }
        }

        return null;
    }
}
