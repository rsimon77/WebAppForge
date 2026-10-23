using WebAppForge.Core.Model;

namespace WebAppForge.Core.Build;

/// <summary>Runs dotnet build or dotnet publish in a generated site folder.</summary>
public sealed class SiteBuilder
{
    private readonly IProcessRunner runner;
    private readonly Func<string, string?> locate;
    private readonly BuildTools tools;

    public SiteBuilder(BuildTools? tools = null)
        : this(new ProcessRunner(), ToolLocator.FindOnPath, tools)
    {
    }

    /// <param name="runner">Process runner (tests inject a fake).</param>
    /// <param name="locate">Finds an executable by name; returns null when it is not on PATH.</param>
    internal SiteBuilder(IProcessRunner runner, Func<string, string?> locate, BuildTools? tools = null)
    {
        this.runner = runner;
        this.locate = locate;
        this.tools = tools ?? BuildTools.Default;
    }

    /// <summary>Runs <c>dotnet build</c> for the site in <paramref name="siteFolder"/>.</summary>
    public Task<BuildResult> BuildAsync(
        string siteFolder,
        string configuration,
        IProgress<BuildOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync("build", siteFolder, configuration, publishFolder: null, runtimeIdentifier: string.Empty, selfContained: false, progress, cancellationToken);
    }

    /// <summary>Runs <c>dotnet publish</c> using the configuration, folder, runtime and self-contained options of <paramref name="settings"/>.</summary>
    public async Task<BuildResult> PublishAsync(
        string siteFolder,
        BuildSettings settings,
        IProgress<BuildOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var folder = string.IsNullOrWhiteSpace(settings.PublishFolder) ? "publish" : settings.PublishFolder;
        var publishPath = Path.GetFullPath(Path.Combine(siteFolder, folder));
        var result = await RunAsync("publish", siteFolder, settings.Configuration, publishPath, settings.RuntimeIdentifier, settings.SelfContained, progress, cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded
            ? new BuildResult
            {
                Succeeded = true,
                ExitCode = result.ExitCode,
                Duration = result.Duration,
                Errors = result.Errors,
                Warnings = result.Warnings,
                ParsedDiagnostics = result.ParsedDiagnostics,
                PublishPath = publishPath,
            }
            : result;
    }

    private async Task<BuildResult> RunAsync(
        string verb,
        string siteFolder,
        string configuration,
        string? publishFolder,
        string runtimeIdentifier,
        bool selfContained,
        IProgress<BuildOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(siteFolder))
        {
            return BuildResult.Failure($"The folder '{siteFolder}' does not exist. Generate the site first.");
        }

        var dotnet = BuildTools.Resolve("dotnet", tools.DotnetPath, locate, out var configuredFailure);
        if (configuredFailure is not null)
        {
            return configuredFailure;
        }

        if (string.IsNullOrEmpty(dotnet))
        {
            return BuildResult.Failure("The dotnet CLI was not found on PATH. Install the .NET SDK (https://dotnet.microsoft.com/download) and try again.");
        }

        var projects = Directory.GetFiles(siteFolder, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projects.Length != 1)
        {
            return BuildResult.Failure(projects.Length == 0
                ? $"No .csproj file was found in '{siteFolder}'. Generate the site first."
                : $"More than one .csproj file was found in '{siteFolder}'.");
        }

        var arguments = new List<string>
        {
            verb,
            projects[0],
            "-c", string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration,
            "-nologo",
            "-nodeReuse:false",
            "-p:UseSharedCompilation=false",
        };

        if (publishFolder is not null)
        {
            arguments.Add("-o");
            arguments.Add(publishFolder);
        }

        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            arguments.Add("-r");
            arguments.Add(runtimeIdentifier.Trim());
        }

        if (verb == "publish" && (selfContained || !string.IsNullOrWhiteSpace(runtimeIdentifier)))
        {
            arguments.Add("--self-contained");
            arguments.Add(selfContained ? "true" : "false");
        }

        return await ToolRun.RunAsync(runner, "dotnet " + verb, dotnet, arguments, siteFolder, progress, cancellationToken, tools.Timeout, ToolRun.ProxyEnvironment(tools.HttpProxy)).ConfigureAwait(false);
    }
}
