using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class ExitAndToolTests
{
    // ---- Exit ----

    [Fact]
    public async Task ExitWithACleanProjectExitsWithoutAsking()
    {
        var dialogs = new FakeDialogService();
        var main = new MainViewModel(dialogs);
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, exits);
        Assert.Equal(0, dialogs.UnsavedPromptCount);
    }

    [Fact]
    public async Task ExitWithUnsavedChangesAndCancelKeepsTheAppOpen()
    {
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Cancel };
        var main = new MainViewModel(dialogs);
        main.ProjectName = "Edited";
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(0, exits);
        Assert.Equal(1, dialogs.UnsavedPromptCount);
        Assert.True(main.IsDirty);
    }

    [Fact]
    public async Task ExitWithUnsavedChangesAndDiscardExitsWithoutSaving()
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Discard, NextSaveProject = folder.File("p.xml") };
        var main = new MainViewModel(dialogs);
        main.ProjectName = "Edited";
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, exits);
        Assert.False(File.Exists(folder.File("p.xml")));
    }

    [Fact]
    public async Task ExitWithUnsavedChangesAndSaveSavesThenExits()
    {
        using var folder = new TempFolder();
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Save, NextSaveProject = folder.File("p.xml") };
        var main = new MainViewModel(dialogs);
        main.ProjectName = "Edited";
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, exits);
        Assert.True(File.Exists(folder.File("p.xml")));
        Assert.False(main.IsDirty);
    }

    [Fact]
    public async Task ExitStaysOpenWhenSaveIsChosenButTheSaveDialogIsCancelled()
    {
        var dialogs = new FakeDialogService { NextUnsavedChoice = UnsavedChangesChoice.Save, NextSaveProject = null };
        var main = new MainViewModel(dialogs);
        main.ProjectName = "Edited";
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(0, exits);
    }

    [Fact]
    public async Task ExitSkipsThePromptWhenConfirmOnExitIsOff()
    {
        var dialogs = new FakeDialogService();
        var main = new MainViewModel(dialogs);
        main.ApplySettings(new AppSettings { ConfirmOnExit = false }, "ok");
        main.ProjectName = "Edited";
        var exits = 0;
        main.ExitRequested += (_, _) => exits++;

        await main.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, exits);
        Assert.Equal(0, dialogs.UnsavedPromptCount);
    }

    // ---- Tool detection (BuildTools.Detect and ProbeVersionAsync, replaced by fakes) ----

    private static ToolDetector Detector(Func<string, string?> detect, Func<string, ToolVersionResult> probe, Func<string, bool> exists, List<string>? probed = null)
        => new(detect, (file, _) =>
        {
            probed?.Add(file);
            return Task.FromResult(probe(file));
        }, exists);

    [Fact]
    public async Task DetectResolvesFromPathAndReportsTheVersion()
    {
        var probed = new List<string>();
        var detector = Detector(t => @"C:\b\" + t + ".exe", _ => new ToolVersionResult(true, "10.0.100", string.Empty), _ => true, probed);

        var result = await detector.DetectAsync("dotnet", null);

        Assert.True(result.Found);
        Assert.Equal("10.0.100", result.Version);
        Assert.Equal(@"C:\b\dotnet.exe", result.Path);
        Assert.Equal(@"C:\b\dotnet.exe: 10.0.100", result.Message);
        Assert.Equal(@"C:\b\dotnet.exe", Assert.Single(probed));
    }

    [Fact]
    public async Task DetectUsesTheConfiguredPathInsteadOfPath()
    {
        var probed = new List<string>();
        var detector = Detector(_ => throw new InvalidOperationException("PATH must not be searched"), _ => new ToolVersionResult(true, "v22.1.0", string.Empty), f => f == @"D:\tools\node.exe", probed);

        var result = await detector.DetectAsync("node", @"D:\tools\node.exe");

        Assert.True(result.Found);
        Assert.Equal(@"D:\tools\node.exe", Assert.Single(probed));
    }

    [Fact]
    public async Task DetectReportsAToolThatIsNotOnPathWithoutProbingAnything()
    {
        var probed = new List<string>();
        var detector = Detector(_ => null, _ => throw new InvalidOperationException("must not run"), _ => false, probed);

        var result = await detector.DetectAsync("docker", null);

        Assert.False(result.Found);
        Assert.Contains("not found on PATH", result.Message);
        Assert.Empty(probed);
    }

    [Fact]
    public async Task DetectReportsAMissingConfiguredFile()
    {
        var detector = Detector(_ => null, _ => throw new InvalidOperationException("must not run"), _ => false);

        var result = await detector.DetectAsync("docker", @"C:\nope\docker.exe");

        Assert.False(result.Found);
        Assert.Contains("does not exist", result.Message);
    }

    [Fact]
    public async Task DetectShowsTheProbeErrorWhenTheVersionCannotBeRead()
    {
        var detector = Detector(t => t, _ => new ToolVersionResult(false, string.Empty, "'dotnet --version' timed out after 5 s."), _ => true);
        var timedOut = await detector.DetectAsync("dotnet", null);
        Assert.False(timedOut.Found);
        Assert.Contains("timed out after 5 s", timedOut.Message);

        detector = Detector(t => t, _ => new ToolVersionResult(false, string.Empty, "Could not start dotnet: Access denied"), _ => true);
        var failed = await detector.DetectAsync("dotnet", null);
        Assert.False(failed.Found);
        Assert.Contains("Access denied", failed.Message);
    }

    [Fact]
    public async Task TheDefaultDetectorUsesBuildToolsForARealDotnetAndAMissingProgram()
    {
        var detector = new ToolDetector();
        var dotnet = detector.ResolveOnPath("dotnet");
        if (dotnet is not null)
        {
            var found = await detector.DetectAsync("dotnet", dotnet);
            Assert.True(found.Found);
            Assert.False(string.IsNullOrWhiteSpace(found.Version));
        }

        Assert.Null(detector.ResolveOnPath("no-such-program-" + Guid.NewGuid().ToString("N")));
        var missing = await detector.DetectAsync("dotnet", Path.Combine(Path.GetTempPath(), "no-such-program-" + Guid.NewGuid().ToString("N")));
        Assert.False(missing.Found);
    }
}
