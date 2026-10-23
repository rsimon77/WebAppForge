using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Build;

public enum BuildStage
{
    Generate,
    DockerFiles,
    Build,
    Publish,
    DockerImage,
}

public enum StageStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled,
}

public sealed record StageProgress(BuildStage Stage, StageStatus Status, string Message = "");

public sealed class GenerateAndBuildResult
{
    public bool Succeeded { get; init; }

    /// <summary>The stage that failed or was cancelled, or null when everything succeeded.</summary>
    public BuildStage? FailedStage { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> GeneratedFiles { get; init; } = [];

    public IReadOnlyList<string> DockerFiles { get; init; } = [];

    public BuildResult? Build { get; init; }

    public BuildResult? Publish { get; init; }

    public BuildResult? DockerImage { get; init; }
}

/// <summary>
/// Generate, then optionally the Docker files, the build, the publish and the Docker image, as configured in
/// <see cref="Project.Build"/>. Stops at the first failed stage. <see cref="ProjectGenerator.Generate"/> itself stays light.
/// </summary>
public sealed class GenerateAndBuild
{
    private readonly SiteBuilder siteBuilder;
    private readonly DockerBuilder dockerBuilder;

    public GenerateAndBuild(BuildTools? tools = null)
        : this(new SiteBuilder(tools), new DockerBuilder(tools))
    {
    }

    public GenerateAndBuild(SiteBuilder siteBuilder, DockerBuilder dockerBuilder)
    {
        this.siteBuilder = siteBuilder;
        this.dockerBuilder = dockerBuilder;
    }

    public async Task<GenerateAndBuildResult> RunAsync(
        Project project,
        string outputDirectory,
        string projectFolder,
        IProgress<StageProgress>? stages = null,
        IProgress<BuildOutputLine>? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.Build;

        // Generate
        stages?.Report(new StageProgress(BuildStage.Generate, StageStatus.Running));
        IReadOnlyList<string> generated;
        try
        {
            generated = await Task.Run(() => new ProjectGenerator().Generate(project, outputDirectory, projectFolder), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Stop(BuildStage.Generate, StageStatus.Cancelled, "Cancelled.", stages);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Stop(BuildStage.Generate, StageStatus.Failed, ex.Message, stages);
        }

        stages?.Report(new StageProgress(BuildStage.Generate, StageStatus.Succeeded, $"{generated.Count} files"));

        // Docker files
        IReadOnlyList<string> dockerFiles = [];
        if (settings.GenerateDockerfile || settings.GenerateCompose || settings.BuildDockerImage)
        {
            stages?.Report(new StageProgress(BuildStage.DockerFiles, StageStatus.Running));
            try
            {
                dockerFiles = new DockerGenerator().Generate(project, outputDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Stop(BuildStage.DockerFiles, StageStatus.Failed, ex.Message, stages, generated);
            }

            stages?.Report(new StageProgress(BuildStage.DockerFiles, StageStatus.Succeeded, string.Join(", ", dockerFiles)));
        }
        else
        {
            stages?.Report(new StageProgress(BuildStage.DockerFiles, StageStatus.Skipped));
        }

        // Build
        BuildResult? build = null;
        if (settings.BuildAfterGenerate)
        {
            stages?.Report(new StageProgress(BuildStage.Build, StageStatus.Running));
            build = await siteBuilder.BuildAsync(outputDirectory, settings.Configuration, output, cancellationToken).ConfigureAwait(false);
            if (!build.Succeeded)
            {
                return Stop(BuildStage.Build, build.Cancelled ? StageStatus.Cancelled : StageStatus.Failed, Describe(build), stages, generated, dockerFiles, build);
            }

            stages?.Report(new StageProgress(BuildStage.Build, StageStatus.Succeeded, $"{build.Warnings} warning(s)"));
        }
        else
        {
            stages?.Report(new StageProgress(BuildStage.Build, StageStatus.Skipped));
        }

        // Publish
        BuildResult? publish = null;
        if (settings.Publish)
        {
            stages?.Report(new StageProgress(BuildStage.Publish, StageStatus.Running));
            publish = await siteBuilder.PublishAsync(outputDirectory, settings, output, cancellationToken).ConfigureAwait(false);
            if (!publish.Succeeded)
            {
                return Stop(BuildStage.Publish, publish.Cancelled ? StageStatus.Cancelled : StageStatus.Failed, Describe(publish), stages, generated, dockerFiles, build, publish);
            }

            stages?.Report(new StageProgress(BuildStage.Publish, StageStatus.Succeeded, publish.PublishPath ?? string.Empty));
        }
        else
        {
            stages?.Report(new StageProgress(BuildStage.Publish, StageStatus.Skipped));
        }

        // Docker image
        BuildResult? image = null;
        if (settings.BuildDockerImage)
        {
            stages?.Report(new StageProgress(BuildStage.DockerImage, StageStatus.Running));
            image = await dockerBuilder.BuildAsync(project, outputDirectory, output, cancellationToken).ConfigureAwait(false);
            if (!image.Succeeded)
            {
                return Stop(BuildStage.DockerImage, image.Cancelled ? StageStatus.Cancelled : StageStatus.Failed, Describe(image), stages, generated, dockerFiles, build, publish, image);
            }

            stages?.Report(new StageProgress(BuildStage.DockerImage, StageStatus.Succeeded, settings.EffectiveImageName(project.Name)));
        }
        else
        {
            stages?.Report(new StageProgress(BuildStage.DockerImage, StageStatus.Skipped));
        }

        return new GenerateAndBuildResult
        {
            Succeeded = true,
            GeneratedFiles = generated,
            DockerFiles = dockerFiles,
            Build = build,
            Publish = publish,
            DockerImage = image,
        };
    }

    private static string Describe(BuildResult result)
    {
        if (result.Errors > 0 && result.ParsedDiagnostics.FirstOrDefault(d => d.IsError) is { } first)
        {
            return $"{result.Errors} error(s). First: {first.File}({first.Line}): {first.Code}: {first.Message}";
        }

        return result.Message;
    }

    private static GenerateAndBuildResult Stop(
        BuildStage stage,
        StageStatus status,
        string message,
        IProgress<StageProgress>? stages,
        IReadOnlyList<string>? generated = null,
        IReadOnlyList<string>? dockerFiles = null,
        BuildResult? build = null,
        BuildResult? publish = null,
        BuildResult? image = null)
    {
        stages?.Report(new StageProgress(stage, status, message));
        return new GenerateAndBuildResult
        {
            Succeeded = false,
            FailedStage = stage,
            Message = message,
            GeneratedFiles = generated ?? [],
            DockerFiles = dockerFiles ?? [],
            Build = build,
            Publish = publish,
            DockerImage = image,
        };
    }
}
