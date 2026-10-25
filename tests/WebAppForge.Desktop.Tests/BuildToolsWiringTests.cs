using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>The Options values reach the Core BuildTools of every build, and a missing configured path is shown in the console.</summary>
public class BuildToolsWiringTests
{
    [Fact]
    public void BuildToolsCarryThePathsProxyAndTimeoutOfTheSettings()
    {
        var tools = BuildToolsFactory.From(new AppSettings
        {
            DotnetPath = @"C:\d\dotnet.exe",
            NodePath = @"C:\n\node.exe",
            DockerPath = @"C:\k\docker.exe",
            HttpProxyUrl = "http://proxy:8080",
            RequestTimeoutSeconds = 90,
        });

        Assert.Equal(@"C:\d\dotnet.exe", tools.DotnetPath);
        Assert.Equal(@"C:\n\node.exe", tools.NodePath);
        Assert.Equal(@"C:\k\docker.exe", tools.DockerPath);
        Assert.Equal("http://proxy:8080", tools.HttpProxy);
        Assert.Equal(TimeSpan.FromSeconds(90), tools.Timeout);
    }

    [Fact]
    public void ATimeoutOfZeroMeansNoLimitAndEmptyPathsStayEmpty()
    {
        var tools = BuildToolsFactory.From(new AppSettings { RequestTimeoutSeconds = 0 }.Normalized());

        Assert.Null(tools.Timeout);
        Assert.Null(tools.DotnetPath);
        Assert.Null(tools.DockerPath);
        Assert.Null(tools.NodePath);
        Assert.Null(tools.HttpProxy);
    }

    [Fact]
    public async Task EveryBuildStepGetsTheCurrentToolsAndOptionsChangesApplyWithoutRestart()
    {
        using var folder = new TempFolder();
        var rig = await BuildConsoleTests.SetupAsync(folder);
        rig.CreateSite(built: true);
        rig.Main.ApplySettings(new AppSettings { DotnetPath = rig.Main.Settings.DotnetPath, RequestTimeoutSeconds = 45, HttpProxyUrl = "http://one:1" }, "ok");

        await rig.Generate.GenerateCommand.ExecuteAsync(null);
        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);
        await rig.Generate.PublishOnlyCommand.ExecuteAsync(null);

        Assert.Equal(3, rig.Runner.ToolsSeen.Count);
        Assert.All(rig.Runner.ToolsSeen, t =>
        {
            Assert.Equal(TimeSpan.FromSeconds(45), t.Timeout);
            Assert.Equal("http://one:1", t.HttpProxy);
        });

        // The Options dialog applies new values; the next build uses them.
        var dockerPath = folder.File("docker.exe");
        rig.Main.ApplySettings(new AppSettings
        {
            DotnetPath = rig.Main.Settings.DotnetPath,
            DockerPath = dockerPath,
            RequestTimeoutSeconds = 0,
            HttpProxyUrl = "http://two:2",
        }, "ok");
        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);

        var last = rig.Runner.ToolsSeen.Last();
        Assert.Null(last.Timeout);
        Assert.Equal("http://two:2", last.HttpProxy);
        Assert.Equal(dockerPath, last.DockerPath);
    }

    [Fact]
    public async Task ApplyingFromTheOptionsDialogUpdatesTheToolsOfTheNextBuild()
    {
        using var folder = new TempFolder();
        var rig = await BuildConsoleTests.SetupAsync(folder);
        rig.CreateSite(built: true);
        var options = new OptionsViewModel(rig.Main.Settings, next => rig.Main.ApplySettings(next, "applied"), rig.Dialogs, new OptionsEnvironment());
        options.NodePath = @"C:\tools\node.exe";
        options.RequestTimeoutText = "120";
        Assert.True(options.Apply());

        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);

        var tools = Assert.Single(rig.Runner.ToolsSeen);
        Assert.Equal(@"C:\tools\node.exe", tools.NodePath);
        Assert.Equal(TimeSpan.FromSeconds(120), tools.Timeout);
    }

    [Fact]
    public async Task AMissingConfiguredPathIsShownInTheConsoleWithAnOpenOptionsHint()
    {
        using var folder = new TempFolder();
        var message = @"The configured dotnet path was not found: C:\gone\dotnet.exe";
        var runner = new FakeBuildRunner { BuildScript = (_, _) => Task.FromResult(BuildResult.MissingToolPath(message)) };
        var rig = await BuildConsoleTests.SetupAsync(folder, runner: runner);
        rig.CreateSite(built: true);
        Assert.False(rig.Generate.ShowOptionsHint);

        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);

        Assert.Contains(message, rig.Generate.LastResult);
        Assert.Contains(rig.Generate.OutputLines, l => l.Text == message && l.Kind == OutputKind.Error);
        Assert.True(rig.Generate.ShowOptionsHint);

        // A later successful run clears the hint.
        runner.BuildScript = null;
        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);
        Assert.False(rig.Generate.ShowOptionsHint);
    }

    [Fact]
    public async Task AMissingPathInTheGenerateRunAlsoShowsTheHintAndOpenOptionsOpensTheToolsPage()
    {
        using var folder = new TempFolder();
        var message = "The configured docker path was not found: C:\\gone\\docker.exe";
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, _, _, _) => Task.FromResult(new GenerateAndBuildResult
            {
                Succeeded = false,
                FailedStage = BuildStage.DockerImage,
                Message = message,
                DockerImage = BuildResult.MissingToolPath(message),
            }),
        };
        var rig = await BuildConsoleTests.SetupAsync(folder, runner: runner);

        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Contains(message, rig.Generate.LastResult);
        Assert.True(rig.Generate.ShowOptionsHint);

        await rig.Generate.OpenOptionsCommand.ExecuteAsync(null);

        var shown = Assert.IsType<OptionsViewModel>(rig.Dialogs.ShownOptions);
        Assert.Equal(OptionsCategory.Tools, shown.SelectedCategory.Id);
        Assert.True(shown.IsToolsPage);
    }

    [Fact]
    public async Task RunSiteUsesTheConfiguredDotnetAndRefusesAMissingOneWithTheHint()
    {
        using var folder = new TempFolder();
        var rig = await BuildConsoleTests.SetupAsync(folder);
        rig.CreateSite(built: true);
        var custom = folder.File("custom-dotnet.exe");
        File.WriteAllText(custom, string.Empty);
        rig.Main.Settings.DotnetPath = custom;

        rig.Generate.RunSiteCommand.Execute(null);

        Assert.Equal(custom, Assert.Single(rig.Site.Launches).FileName);

        rig.Generate.StopSite();
        rig.Main.Settings.DotnetPath = folder.File("nope.exe");
        rig.Generate.RunSiteCommand.Execute(null);

        Assert.Contains("dotnet CLI was not found", rig.Generate.Message);
        Assert.True(rig.Generate.ShowOptionsHint);
        Assert.Single(rig.Site.Launches);
    }
}
