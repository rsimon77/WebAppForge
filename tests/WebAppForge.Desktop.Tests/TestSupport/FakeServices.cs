using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>A translation provider that never touches the network. It records what it was asked to translate.</summary>
public sealed class FakeTranslationProvider : ITranslationProvider
{
    public TranslationSource Source => TranslationSource.Google;

    /// <summary>Every request: target language and the texts.</summary>
    public List<(string From, string To, IReadOnlyList<string> Texts)> Calls { get; } = [];

    /// <summary>Target languages that fail with a <see cref="TranslationException"/>.</summary>
    public HashSet<string> FailLanguages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs at the start of each call, for example to cancel.</summary>
    public Action<string>? OnCall { get; set; }

    public int TextCount => Calls.Sum(c => c.Texts.Count);

    public Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts, string fromLanguage, string toLanguage, string? notes, CancellationToken cancellationToken)
    {
        Calls.Add((fromLanguage, toLanguage, texts));
        OnCall?.Invoke(toLanguage);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailLanguages.Contains(toLanguage))
        {
            throw new TranslationException($"Fake service refused {toLanguage}: quota exceeded.");
        }

        return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => $"[{toLanguage}] {t}").ToList());
    }
}

public sealed class FakeApiKeys : IApiKeyProvider
{
    public const string SecretValue = "SECRET-KEY-VALUE-123";

    public HashSet<TranslationProviderKind> Set { get; } = [TranslationProviderKind.Google, TranslationProviderKind.Claude];

    public string? GetApiKey(TranslationProviderKind provider) => Set.Contains(provider) ? SecretValue : null;
}

public sealed class FakeProviderFactory(FakeTranslationProvider provider) : ITranslationProviderFactory
{
    public List<TranslationProviderKind> Created { get; } = [];

    public ITranslationProvider? Create(TranslationProviderKind kind)
    {
        Created.Add(kind);
        return kind == TranslationProviderKind.None ? null : provider;
    }
}

/// <summary>A build runner that runs a script instead of dotnet or docker.</summary>
public sealed class FakeBuildRunner : IBuildRunner
{
    public int RunCount { get; private set; }

    public List<string> Calls { get; } = [];

    /// <summary>Full pipeline script. Default: every stage succeeds.</summary>
    public Func<Project, string, IProgress<StageProgress>, IProgress<BuildOutputLine>, CancellationToken, Task<GenerateAndBuildResult>>? RunScript { get; set; }

    public Func<IProgress<BuildOutputLine>, CancellationToken, Task<BuildResult>>? BuildScript { get; set; }

    public Func<IProgress<BuildOutputLine>, CancellationToken, Task<BuildResult>>? PublishScript { get; set; }

    public string? LastSiteFolder { get; private set; }

    /// <summary>The BuildTools each call received, in order.</summary>
    public List<BuildTools> ToolsSeen { get; } = [];

    public Task<GenerateAndBuildResult> RunAsync(
        Project project, string outputDirectory, string projectFolder,
        BuildTools tools, IProgress<StageProgress> stages, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
    {
        ToolsSeen.Add(tools);
        RunCount++;
        Calls.Add("run");
        LastSiteFolder = outputDirectory;
        return RunScript is not null
            ? RunScript(project, outputDirectory, stages, output, cancellationToken)
            : Task.FromResult(new GenerateAndBuildResult { Succeeded = true, GeneratedFiles = ["a.cs", "b.cs"] });
    }

    public Task<BuildResult> BuildAsync(string siteFolder, string configuration, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
    {
        ToolsSeen.Add(tools);
        Calls.Add("build:" + configuration);
        LastSiteFolder = siteFolder;
        return BuildScript is not null ? BuildScript(output, cancellationToken) : Task.FromResult(new BuildResult { Succeeded = true });
    }

    public Task<BuildResult> PublishAsync(string siteFolder, BuildSettings settings, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
    {
        ToolsSeen.Add(tools);
        Calls.Add("publish:" + settings.PublishFolder);
        LastSiteFolder = siteFolder;
        return PublishScript is not null
            ? PublishScript(output, cancellationToken)
            : Task.FromResult(new BuildResult { Succeeded = true, PublishPath = System.IO.Path.Combine(siteFolder, settings.PublishFolder) });
    }

    public static BuildOutputLine Line(string text, OutputStream stream = OutputStream.StdOut) => new(stream, text, DateTimeOffset.Now);
}

public sealed class FakeSiteHandle(Action<int> onExited) : ISiteHandle
{
    public int ProcessId => 4242;

    public int StopCount { get; private set; }

    public void Stop()
    {
        StopCount++;
        onExited(-1);
    }
}

/// <summary>Starts no process. A test calls <see cref="EmitLine"/> and <see cref="Exit"/> to play the site.</summary>
public sealed class FakeSiteRunner : ISiteRunner
{
    private Action<OutputStream, string>? _onLine;
    private Action<int>? _onExited;

    public List<SiteLaunch> Launches { get; } = [];

    public FakeSiteHandle? Handle { get; private set; }

    public Exception? FailToStart { get; set; }

    public ISiteHandle Start(SiteLaunch launch, Action<OutputStream, string> onLine, Action<int> onExited)
    {
        if (FailToStart is not null)
        {
            throw FailToStart;
        }

        Launches.Add(launch);
        _onLine = onLine;
        _onExited = onExited;
        Handle = new FakeSiteHandle(onExited);
        return Handle;
    }

    public void EmitLine(string text, OutputStream stream = OutputStream.StdOut) => _onLine?.Invoke(stream, text);

    public void Exit(int code) => _onExited?.Invoke(code);
}

public sealed class FakePortFinder(int port) : IPortFinder
{
    public int FindFreePort() => port;
}
