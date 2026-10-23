namespace WebAppForge.Core.Model;

/// <summary>Build, publish and Docker options applied after a site is generated. Stored as the &lt;build&gt; element.</summary>
public sealed class BuildSettings
{
    public const string DefaultSdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    public const string DefaultRuntimeImage = "mcr.microsoft.com/dotnet/aspnet:10.0";

    /// <summary>Run dotnet build in the output folder after generating.</summary>
    public bool BuildAfterGenerate { get; set; }

    /// <summary>"Debug" or "Release".</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Run dotnet publish instead of build.</summary>
    public bool Publish { get; set; }

    /// <summary>Publish folder, relative to the output folder.</summary>
    public string PublishFolder { get; set; } = "publish";

    /// <summary>Optional runtime identifier such as win-x64 or linux-x64. Empty means framework dependent for any platform.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    public bool SelfContained { get; set; }

    public bool GenerateDockerfile { get; set; }

    /// <summary>Docker image name. Empty means the project name in lower case.</summary>
    public string DockerImageName { get; set; } = string.Empty;

    public int DockerExposePort { get; set; } = 8080;

    public string DockerBaseSdkImage { get; set; } = DefaultSdkImage;

    public string DockerBaseRuntimeImage { get; set; } = DefaultRuntimeImage;

    public bool GenerateCompose { get; set; }

    /// <summary>Run docker build after generating the Docker files.</summary>
    public bool BuildDockerImage { get; set; }

    /// <summary>The image name to use for a project: the setting, or the project name in lower case with unsafe characters replaced.</summary>
    public string EffectiveImageName(string projectName)
    {
        if (!string.IsNullOrWhiteSpace(DockerImageName))
        {
            return DockerImageName.Trim();
        }
        return SafeName(projectName);
    }

    /// <summary>Lower-case name made of letters, digits, '-', '_' and '.', usable as an image, service or volume name.</summary>
    public static string SafeName(string name)
    {
        var chars = (name ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        var safe = new string(chars).Trim('-', '_', '.');
        return safe.Length == 0 ? "site" : safe;
    }
}
