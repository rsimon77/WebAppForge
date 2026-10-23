using System.Diagnostics;
using WebAppForge.Core.Build;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Tests;

public class BuildTests
{
    private static Project TinyProject()
    {
        var project = new Project
        {
            Name = "Tiny Shop",
            Database = new DatabaseSettings { Provider = "sqlite", Path = "data/tiny.db" },
        };
        project.Tables.Add(new TableInfo
        {
            Name = "items",
            Fields =
            {
                new FieldInfo { Name = "id", Type = FieldType.Integer, IsPrimaryKey = true, IsAutoIncrement = true, IsRequired = true },
                new FieldInfo { Name = "name", Type = FieldType.Text, IsRequired = true },
            },
        });
        return project;
    }

    private static string TempFolder()
    {
        return Path.Combine(Path.GetTempPath(), $"build-{Guid.NewGuid():N}");
    }

    private sealed class FakeRunner(Func<ProcessSpec, Action<OutputStream, string>, CancellationToken, Task<int>> run) : IProcessRunner
    {
        public ProcessSpec? Last { get; private set; }

        public Task<int> RunAsync(ProcessSpec spec, Action<OutputStream, string> onLine, CancellationToken cancellationToken)
        {
            Last = spec;
            return run(spec, onLine, cancellationToken);
        }
    }

    // ---- model ----

    [Fact]
    public void BuildSettingsHaveDocumentedDefaults()
    {
        var build = new BuildSettings();

        Assert.False(build.BuildAfterGenerate);
        Assert.Equal("Debug", build.Configuration);
        Assert.False(build.Publish);
        Assert.Equal("publish", build.PublishFolder);
        Assert.Equal(string.Empty, build.RuntimeIdentifier);
        Assert.False(build.SelfContained);
        Assert.False(build.GenerateDockerfile);
        Assert.Equal(8080, build.DockerExposePort);
        Assert.Equal("mcr.microsoft.com/dotnet/sdk:10.0", build.DockerBaseSdkImage);
        Assert.Equal("mcr.microsoft.com/dotnet/aspnet:10.0", build.DockerBaseRuntimeImage);
        Assert.False(build.GenerateCompose);
        Assert.False(build.BuildDockerImage);
        Assert.Equal("my-shop", build.EffectiveImageName("My Shop"));
        Assert.Equal("custom/img:1", new BuildSettings { DockerImageName = "custom/img:1" }.EffectiveImageName("x"));
        Assert.Equal("shop", new Project { Name = "Shop" }.Build.EffectiveImageName("Shop"));
    }

    [Fact]
    public void BuildSettingsRoundTripThroughProjectFile()
    {
        var project = TinyProject();
        project.Build = new BuildSettings
        {
            BuildAfterGenerate = true,
            Configuration = "Release",
            Publish = true,
            PublishFolder = "out/pub",
            RuntimeIdentifier = "linux-x64",
            SelfContained = true,
            GenerateDockerfile = true,
            DockerImageName = "acme/tiny",
            DockerExposePort = 9090,
            DockerBaseSdkImage = "example/sdk:1",
            DockerBaseRuntimeImage = "example/run:1",
            GenerateCompose = true,
            BuildDockerImage = true,
        };

        var xml = ProjectFile.Write(project);
        Assert.NotNull(xml.Root!.Element("build"));
        var loaded = ProjectFile.Read(xml).Build;

        Assert.True(loaded.BuildAfterGenerate);
        Assert.Equal("Release", loaded.Configuration);
        Assert.True(loaded.Publish);
        Assert.Equal("out/pub", loaded.PublishFolder);
        Assert.Equal("linux-x64", loaded.RuntimeIdentifier);
        Assert.True(loaded.SelfContained);
        Assert.True(loaded.GenerateDockerfile);
        Assert.Equal("acme/tiny", loaded.DockerImageName);
        Assert.Equal(9090, loaded.DockerExposePort);
        Assert.Equal("example/sdk:1", loaded.DockerBaseSdkImage);
        Assert.Equal("example/run:1", loaded.DockerBaseRuntimeImage);
        Assert.True(loaded.GenerateCompose);
        Assert.True(loaded.BuildDockerImage);
    }

    [Fact]
    public void ProjectFileWithoutBuildElementLoadsWithDefaults()
    {
        var xml = ProjectFile.Write(TinyProject());
        xml.Root!.Element("build")!.Remove();

        var loaded = ProjectFile.Read(xml);

        Assert.False(loaded.Build.BuildAfterGenerate);
        Assert.Equal("Debug", loaded.Build.Configuration);
        Assert.Equal(8080, loaded.Build.DockerExposePort);
        Assert.Equal(BuildSettings.DefaultSdkImage, loaded.Build.DockerBaseSdkImage);
    }

    // ---- docker files ----

    [Fact]
    public void DockerFilesContainImageNamesPortAndVolume()
    {
        var project = TinyProject();
        project.Build.GenerateDockerfile = true;
        project.Build.GenerateCompose = true;
        project.Build.DockerImageName = "acme/tiny";
        project.Build.DockerExposePort = 9090;
        project.Build.DockerBaseSdkImage = "example/sdk:1";
        project.Build.DockerBaseRuntimeImage = "example/run:1";
        var folder = TempFolder();
        try
        {
            var written = new DockerGenerator().Generate(project, folder);

            Assert.Equal(["Dockerfile", ".dockerignore", "docker-compose.yml"], written);
            var dockerfile = File.ReadAllText(Path.Combine(folder, "Dockerfile"));
            Assert.Contains("FROM example/sdk:1 AS build", dockerfile);
            Assert.Contains("FROM example/run:1 AS final", dockerfile);
            Assert.Contains($"dotnet restore \"{Identifier.Pascal(project.Name)}.csproj\"", dockerfile);
            Assert.Contains("dotnet publish", dockerfile);
            Assert.Contains("USER ${APP_UID}", dockerfile);
            Assert.Contains("EXPOSE 9090", dockerfile);
            Assert.Contains("ASPNETCORE_URLS=http://+:9090", dockerfile);
            Assert.Contains("ConnectionStrings__Default=\"Data Source=/data/tiny.db\"", dockerfile);

            var compose = File.ReadAllText(Path.Combine(folder, "docker-compose.yml"));
            Assert.Contains("  tiny-shop:", compose);
            Assert.Contains("image: acme/tiny", compose);
            Assert.Contains("\"9090:9090\"", compose);
            Assert.Contains("ConnectionStrings__Default: \"Data Source=/data/tiny.db\"", compose);
            Assert.Contains("- tiny-shop-data:/data", compose);
            Assert.Contains("volumes:\n  tiny-shop-data:", compose);

            Assert.Contains("**/bin/", File.ReadAllText(Path.Combine(folder, ".dockerignore")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ComposeIsSkippedUnlessRequested()
    {
        var folder = TempFolder();
        try
        {
            var written = new DockerGenerator().Generate(TinyProject(), folder);

            Assert.DoesNotContain("docker-compose.yml", written);
            Assert.False(File.Exists(Path.Combine(folder, "docker-compose.yml")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- diagnostics ----

    [Fact]
    public void ParsesMsBuildDiagnosticsAndCounts()
    {
        string[] lines =
        [
            @"C:\site\Program.cs(12,5): error CS1002: ; expected [C:\site\Site.csproj]",
            @"C:\site\Models\A.cs(3,9): warning CS0168: The variable 'x' is declared but never used [C:\site\Site.csproj]",
            "Build FAILED.",
            @"C:\site\Program.cs(12,5): error CS1002: ; expected [C:\site\Site.csproj]",
            "    1 Warning(s)",
            "    1 Error(s)",
            "Time Elapsed 00:00:01.23",
        ];

        var parsed = MsBuildOutput.Parse(lines);

        Assert.Equal(1, parsed.Errors);
        Assert.Equal(1, parsed.Warnings);
        Assert.False(parsed.BuildSucceeded);
        Assert.Equal(2, parsed.Diagnostics.Count);
        var error = parsed.Diagnostics[0];
        Assert.Equal(@"C:\site\Program.cs", error.File);
        Assert.Equal(12, error.Line);
        Assert.Equal("CS1002", error.Code);
        Assert.Equal("; expected", error.Message);
        Assert.True(error.IsError);
        Assert.False(parsed.Diagnostics[1].IsError);
    }

    [Fact]
    public void ParsesSuccessAndDiagnosticsWithoutLine()
    {
        var parsed = MsBuildOutput.Parse(["Build succeeded.", "    0 Warning(s)", "    0 Error(s)"]);
        Assert.True(parsed.BuildSucceeded);
        Assert.Equal(0, parsed.Errors);

        var diagnostic = MsBuildOutput.ParseDiagnostic("/src/Site.csproj : error NU1101: Unable to find package Foo.");
        Assert.NotNull(diagnostic);
        Assert.Equal("/src/Site.csproj", diagnostic.File);
        Assert.Equal(0, diagnostic.Line);
        Assert.Equal("NU1101", diagnostic.Code);
        Assert.Null(MsBuildOutput.ParseDiagnostic("  Restore complete (0.5s)"));
    }

    // ---- builder with fakes ----

    [Fact]
    public async Task MissingDotnetGivesClearResult()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        try
        {
            var builder = new SiteBuilder(new FakeRunner((_, _, _) => throw new InvalidOperationException("must not run")), _ => null);

            var result = await builder.BuildAsync(folder, "Debug");

            Assert.False(result.Succeeded);
            Assert.Contains("dotnet CLI was not found", result.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task MissingDockerGivesClearResult()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Dockerfile"), "FROM scratch");
        try
        {
            var project = TinyProject();
            project.Build.BuildDockerImage = true;
            var builder = new DockerBuilder(new FakeRunner((_, _, _) => throw new InvalidOperationException("must not run")), _ => null);

            var result = await builder.BuildAsync(project, folder);

            Assert.False(result.Succeeded);
            Assert.Contains("docker CLI was not found", result.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task BuildPassesArgumentListAndReportsLines()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Site.csproj"), "<Project />");
        try
        {
            var runner = new FakeRunner((_, onLine, _) =>
            {
                onLine(OutputStream.StdOut, "Build succeeded.");
                onLine(OutputStream.StdOut, "    2 Warning(s)");
                onLine(OutputStream.StdErr, "oops");
                return Task.FromResult(0);
            });
            var lines = new List<BuildOutputLine>();

            var result = await new SiteBuilder(runner, name => "/bin/" + name).BuildAsync(folder, "Release", new SyncProgress<BuildOutputLine>(lines.Add));

            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Warnings);
            Assert.Equal("/bin/dotnet", runner.Last!.FileName);
            Assert.Equal("build", runner.Last.Arguments[0]);
            Assert.Contains("Release", runner.Last.Arguments);
            Assert.Equal(3, lines.Count);
            Assert.Contains(lines, l => l.Stream == OutputStream.StdErr && l.Text == "oops");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task FailedExitCodeGivesFailureResultWithDiagnostics()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Site.csproj"), "<Project />");
        try
        {
            var runner = new FakeRunner((_, onLine, _) =>
            {
                onLine(OutputStream.StdOut, "Program.cs(1,2): error CS1002: ; expected [Site.csproj]");
                return Task.FromResult(1);
            });

            var result = await new SiteBuilder(runner, name => name).BuildAsync(folder, "Debug");

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal(1, result.Errors);
            Assert.Single(result.ParsedDiagnostics);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- cancellation with a real process ----

    private static ProcessSpec LongRunningSpec(Action<int> onStarted)
    {
        return OperatingSystem.IsWindows()
            ? new ProcessSpec { FileName = "ping", Arguments = ["-n", "60", "127.0.0.1"], WorkingDirectory = Path.GetTempPath(), OnStarted = onStarted }
            : new ProcessSpec { FileName = "sleep", Arguments = ["60"], WorkingDirectory = Path.GetTempPath(), OnStarted = onStarted };
    }

    [Fact]
    public async Task CancellationKillsTheProcess()
    {
        var pid = 0;
        using var cts = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var stopwatch = Stopwatch.StartNew();

        var task = runner.RunAsync(LongRunningSpec(id => { pid = id; cts.CancelAfter(500); }), (_, _) => { }, cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, pid);
        var alive = true;
        try
        {
            using var process = Process.GetProcessById(pid);
            alive = !process.HasExited;
        }
        catch (ArgumentException)
        {
            alive = false;
        }

        Assert.False(alive);
    }

    [Fact]
    public async Task CancellingABuildReturnsCancelledResult()
    {
        var folder = TempFolder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Site.csproj"), "<Project />");
        try
        {
            using var cts = new CancellationTokenSource();
            var inner = new ProcessRunner();
            var runner = new FakeRunner((_, onLine, token) =>
                inner.RunAsync(LongRunningSpec(_ => cts.CancelAfter(300)), onLine, token));

            var result = await new SiteBuilder(runner, name => name).BuildAsync(folder, "Debug", cancellationToken: cts.Token);

            Assert.False(result.Succeeded);
            Assert.True(result.Cancelled);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- orchestrator ----

    [Fact]
    public async Task OrchestratorStopsAtFirstFailedStage()
    {
        var folder = TempFolder();
        try
        {
            var project = TinyProject();
            project.Build.BuildAfterGenerate = true;
            project.Build.Publish = true;
            project.Build.GenerateDockerfile = true;
            var runner = new FakeRunner((_, _, _) => Task.FromResult(1));
            var stages = new List<StageProgress>();
            var orchestrator = new GenerateAndBuild(new SiteBuilder(runner, name => name), new DockerBuilder(runner, name => name));

            var result = await orchestrator.RunAsync(project, folder, folder, new SyncProgress<StageProgress>(stages.Add));

            Assert.False(result.Succeeded);
            Assert.Equal(BuildStage.Build, result.FailedStage);
            Assert.True(File.Exists(Path.Combine(folder, "Dockerfile")));
            Assert.Contains(stages, s => s.Stage == BuildStage.Generate && s.Status == StageStatus.Succeeded);
            Assert.Contains(stages, s => s.Stage == BuildStage.DockerFiles && s.Status == StageStatus.Succeeded);
            Assert.Contains(stages, s => s.Stage == BuildStage.Build && s.Status == StageStatus.Failed);
            Assert.DoesNotContain(stages, s => s.Stage == BuildStage.Publish);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // ---- real dotnet ----

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RealDebugBuildOfGeneratedSiteSucceeds()
    {
        var folder = TempFolder();
        try
        {
            new ProjectGenerator().Generate(TinyProject(), folder, folder);
            var lines = new List<BuildOutputLine>();

            var result = await new SiteBuilder().BuildAsync(folder, "Debug", new SyncProgress<BuildOutputLine>(l => { lock (lines) { lines.Add(l); } }));

            Assert.True(result.Succeeded, result.Message + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(l => l.Text)));
            Assert.Equal(0, result.Errors);
            Assert.Equal(0, result.Warnings);
            Assert.NotEmpty(lines);
        }
        finally
        {
            TryDelete(folder);
        }
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task RealBuildWithBrokenProjectFileFails()
    {
        var folder = TempFolder();
        try
        {
            new ProjectGenerator().Generate(TinyProject(), folder, folder);
            File.WriteAllText(Directory.GetFiles(folder, "*.csproj")[0], "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup></Project>");

            var result = await new SiteBuilder().BuildAsync(folder, "Debug");

            Assert.False(result.Succeeded);
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(result.Errors >= 1);
            Assert.Contains(result.ParsedDiagnostics, d => d.IsError && d.Code.StartsWith("MSB", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(folder);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
