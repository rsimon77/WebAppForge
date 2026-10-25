using WebAppForge.Core.Model;

namespace WebAppForge.Core.Build;

/// <summary>Runs <c>docker build</c> in the generated site folder.</summary>
public sealed class DockerBuilder
{
    private readonly IProcessRunner runner;
    private readonly Func<string, string?> locate;
    private readonly BuildTools tools;

    public DockerBuilder(BuildTools? tools = null)
        : this(new ProcessRunner(), ToolLocator.FindOnPath, tools)
    {
    }

    internal DockerBuilder(IProcessRunner runner, Func<string, string?> locate, BuildTools? tools = null)
    {
        this.runner = runner;
        this.locate = locate;
        this.tools = tools ?? BuildTools.Default;
    }

    /// <summary>Builds the image when <see cref="BuildSettings.BuildDockerImage"/> is set; otherwise returns a failure result saying it is off.</summary>
    public async Task<BuildResult> BuildAsync(
        Project project,
        string siteFolder,
        IProgress<BuildOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Build.BuildDockerImage)
        {
            return BuildResult.Failure(Localization.CoreMessages.Text("Build_DockerOff"));
        }

        if (!Directory.Exists(siteFolder) || !File.Exists(Path.Combine(siteFolder, "Dockerfile")))
        {
            return BuildResult.Failure(Localization.CoreMessages.Format("Build_NoDockerfile", siteFolder));
        }

        var docker = BuildTools.Resolve("docker", tools.DockerPath, locate, out var configuredFailure);
        if (configuredFailure is not null)
        {
            return configuredFailure;
        }

        if (string.IsNullOrEmpty(docker))
        {
            return BuildResult.Failure(Localization.CoreMessages.Text("Build_DockerMissing"));
        }

        var image = project.Build.EffectiveImageName(project.Name);
        return await ToolRun.RunAsync(runner, "docker build", docker, ["build", "-t", image, "."], siteFolder, progress, cancellationToken, tools.Timeout).ConfigureAwait(false);
    }
}
