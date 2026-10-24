using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

/// <summary>The Generate tab build console: stage strip, output, diagnostics, cancel, build only, publish only and Run site, all with fakes.</summary>
public class BuildConsoleTests
{
    private const string ErrorLine = @"C:\site\Pages\Home.cs(10,5): error CS1002: ; expected [C:\site\Site.csproj]";
    private const string WarningLine = @"C:\site\Models\Order.cs(3,9): warning CS8618: Non-nullable property 'Id' must contain a non-null value [C:\site\Site.csproj]";

    public sealed class Rig
    {
        public required MainViewModel Main { get; init; }

        public required FakeBuildRunner Runner { get; init; }

        public required FakeSiteRunner Site { get; init; }

        public required FakeDialogService Dialogs { get; init; }

        public required TempFolder Folder { get; init; }

        public required List<string> OpenedUrls { get; init; }

        public required List<string> OpenedFolders { get; init; }

        public GenerateViewModel Generate => Main.Generate;

        public string Output => Path.Combine(Folder.Path, "generated");

        /// <summary>Creates a generated site folder (a csproj), and optionally a bin folder as if it had been built.</summary>
        public void CreateSite(bool built)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "Site.csproj"), "<Project />");
            if (built)
            {
                Directory.CreateDirectory(Path.Combine(Output, "bin"));
            }
        }
    }

    public static async Task<Rig> SetupAsync(TempFolder folder, bool saved = true, FakeBuildRunner? runner = null)
    {
        runner ??= new FakeBuildRunner();
        var site = new FakeSiteRunner();
        var dialogs = new FakeDialogService { NextSaveProject = folder.File("project.xml") };
        var opened = new List<string>();
        var folders = new List<string>();
        var main = new MainViewModel(dialogs)
        {
            Tools = new ToolServices
            {
                BuildRunner = runner,
                SiteRunner = site,
                PortFinder = new FakePortFinder(5123),
                OpenUrl = opened.Add,
            },
            OptionsEnvironment = new OptionsEnvironment { OpenFolder = folders.Add },
        };
        if (saved)
        {
            await main.SaveAsCommand.ExecuteAsync(null);
        }

        // A file that exists stands in for the dotnet executable, so no test depends on the machine's PATH.
        var dotnet = folder.File("fake-dotnet.exe");
        File.WriteAllText(dotnet, string.Empty);
        main.Settings.DotnetPath = dotnet;
        return new Rig { Main = main, Runner = runner, Site = site, Dialogs = dialogs, Folder = folder, OpenedUrls = opened, OpenedFolders = folders };
    }
    private static string States(GenerateViewModel generate) => string.Join(",", generate.Stages.Select(s => s.State));

    private static Task<GenerateAndBuildResult> Succeed(IProgress<StageProgress> stages, params BuildStage[] ran)
    {
        foreach (var stage in new[] { BuildStage.Generate, BuildStage.DockerFiles, BuildStage.Build, BuildStage.Publish, BuildStage.DockerImage })
        {
            if (ran.Contains(stage))
            {
                stages.Report(new StageProgress(stage, StageStatus.Running));
                stages.Report(new StageProgress(stage, StageStatus.Succeeded, "done"));
            }
            else
            {
                stages.Report(new StageProgress(stage, StageStatus.Skipped));
            }
        }

        return Task.FromResult(new GenerateAndBuildResult { Succeeded = true, GeneratedFiles = ["a", "b", "c"] });
    }

    // ---- Stage strip ----

    [Fact]
    public async Task StageStripFollowsTheProgressOfARunThatSucceeds()
    {
        using var folder = new TempFolder();
        var snapshots = new List<string>();
        GenerateViewModel? generate = null;
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, stages, output, _) =>
            {
                snapshots.Add("start:" + States(generate!));
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Running));
                snapshots.Add("generating:" + States(generate!));
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Succeeded, "3 files"));
                stages.Report(new StageProgress(BuildStage.DockerFiles, StageStatus.Skipped));
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Running));
                output.Report(FakeBuildRunner.Line("Restore complete"));
                snapshots.Add("building:" + States(generate!) + ":busy=" + generate!.IsBusy);
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Succeeded, "0 warning(s)"));
                stages.Report(new StageProgress(BuildStage.Publish, StageStatus.Skipped));
                stages.Report(new StageProgress(BuildStage.DockerImage, StageStatus.Skipped));
                return Task.FromResult(new GenerateAndBuildResult
                {
                    Succeeded = true,
                    GeneratedFiles = ["a", "b", "c"],
                    Build = new BuildResult { Succeeded = true, Duration = TimeSpan.FromSeconds(2), Warnings = 0 },
                });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);
        generate = rig.Generate;
        rig.Main.Current.Build.BuildAfterGenerate = true;

        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Equal("start:Pending,Pending,Pending,Pending,Pending", snapshots[0]);
        Assert.Equal("generating:Running,Pending,Pending,Pending,Pending", snapshots[1]);
        Assert.Equal("building:Ok,Skipped,Running,Pending,Pending:busy=True", snapshots[2]);
        Assert.Equal("Ok,Skipped,Ok,Skipped,Skipped", States(rig.Generate));
        Assert.Equal("3 files", rig.Generate.Stages[0].Message);
        Assert.True(rig.Generate.Stages[0].IsOk);
        Assert.True(rig.Generate.Stages[1].IsSkipped);
        Assert.False(rig.Generate.IsBusy);
        Assert.True(rig.Generate.IsIdle);
        Assert.Equal($"Generated 3 file(s) into {rig.Output}. Built with 0 warning(s) in 2 s.", rig.Main.Status);
        Assert.Contains("Restore complete", rig.Generate.OutputLines.Select(l => l.Text));
        Assert.Equal(["Generate", "Docker files", "Build", "Publish", "Docker image"], rig.Generate.Stages.Select(s => s.Title));
    }

    [Fact]
    public async Task AFailedStageStopsTheStripAndLeavesTheLaterStagesPending()
    {
        using var folder = new TempFolder();
        var diagnostic = MsBuildOutput.ParseDiagnostic(ErrorLine)!;
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, stages, output, _) =>
            {
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Running));
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Succeeded));
                stages.Report(new StageProgress(BuildStage.DockerFiles, StageStatus.Skipped));
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Running));
                output.Report(FakeBuildRunner.Line(ErrorLine));
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Failed, "1 error(s)"));
                return Task.FromResult(new GenerateAndBuildResult
                {
                    Succeeded = false,
                    FailedStage = BuildStage.Build,
                    Message = "1 error(s). First: CS1002",
                    Build = new BuildResult { Succeeded = false, Errors = 1, ParsedDiagnostics = [diagnostic] },
                });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);

        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.Equal("Ok,Skipped,Failed,Pending,Pending", States(rig.Generate));
        Assert.True(rig.Generate.Stages[2].IsFailed);
        Assert.Equal("Build failed: 1 error(s). First: CS1002", rig.Main.Status);
        Assert.Single(rig.Generate.Diagnostics);
        Assert.False(rig.Generate.IsBusy);
    }

    [Fact]
    public async Task CancelStopsTheRunThroughTheCancellationToken()
    {
        using var folder = new TempFolder();
        var started = new TaskCompletionSource();
        var runner = new FakeBuildRunner
        {
            RunScript = async (_, _, stages, _, token) =>
            {
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Running));
                stages.Report(new StageProgress(BuildStage.Generate, StageStatus.Succeeded));
                stages.Report(new StageProgress(BuildStage.Build, StageStatus.Running));
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    stages.Report(new StageProgress(BuildStage.Build, StageStatus.Cancelled, "Cancelled."));
                }

                return new GenerateAndBuildResult { Succeeded = false, FailedStage = BuildStage.Build, Message = "Cancelled." };
            },
        };
        var rig = await SetupAsync(folder, runner: runner);

        var run = rig.Generate.GenerateCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(rig.Generate.IsBusy);
        Assert.False(rig.Generate.IsIdle);

        // A second Generate while one runs does nothing.
        await rig.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(1, runner.RunCount);

        rig.Generate.CancelCommand.Execute(null);
        await run;

        Assert.False(rig.Generate.IsBusy);
        Assert.Equal("Ok,Pending,Cancelled,Pending,Pending", States(rig.Generate));
        Assert.Equal("Cancelled during Build.", rig.Main.Status);
    }

    // ---- Blocking ----

    [Fact]
    public async Task GenerateIsBlockedWithAMessageWhenTheProjectIsNotSavedOrInvalid()
    {
        using var folder = new TempFolder();
        var unsaved = await SetupAsync(folder, saved: false);
        await unsaved.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(0, unsaved.Runner.RunCount);
        Assert.StartsWith("Save the project first", unsaved.Generate.Message);
        Assert.True(unsaved.Generate.HasMessage);

        using var folder2 = new TempFolder();
        var invalid = await SetupAsync(folder2);
        invalid.Main.Security.Enabled = true;
        await invalid.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(0, invalid.Runner.RunCount);
        Assert.StartsWith("Generation blocked:", invalid.Main.Status);
        Assert.Equal(invalid.Main.Status, invalid.Generate.Message);
        Assert.False(invalid.Main.Security.CanGenerate);
        Assert.True(invalid.Main.Security.HasValidationMessage);

        using var folder3 = new TempFolder();
        var provider = await SetupAsync(folder3);
        provider.Main.Current.Database.Provider = "postgres";
        await provider.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(0, provider.Runner.RunCount);
        Assert.Equal("Not supported: Provider 'postgres' is not supported. Use one of: sqlite, sqlserver, azuresql, mysql.", provider.Main.Status);
    }

    // ---- Output and diagnostics ----

    [Fact]
    public async Task OutputLinesAreColouredByKindAndDiagnosticsAreParsedOnceEach()
    {
        using var folder = new TempFolder();
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, stages, output, _) =>
            {
                output.Report(FakeBuildRunner.Line("Determining projects to restore..."));
                output.Report(FakeBuildRunner.Line(WarningLine));
                output.Report(FakeBuildRunner.Line(ErrorLine));
                output.Report(FakeBuildRunner.Line("Something went wrong", OutputStream.StdErr));
                output.Report(FakeBuildRunner.Line("Build FAILED."));
                output.Report(FakeBuildRunner.Line(ErrorLine));
                output.Report(FakeBuildRunner.Line(@"C:\site\Models\Order.cs: error CS0103: no line number [C:\site\Site.csproj]"));
                return Task.FromResult(new GenerateAndBuildResult
                {
                    Succeeded = false,
                    FailedStage = BuildStage.Build,
                    Message = "failed",
                    Build = new BuildResult
                    {
                        Errors = 1,
                        ParsedDiagnostics = [MsBuildOutput.ParseDiagnostic(ErrorLine)!, MsBuildOutput.ParseDiagnostic(WarningLine)!],
                    },
                });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);

        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        var kinds = rig.Generate.OutputLines.GroupBy(l => l.Text).ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);
        Assert.Equal(OutputKind.Normal, kinds["Determining projects to restore..."]);
        Assert.Equal(OutputKind.Warning, kinds[WarningLine]);
        Assert.Equal(OutputKind.Error, kinds[ErrorLine]);
        Assert.Equal(OutputKind.Error, kinds["Something went wrong"]);
        Assert.Equal(OutputKind.Error, kinds["Build FAILED."]);
        Assert.True(rig.Generate.OutputLines.First(l => l.Text == WarningLine).IsWarning);
        Assert.True(rig.Generate.OutputLines.First(l => l.Text == ErrorLine).IsError);

        Assert.Equal(3, rig.Generate.Diagnostics.Count);
        var warning = rig.Generate.Diagnostics.Single(d => d.Code == "CS8618");
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal(@"C:\site\Models\Order.cs", warning.File);
        Assert.Equal(3, warning.Line);
        Assert.StartsWith("Non-nullable property", warning.Message);
        var error = rig.Generate.Diagnostics.Single(d => d.Code == "CS1002");
        Assert.Equal("Error", error.Severity);
        Assert.True(error.IsError);
        Assert.Equal(10, error.Line);
        Assert.Equal(@"C:\site\Pages\Home.cs:10", error.Location);
        var noLine = rig.Generate.Diagnostics.Single(d => d.Code == "CS0103");
        Assert.Equal(0, noLine.Line);
        Assert.Equal(@"C:\site\Models\Order.cs", noLine.Location);
    }

    [Fact]
    public async Task CopyingADiagnosticPutsPathAndLineOnTheClipboardAndCopyAllAndClearWork()
    {
        using var folder = new TempFolder();
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, _, output, _) =>
            {
                output.Report(FakeBuildRunner.Line("line one"));
                output.Report(FakeBuildRunner.Line(ErrorLine));
                return Task.FromResult(new GenerateAndBuildResult { Succeeded = true });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);
        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        await rig.Generate.CopyDiagnosticCommand.ExecuteAsync(rig.Generate.Diagnostics[0]);
        Assert.Equal(@"C:\site\Pages\Home.cs:10", rig.Dialogs.ClipboardTexts[^1]);

        await rig.Generate.CopyAllCommand.ExecuteAsync(null);
        var all = rig.Dialogs.ClipboardTexts[^1];
        Assert.Contains("line one", all);
        Assert.Contains(ErrorLine, all);

        rig.Generate.ClearOutputCommand.Execute(null);
        Assert.Empty(rig.Generate.OutputLines);
        Assert.Empty(rig.Generate.Diagnostics);
    }

    [Fact]
    public async Task TheConsoleKeepsAtMostTheLastFewThousandLines()
    {
        using var folder = new TempFolder();
        var runner = new FakeBuildRunner
        {
            RunScript = (_, _, _, output, _) =>
            {
                for (var i = 0; i < GenerateViewModel.MaxOutputLines + 700; i++)
                {
                    output.Report(FakeBuildRunner.Line("line " + i));
                }

                return Task.FromResult(new GenerateAndBuildResult { Succeeded = true });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);

        await rig.Generate.GenerateCommand.ExecuteAsync(null);

        Assert.True(rig.Generate.OutputLines.Count <= GenerateViewModel.MaxOutputLines);
        Assert.Equal($"line {GenerateViewModel.MaxOutputLines + 699}", rig.Generate.OutputLines[^1].Text);
        Assert.DoesNotContain(rig.Generate.OutputLines, l => l.Text == "line 0");
    }

    // ---- Build only, publish only ----

    [Fact]
    public async Task BuildOnlyAndPublishOnlyUseTheSavedOutputFolderAndTheBuildSettings()
    {
        using var folder = new TempFolder();
        var errorLine = MsBuildOutput.ParseDiagnostic(ErrorLine)!;
        var runner = new FakeBuildRunner
        {
            BuildScript = (output, _) =>
            {
                output.Report(FakeBuildRunner.Line("Build started"));
                return Task.FromResult(new BuildResult { Succeeded = true, Warnings = 2, Duration = TimeSpan.FromSeconds(3) });
            },
        };
        var rig = await SetupAsync(folder, runner: runner);

        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);
        Assert.Empty(runner.Calls);
        Assert.Contains("no generated site", rig.Generate.Message);

        rig.CreateSite(built: false);
        rig.Main.Current.Build.Configuration = "Release";
        rig.Main.Current.Build.PublishFolder = "out";
        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);

        Assert.Equal(["build:Release"], runner.Calls);
        Assert.Equal(rig.Output, runner.LastSiteFolder);
        Assert.Equal("Skipped,Skipped,Ok,Skipped,Skipped", States(rig.Generate));
        Assert.Equal("Build succeeded with 2 warning(s) in 3 s.", rig.Main.Status);
        Assert.Contains("Build started", rig.Generate.OutputLines.Select(l => l.Text));

        await rig.Generate.PublishOnlyCommand.ExecuteAsync(null);
        Assert.Equal(["build:Release", "publish:out"], runner.Calls);
        Assert.Equal("Skipped,Skipped,Skipped,Ok,Skipped", States(rig.Generate));
        Assert.Equal($"Published to {Path.Combine(rig.Output, "out")}.", rig.Main.Status);

        runner.BuildScript = (_, _) => Task.FromResult(new BuildResult
        {
            Succeeded = false, ExitCode = 1, Errors = 1, Message = "dotnet build failed with exit code 1.", ParsedDiagnostics = [errorLine],
        });
        await rig.Generate.BuildOnlyCommand.ExecuteAsync(null);
        Assert.Equal("Skipped,Skipped,Failed,Skipped,Skipped", States(rig.Generate));
        Assert.StartsWith("Build failed: 1 error(s).", rig.Main.Status);
        Assert.Single(rig.Generate.Diagnostics);
    }

    [Fact]
    public async Task CancellingABuildOnlyMarksTheStageCancelled()
    {
        using var folder = new TempFolder();
        var started = new TaskCompletionSource();
        var runner = new FakeBuildRunner
        {
            BuildScript = async (_, token) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                }

                return BuildResult.Failure("dotnet build was cancelled.", cancelled: true);
            },
        };
        var rig = await SetupAsync(folder, runner: runner);
        rig.CreateSite(built: false);

        var run = rig.Generate.BuildOnlyCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(rig.Generate.IsBusy);
        rig.Generate.CancelCommand.Execute(null);
        await run;

        Assert.Equal("Skipped,Skipped,Cancelled,Skipped,Skipped", States(rig.Generate));
        Assert.Equal("Cancelled during Build.", rig.Main.Status);
        Assert.False(rig.Generate.IsBusy);
    }

    // ---- Real generation still works through the default runner ----

    [Fact]
    public async Task OpenOutputFolderAndOpenAfterGenerateUseTheFolderOpener()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);

        rig.Generate.OpenOutputFolderCommand.Execute(null);
        Assert.Contains("does not exist yet", rig.Generate.Message);
        Assert.Empty(rig.OpenedFolders);

        rig.CreateSite(built: false);
        rig.Generate.OpenOutputFolderCommand.Execute(null);
        Assert.Equal([rig.Output], rig.OpenedFolders);

        rig.Main.Settings.OpenOutputAfterGenerate = true;
        await rig.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(2, rig.OpenedFolders.Count);
        Assert.Equal($"Generated 2 file(s) into {rig.Output}.", rig.Main.Status);
    }

    // ---- Run site ----

    [Fact]
    public async Task RunSiteStartsDotnetRunOnAFreePortAndStopKillsIt()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);
        rig.CreateSite(built: true);

        rig.Generate.RunSiteCommand.Execute(null);

        Assert.True(rig.Generate.IsSiteRunning);
        Assert.Equal("http://localhost:5123", rig.Generate.SiteUrl);
        var launch = Assert.Single(rig.Site.Launches);
        Assert.Equal(rig.Main.Settings.DotnetPath, launch.FileName);
        Assert.Equal(["run", "--no-build", "--urls", "http://localhost:5123"], launch.Arguments);
        Assert.Equal(rig.Output, launch.WorkingDirectory);

        rig.Site.EmitLine("Now listening on: http://localhost:5123");
        rig.Site.EmitLine("boom", OutputStream.StdErr);
        Assert.Contains("Now listening on: http://localhost:5123", rig.Generate.OutputLines.Select(l => l.Text));
        Assert.Equal(OutputKind.Error, rig.Generate.OutputLines.Single(l => l.Text == "boom").Kind);

        // A second Run site while it runs does not start another process.
        rig.Generate.RunSiteCommand.Execute(null);
        Assert.Single(rig.Site.Launches);

        rig.Generate.OpenInBrowserCommand.Execute(null);
        Assert.Equal(["http://localhost:5123"], rig.OpenedUrls);

        rig.Generate.StopSiteCommand.Execute(null);
        Assert.Equal(1, rig.Site.Handle!.StopCount);
        Assert.False(rig.Generate.IsSiteRunning);
        Assert.Equal(string.Empty, rig.Generate.SiteUrl);

        // Stopped sites cannot be opened, and Stop is harmless the second time.
        rig.Generate.OpenInBrowserCommand.Execute(null);
        Assert.Single(rig.OpenedUrls);
        Assert.Contains("not running", rig.Generate.Message);
        rig.Generate.StopSiteCommand.Execute(null);
        Assert.Equal(1, rig.Site.Handle.StopCount);
    }

    [Fact]
    public async Task ASiteThatExitsByItselfIsShownAsStopped()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);
        rig.CreateSite(built: true);
        rig.Generate.RunSiteCommand.Execute(null);

        rig.Site.Exit(3);

        Assert.False(rig.Generate.IsSiteRunning);
        Assert.Equal("The site stopped (exit code 3).", rig.Main.Status);
        Assert.Equal(0, rig.Site.Handle!.StopCount);

        // It can be started again, on a new generation of the process.
        rig.Generate.RunSiteCommand.Execute(null);
        Assert.True(rig.Generate.IsSiteRunning);
        Assert.Equal(2, rig.Site.Launches.Count);
    }

    [Fact]
    public async Task RunSiteIsRefusedWithAReasonWhenThereIsNothingToRun()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);

        rig.Generate.RunSiteCommand.Execute(null);
        Assert.Contains("no generated site", rig.Generate.Message);

        rig.CreateSite(built: false);
        rig.Generate.RunSiteCommand.Execute(null);
        Assert.Contains("has not been built", rig.Generate.Message);

        Directory.CreateDirectory(Path.Combine(rig.Output, "bin"));
        rig.Main.Settings.DotnetPath = folder.File("missing-dotnet.exe");
        rig.Generate.RunSiteCommand.Execute(null);
        Assert.Contains("dotnet CLI was not found", rig.Generate.Message);

        rig.Main.Settings.DotnetPath = folder.File("fake-dotnet.exe");
        rig.Site.FailToStart = new InvalidOperationException("access denied");
        rig.Generate.RunSiteCommand.Execute(null);
        Assert.Contains("could not be started: access denied", rig.Generate.Message);
        Assert.False(rig.Generate.IsSiteRunning);
        Assert.Empty(rig.Site.Launches);
    }

    [Fact]
    public async Task TheSiteIsStoppedBeforeABuildAndWhenTheApplicationShutsDown()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);
        rig.CreateSite(built: true);

        rig.Generate.RunSiteCommand.Execute(null);
        await rig.Generate.GenerateCommand.ExecuteAsync(null);
        Assert.False(rig.Generate.IsSiteRunning);
        Assert.Equal(1, rig.Site.Handle!.StopCount);

        rig.Generate.RunSiteCommand.Execute(null);
        Assert.True(rig.Generate.IsSiteRunning);
        var second = rig.Site.Handle;
        rig.Generate.Shutdown();
        Assert.Equal(1, second.StopCount);
        Assert.False(rig.Generate.IsSiteRunning);
    }

    // ---- Tool status and summary ----

    [Fact]
    public async Task DockerStatusSaysFoundOrNotFoundAndWarnsWhenTheImageStageNeedsIt()
    {
        using var folder = new TempFolder();
        var present = false;
        var detector = new ToolDetector(detect: t => present && t == "docker" ? Path.Combine(folder.Path, "docker.exe") : null);
        var rig = await SetupAsync(folder);
        var main = new MainViewModel(rig.Dialogs) { OptionsEnvironment = new OptionsEnvironment { Detector = detector } };

        main.Generate.RefreshToolStatus();
        Assert.False(main.Generate.DockerFound);
        Assert.Equal("Docker CLI: not found on PATH", main.Generate.DockerStatus);
        Assert.False(main.Generate.HasDockerWarning);

        main.Current.Build.BuildDockerImage = true;
        main.Generate.RefreshSummary();
        Assert.True(main.Generate.HasDockerWarning);
        Assert.Contains("Docker CLI was not found", main.Generate.DockerWarning);

        present = true;
        main.Generate.RefreshToolStatus();
        Assert.True(main.Generate.DockerFound);
        Assert.StartsWith("Docker CLI: found (", main.Generate.DockerStatus);
        Assert.False(main.Generate.HasDockerWarning);

        // A path from the Options dialog wins over PATH.
        present = false;
        var docker = folder.File("my-docker.exe");
        File.WriteAllText(docker, string.Empty);
        main.Settings.DockerPath = docker;
        main.Generate.RefreshToolStatus();
        Assert.True(main.Generate.DockerFound);
        Assert.Equal($"Docker CLI: found ({docker})", main.Generate.DockerStatus);
        main.Settings.DockerPath = folder.File("gone.exe");
        main.Generate.RefreshToolStatus();
        Assert.False(main.Generate.DockerFound);
        Assert.Contains("path set in Options was not found", main.Generate.DockerStatus);
        Assert.Contains("dotnet CLI", main.Generate.DotnetStatus);
        Assert.Contains("Node", main.Generate.NodeStatus);
    }

    [Fact]
    public async Task TheSummaryLineFollowsTheBuildSettingsAndTheLinkJumpsToTheProjectTab()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);
        Assert.StartsWith("Generate only.", rig.Generate.BuildSummary);

        rig.Main.ProjectSettings.BuildAfterGenerate = true;
        rig.Main.ProjectSettings.Configuration = "Release";
        rig.Main.ProjectSettings.Publish = true;
        rig.Main.ProjectSettings.BuildDockerImage = true;
        rig.Main.ProjectSettings.DockerImageName = "shop";
        rig.Generate.RefreshSummary();

        Assert.Equal("Generate, then Docker files, Build (Release), Publish to publish, Docker image shop.", rig.Generate.BuildSummary);

        rig.Main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Generate");
        rig.Generate.ShowBuildSettingsCommand.Execute(null);
        Assert.Equal("Project", MainViewModel.TabNames[rig.Main.ActiveTabIndex]);
    }

    [Fact]
    public async Task BuildSettingsEditedInTheProjectTabAreMarkedDirty()
    {
        using var folder = new TempFolder();
        var rig = await SetupAsync(folder);
        await rig.Main.SaveProjectCommand.ExecuteAsync(null);
        Assert.False(rig.Main.IsDirty);

        rig.Main.ProjectSettings.BuildAfterGenerate = true;

        Assert.True(rig.Main.IsDirty);
    }
}
