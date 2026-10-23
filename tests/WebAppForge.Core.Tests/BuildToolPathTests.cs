using WebAppForge.Core.Build;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class BuildToolPathTests
{
    private sealed class Runner(Func<ProcessSpec, Action<OutputStream, string>, CancellationToken, Task<int>> run) : IProcessRunner
    {
        public ProcessSpec? Last { get; private set; }

        public Task<int> RunAsync(ProcessSpec spec, Action<OutputStream, string> onLine, CancellationToken cancellationToken)
        {
            Last = spec;
            return run(spec, onLine, cancellationToken);
        }
    }

    private static Runner Ok() => new((_, _, _) => Task.FromResult(0));

    private static Runner Hang() => new(async (_, _, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    });

    private static string Site()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Site.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(folder, "Dockerfile"), "FROM x");
        return folder;
    }

    private static string FakeExe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fake-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static Project DockerProject()
    {
        var project = new Project { Name = "Shop" };
        project.Build.BuildDockerImage = true;
        return project;
    }

    [Fact]
    public async Task ExplicitDotnetPathIsUsed()
    {
        var exe = FakeExe();
        var runner = Ok();
        var builder = new SiteBuilder(runner, _ => throw new InvalidOperationException("PATH must not be used"), new BuildTools(DotnetPath: exe));

        var result = await builder.BuildAsync(Site(), "Debug");

        Assert.True(result.Succeeded);
        Assert.Equal(exe, runner.Last!.FileName);
    }

    [Fact]
    public async Task MissingExplicitDotnetPathFailsClearly()
    {
        var runner = Ok();
        var missing = Path.Combine(Path.GetTempPath(), "nope", "dotnet.exe");
        var builder = new SiteBuilder(runner, _ => "path-dotnet", new BuildTools(DotnetPath: missing));

        var result = await builder.BuildAsync(Site(), "Debug");

        Assert.False(result.Succeeded);
        Assert.Equal($"The configured dotnet path was not found: {missing}", result.Message);
        Assert.Null(runner.Last);
    }

    [Fact]
    public async Task EmptyPathFallsBackToPath()
    {
        var runner = Ok();
        string? asked = null;
        var builder = new SiteBuilder(runner, name => { asked = name; return name + "-on-path"; }, new BuildTools(DotnetPath: "  "));

        var result = await builder.BuildAsync(Site(), "Debug");

        Assert.True(result.Succeeded);
        Assert.Equal("dotnet", asked);
        Assert.Equal("dotnet-on-path", runner.Last!.FileName);
    }

    [Fact]
    public async Task ExplicitDockerPathIsUsedAndMissingOneFails()
    {
        var exe = FakeExe();
        var runner = Ok();
        var ok = await new DockerBuilder(runner, _ => null, new BuildTools(DockerPath: exe)).BuildAsync(DockerProject(), Site());
        Assert.True(ok.Succeeded);
        Assert.Equal(exe, runner.Last!.FileName);
        Assert.Equal("build", runner.Last.Arguments[0]);

        var missing = Path.Combine(Path.GetTempPath(), "nope", "docker.exe");
        var bad = await new DockerBuilder(Ok(), _ => "docker", new BuildTools(DockerPath: missing)).BuildAsync(DockerProject(), Site());
        Assert.False(bad.Succeeded);
        Assert.Equal($"The configured docker path was not found: {missing}", bad.Message);
    }

    [Fact]
    public async Task ProxyVariablesAreSetOnlyWhenConfigured()
    {
        var without = Ok();
        await new SiteBuilder(without, _ => "dotnet", new BuildTools()).BuildAsync(Site(), "Debug");
        Assert.Empty(without.Last!.Environment);

        var with = Ok();
        await new SiteBuilder(with, _ => "dotnet", new BuildTools { HttpProxy = "http://proxy:8080" }).BuildAsync(Site(), "Debug");
        Assert.Equal("http://proxy:8080", with.Last!.Environment["HTTP_PROXY"]);
        Assert.Equal("http://proxy:8080", with.Last.Environment["HTTPS_PROXY"]);
    }

    [Fact]
    public async Task TimeoutCancelsAndReports()
    {
        var killed = false;
        var runner = new Runner(async (_, _, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                killed = true;
                throw;
            }

            return 0;
        });
        var builder = new SiteBuilder(runner, _ => "dotnet", new BuildTools { Timeout = TimeSpan.FromMilliseconds(100) });

        var result = await builder.BuildAsync(Site(), "Debug");

        Assert.True(killed);
        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.Contains("timed out after", result.Message);
    }

    [Fact]
    public async Task UserCancellationIsStillCancelledWhenTimeoutIsSet()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var builder = new SiteBuilder(Hang(), _ => "dotnet", new BuildTools { Timeout = TimeSpan.FromMinutes(5) });

        var result = await builder.BuildAsync(Site(), "Debug", null, cts.Token);

        Assert.True(result.Cancelled);
    }

    [Fact]
    public void DetectFindsNothingForUnknownTool()
    {
        Assert.Null(BuildTools.Detect("definitely-not-a-real-tool-xyz"));
    }

    [Fact]
    public async Task VersionProbeReturnsFirstLine()
    {
        var runner = new Runner((_, onLine, _) =>
        {
            onLine(OutputStream.StdOut, "10.0.100");
            onLine(OutputStream.StdOut, "second");
            return Task.FromResult(0);
        });

        var result = await BuildTools.ProbeVersionAsync(runner, "dotnet", TimeSpan.FromSeconds(5), default);

        Assert.True(result.Succeeded);
        Assert.Equal("10.0.100", result.Version);
        Assert.Equal(["--version"], runner.Last!.Arguments);
    }

    [Fact]
    public async Task VersionProbeReportsFailureAndStartErrors()
    {
        var failing = new Runner((_, onLine, _) =>
        {
            onLine(OutputStream.StdErr, "boom");
            return Task.FromResult(3);
        });
        var failed = await BuildTools.ProbeVersionAsync(failing, "x", TimeSpan.FromSeconds(5), default);
        Assert.False(failed.Succeeded);
        Assert.Equal("boom", failed.Error);

        var missing = new Runner((_, _, _) => throw new System.ComponentModel.Win32Exception("not found"));
        var start = await BuildTools.ProbeVersionAsync(missing, "x", TimeSpan.FromSeconds(5), default);
        Assert.False(start.Succeeded);
        Assert.Contains("Could not start", start.Error);
    }

    [Fact]
    public async Task VersionProbeTimesOut()
    {
        var result = await BuildTools.ProbeVersionAsync(Hang(), "x", TimeSpan.FromMilliseconds(100), default);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out after", result.Error);
    }
}
