using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>Calls the handler directly on the reporting thread (unlike Progress&lt;T&gt;, which posts), so the view model decides how to reach the UI thread.</summary>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/// <summary>
/// The Generate tab: the output folder, the build console (stage strip, live output, diagnostics) and running the
/// generated site. Generate runs the generator and then whatever <see cref="Project.Build"/> asks for; Build only and
/// Publish only use the saved output folder. Long work runs off the UI thread and can be cancelled.
/// </summary>
public partial class GenerateViewModel : ViewModelBase
{
    /// <summary>The console keeps this many lines; older lines are dropped.</summary>
    public const int MaxOutputLines = 5000;

    private const int TrimBatch = 500;

    private readonly MainViewModel _main;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<OutputLineItem> _pendingLines = new();
    private readonly HashSet<BuildDiagnostic> _seenDiagnostics = [];
    private int _flushScheduled;
    private Action<Action> _post = action => action();
    private CancellationTokenSource? _cancel;
    private ISiteHandle? _site;
    private int _siteGeneration;
    private bool _cancelRequested;

    [ObservableProperty]
    private string _outputFolder = "generated";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _autoScroll = true;

    /// <summary>Why the last operation did not start, or empty. Validation problems of the project are shown by the Security view model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isSiteRunning;

    [ObservableProperty]
    private string _siteUrl = string.Empty;

    [ObservableProperty]
    private string _dotnetStatus = string.Empty;

    [ObservableProperty]
    private string _nodeStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DockerWarning))]
    [NotifyPropertyChangedFor(nameof(HasDockerWarning))]
    private string _dockerStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DockerWarning))]
    [NotifyPropertyChangedFor(nameof(HasDockerWarning))]
    private bool _dockerFound;

    /// <summary>True when the last run failed because a path set in Options was not found; the view then shows the "Open Options" link.</summary>
    [ObservableProperty]
    private bool _showOptionsHint;

    /// <summary>One line saying what Generate will do after writing the files, from the Build and Docker settings of the Project tab.</summary>
    [ObservableProperty]
    private string _buildSummary = string.Empty;

    /// <summary>The outcome of the last run, such as "Build failed: 2 error(s)".</summary>
    [ObservableProperty]
    private string _lastResult = string.Empty;

    public GenerateViewModel(MainViewModel main)
    {
        _main = main;
        Stages =
        [
            new StageItem(BuildStage.Generate, "Generate"),
            new StageItem(BuildStage.DockerFiles, "Docker files"),
            new StageItem(BuildStage.Build, "Build"),
            new StageItem(BuildStage.Publish, "Publish"),
            new StageItem(BuildStage.DockerImage, "Docker image"),
        ];
        _main.PropertyChanged += OnMainChanged;
    }

    public ObservableCollection<StageItem> Stages { get; }

    public ObservableCollection<OutputLineItem> OutputLines { get; } = [];

    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = [];

    public bool IsIdle => !IsBusy;

    public bool HasMessage => Message.Length > 0;

    public bool HasDockerWarning => DockerWarning.Length > 0;

    /// <summary>A warning when the Docker image stage is on but the Docker CLI is missing.</summary>
    public string DockerWarning
        => _main.Current.Build.BuildDockerImage && !DockerFound
            ? "The Docker image stage is on, but the Docker CLI was not found. Install Docker or set its path in Tools > Options."
            : string.Empty;

    public void LoadFrom(Project project)
    {
        OutputFolder = project.OutputFolder;
        RefreshSummary();
        RefreshToolStatus();
    }

    partial void OnOutputFolderChanged(string value)
    {
        _main.Current.OutputFolder = value;
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTabIndex))
        {
            RefreshSummary();
            RefreshToolStatus();
        }
    }

    /// <summary>Re-reads the Build and Docker settings of the Project tab into the one-line summary.</summary>
    public void RefreshSummary()
    {
        var build = _main.Current.Build;
        var steps = new List<string>();
        if (build.GenerateDockerfile || build.GenerateCompose || build.BuildDockerImage)
        {
            steps.Add("Docker files");
        }

        if (build.BuildAfterGenerate)
        {
            steps.Add($"Build ({build.Configuration})");
        }

        if (build.Publish)
        {
            steps.Add($"Publish to {build.PublishFolder}");
        }

        if (build.BuildDockerImage)
        {
            steps.Add($"Docker image {build.EffectiveImageName(_main.Current.Name)}");
        }

        BuildSummary = steps.Count == 0
            ? "Generate only. Build, publish and Docker options are in the Project tab."
            : "Generate, then " + string.Join(", ", steps) + ".";
        OnPropertyChanged(nameof(DockerWarning));
        OnPropertyChanged(nameof(HasDockerWarning));
    }

    /// <summary>Looks for dotnet, node and docker: the path set in Tools > Options, or PATH.</summary>
    public void RefreshToolStatus()
    {
        var settings = _main.Settings;
        DotnetStatus = Describe("dotnet CLI", "dotnet", settings.DotnetPath, out _);
        NodeStatus = Describe("Node", "node", settings.NodePath, out _);
        DockerStatus = Describe("Docker CLI", "docker", settings.DockerPath, out var docker);
        DockerFound = docker;
    }

    private string Describe(string label, string tool, string? configured, out bool found)
    {
        var path = FindTool(tool, configured);
        found = path is not null;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return found ? $"{label}: found ({path})" : $"{label}: the path set in Options was not found ({configured})";
        }

        return found ? $"{label}: found ({path})" : $"{label}: not found on PATH";
    }

    private string? FindTool(string tool, string? configured) => _main.OptionsEnvironment.Detector.Find(tool, configured);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _main.Dialogs.PickFolderAsync(ProjectPaths.Resolve(_main.ProjectFolder, OutputFolder));
        if (path is null)
        {
            return;
        }

        OutputFolder = _main.ProjectFolder is null ? path : ProjectPaths.ToRelative(_main.ProjectFolder, path);
    }

    /// <summary>Opens Tools &gt; Options on the Tools and paths page.</summary>
    [RelayCommand]
    private Task OpenOptionsAsync() => _main.ShowToolOptionsAsync();

    [RelayCommand]
    private void ShowBuildSettings()
        => _main.ActiveTabIndex = MainViewModel.TabNames.ToList().IndexOf("Project");

    // ---- Generate, build, publish ----

    /// <summary>Generates the site, then does what the build settings ask for (build or publish, Docker files and image).</summary>
    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var folder = _main.ProjectFolder;
        if (folder is null)
        {
            Block("Save the project first. Output and database paths are resolved against the project folder.");
            return;
        }

        _main.Security.Refresh();
        if (!_main.Security.CanGenerate)
        {
            Block($"Generation blocked: {_main.Security.ValidationMessage}");
            return;
        }

        var project = _main.Current;
        if (!string.Equals(project.Database.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            Block($"Not supported: Provider '{project.Database.Provider}' is not supported yet. Only SQLite is implemented.");
            return;
        }

        var outputDirectory = ProjectPaths.Resolve(folder, OutputFolder);
        var token = Begin();
        foreach (var stage in Stages)
        {
            stage.State = StageState.Pending;
            stage.Message = string.Empty;
        }

        AddInfo($"Generate: {outputDirectory}");
        var stages = new InlineProgress<StageProgress>(p => _post(() => ApplyStage(p)));
        var output = new InlineProgress<BuildOutputLine>(Enqueue);
        GenerateAndBuildResult? result = null;
        string? failure = null;
        try
        {
            result = await _main.Tools.BuildRunner.RunAsync(project, outputDirectory, folder, _main.BuildTools, stages, output, token);
        }
        catch (OperationCanceledException)
        {
            failure = "Cancelled.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = $"Generation failed: {ex.Message}";
        }
        finally
        {
            End();
        }

        if (result is not null)
        {
            AddDiagnostics(result.Build);
            AddDiagnostics(result.Publish);
            AddDiagnostics(result.DockerImage);
        }

        if (failure is not null)
        {
            LastResult = failure;
            _main.Status = failure;
            return;
        }

        ShowOptionsHint = false;
        if (result!.Succeeded)
        {
            var status = $"Generated {result.GeneratedFiles.Count} file(s) into {outputDirectory}.";
            if (result.DockerFiles.Count > 0)
            {
                status += $" Wrote {result.DockerFiles.Count} Docker file(s).";
            }

            if (result.Build is { } build)
            {
                status += $" Built with {build.Warnings} warning(s) in {build.Duration.TotalSeconds:0.#} s.";
            }

            if (result.Publish is { } publish)
            {
                status += $" Published to {publish.PublishPath}.";
            }

            if (result.DockerImage is not null)
            {
                status += $" Built Docker image {project.Build.EffectiveImageName(project.Name)}.";
            }

            LastResult = status;
            _main.Status = status;
            if (_main.Settings.OpenOutputAfterGenerate)
            {
                OpenFolder(outputDirectory, appendToStatus: true);
            }

            return;
        }

        var failedStage = result.FailedStage ?? BuildStage.Generate;
        var title = Stages.First(s => s.Stage == failedStage).Title;
        var cancelled = Stages.Any(s => s.State == StageState.Cancelled) || _cancelRequested;
        failure = cancelled
            ? $"Cancelled during {title}."
            : failedStage == BuildStage.Generate
                ? $"Generation failed: {result.Message}"
                : $"{title} failed: {result.Message}";
        LastResult = failure;
        _main.Status = failure;
        RelayMissingPath(result.Message);
    }

    /// <summary>Runs <c>dotnet build</c> on the site in the saved output folder.</summary>
    [RelayCommand]
    private Task BuildOnlyAsync() => RunSingleAsync(BuildStage.Build);

    /// <summary>Runs <c>dotnet publish</c> on the site in the saved output folder.</summary>
    [RelayCommand]
    private Task PublishOnlyAsync() => RunSingleAsync(BuildStage.Publish);

    private async Task RunSingleAsync(BuildStage stage)
    {
        if (IsBusy)
        {
            return;
        }

        var siteFolder = ResolveSiteFolder();
        if (siteFolder is null)
        {
            return;
        }

        var token = Begin();
        foreach (var item in Stages)
        {
            item.Message = string.Empty;
            item.State = item.Stage == stage ? StageState.Running : StageState.Skipped;
        }

        var title = Stages.First(s => s.Stage == stage).Title;
        AddInfo($"{title}: {siteFolder}");
        var output = new InlineProgress<BuildOutputLine>(Enqueue);
        BuildResult result;
        try
        {
            var build = _main.Current.Build;
            result = stage == BuildStage.Build
                ? await _main.Tools.BuildRunner.BuildAsync(siteFolder, build.Configuration, _main.BuildTools, output, token)
                : await _main.Tools.BuildRunner.PublishAsync(siteFolder, build, _main.BuildTools, output, token);
        }
        catch (OperationCanceledException)
        {
            result = BuildResult.Failure($"{title} was cancelled.", cancelled: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = BuildResult.Failure(ex.Message);
        }
        finally
        {
            End();
        }

        AddDiagnostics(result);
        var item2 = Stages.First(s => s.Stage == stage);
        string status;
        if (result.Succeeded)
        {
            item2.State = StageState.Ok;
            item2.Message = stage == BuildStage.Publish ? result.PublishPath ?? string.Empty : $"{result.Warnings} warning(s)";
            status = stage == BuildStage.Build
                ? $"Build succeeded with {result.Warnings} warning(s) in {result.Duration.TotalSeconds:0.#} s."
                : $"Published to {result.PublishPath}.";
        }
        else
        {
            item2.State = result.Cancelled ? StageState.Cancelled : StageState.Failed;
            item2.Message = result.Message;
            status = result.Cancelled
                ? $"Cancelled during {title}."
                : result.Errors > 0 ? $"{title} failed: {result.Errors} error(s). {result.Message}".TrimEnd() : $"{title} failed: {result.Message}";
        }

        LastResult = status;
        _main.Status = status;
        ShowOptionsHint = false;
        if (!result.Succeeded)
        {
            RelayMissingPath(result.Message);
        }
    }

    /// <summary>When a stage failed because a configured tool path does not exist, writes the message to the console and shows the Open Options link.</summary>
    private void RelayMissingPath(string message)
    {
        if (!message.Contains("path was not found", StringComparison.Ordinal))
        {
            return;
        }

        _pendingLines.Enqueue(new OutputLineItem(message, OutputKind.Error, DateTimeOffset.Now));
        Flush();
        ShowOptionsHint = true;
    }

    /// <summary>The saved output folder when it holds a generated site, otherwise null with <see cref="Message"/> set.</summary>
    private string? ResolveSiteFolder()
    {
        var folder = _main.ProjectFolder;
        if (folder is null)
        {
            Block("Save the project first. Output and database paths are resolved against the project folder.");
            return null;
        }

        var outputDirectory = ProjectPaths.Resolve(folder, OutputFolder);
        if (!Directory.Exists(outputDirectory) || Directory.GetFiles(outputDirectory, "*.csproj").Length == 0)
        {
            Block($"There is no generated site in {outputDirectory}. Use Generate first.");
            return null;
        }

        return outputDirectory;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelRequested = true;
        _cancel?.Cancel();
    }

    /// <summary>Stops the site and any running build. Called when the application exits.</summary>
    public void Shutdown()
    {
        _cancel?.Cancel();
        StopSite();
    }

    private CancellationToken Begin()
    {
        if (IsSiteRunning)
        {
            AddInfo("Stopping the running site before building.");
            StopSite();
        }

        _post = MakePost();
        Message = string.Empty;
        _cancelRequested = false;
        lock (_gate)
        {
            Diagnostics.Clear();
            _seenDiagnostics.Clear();
        }

        _cancel = new CancellationTokenSource();
        IsBusy = true;
        return _cancel.Token;
    }

    private void End()
    {
        Flush();
        _cancel?.Dispose();
        _cancel = null;
        IsBusy = false;
    }

    private Action<Action> MakePost()
    {
        if (_main.Tools.PostToUi is { } custom)
        {
            return custom;
        }

        var context = SynchronizationContext.Current;
        return context is null ? action => action() : action => context.Post(_ => action(), null);
    }

    private void Block(string message)
    {
        Message = message;
        _main.Status = message;
    }

    // ---- Stage strip ----

    private void ApplyStage(StageProgress progress)
    {
        lock (_gate)
        {
            var item = Stages.FirstOrDefault(s => s.Stage == progress.Stage);
            if (item is null)
            {
                return;
            }

            item.State = progress.Status switch
            {
                StageStatus.Running => StageState.Running,
                StageStatus.Succeeded => StageState.Ok,
                StageStatus.Failed => StageState.Failed,
                StageStatus.Skipped => StageState.Skipped,
                _ => StageState.Cancelled,
            };
            item.Message = progress.Message;
        }
    }

    // ---- Console ----

    private void Enqueue(BuildOutputLine line)
    {
        _pendingLines.Enqueue(Classify(line.Text, line.Stream, line.Timestamp));
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            _post(Flush);
        }
    }

    private static OutputLineItem Classify(string text, OutputStream stream, DateTimeOffset timestamp)
    {
        var kind = OutputKind.Normal;
        if (MsBuildOutput.ParseDiagnostic(text) is { } diagnostic)
        {
            kind = diagnostic.IsError ? OutputKind.Error : OutputKind.Warning;
        }
        else if (stream == OutputStream.StdErr || text.Contains("Build FAILED", StringComparison.Ordinal))
        {
            kind = OutputKind.Error;
        }

        return new OutputLineItem(text, kind, timestamp);
    }

    private void AddInfo(string text)
    {
        _pendingLines.Enqueue(new OutputLineItem(text, OutputKind.Normal, DateTimeOffset.Now));
        Flush();
    }

    /// <summary>Moves queued output lines into the console list. Runs on the UI thread; lines arrive in batches so a chatty build does not flood the dispatcher.</summary>
    private void Flush()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        lock (_gate)
        {
            while (_pendingLines.TryDequeue(out var line))
            {
                OutputLines.Add(line);
                if (MsBuildOutput.ParseDiagnostic(line.Text) is { } diagnostic && _seenDiagnostics.Add(diagnostic))
                {
                    Diagnostics.Add(ToRow(diagnostic));
                }
            }

            if (OutputLines.Count > MaxOutputLines)
            {
                for (var i = 0; i < TrimBatch; i++)
                {
                    OutputLines.RemoveAt(0);
                }
            }
        }
    }

    private void AddDiagnostics(BuildResult? result)
    {
        if (result is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var diagnostic in result.ParsedDiagnostics)
            {
                if (_seenDiagnostics.Add(diagnostic))
                {
                    Diagnostics.Add(ToRow(diagnostic));
                }
            }
        }
    }

    private static DiagnosticRow ToRow(BuildDiagnostic d)
        => new(d.IsError ? "Error" : "Warning", d.Code, d.Message, d.File, d.Line);

    [RelayCommand]
    private void ClearOutput()
    {
        lock (_gate)
        {
            OutputLines.Clear();
            Diagnostics.Clear();
            _seenDiagnostics.Clear();
        }
    }

    [RelayCommand]
    private async Task CopyAllAsync()
    {
        string text;
        lock (_gate)
        {
            text = string.Join(Environment.NewLine, OutputLines.Select(l => l.Text));
        }

        await _main.Dialogs.SetClipboardTextAsync(text);
        _main.Status = "Copied the console output to the clipboard.";
    }

    /// <summary>Copies "path:line" of a diagnostic to the clipboard. The grid calls this on double-click.</summary>
    [RelayCommand]
    private async Task CopyDiagnosticAsync(DiagnosticRow? row)
    {
        if (row is null)
        {
            return;
        }

        await _main.Dialogs.SetClipboardTextAsync(row.Location);
        _main.Status = $"Copied {row.Location} to the clipboard.";
    }

    // ---- Run site ----

    /// <summary>Starts <c>dotnet run --no-build</c> for the built site on a free port.</summary>
    [RelayCommand]
    private void RunSite()
    {
        if (IsSiteRunning)
        {
            return;
        }

        if (IsBusy)
        {
            Block("Wait for the current build to finish before running the site.");
            return;
        }

        var siteFolder = ResolveSiteFolder();
        if (siteFolder is null)
        {
            return;
        }

        if (!Directory.Exists(Path.Combine(siteFolder, "bin")))
        {
            Block("The site has not been built yet. Use Build only (or turn on Build in the Project tab) first.");
            return;
        }

        var dotnet = FindTool("dotnet", _main.Settings.DotnetPath);
        if (dotnet is null)
        {
            ShowOptionsHint = true;
            Block("The dotnet CLI was not found. Install the .NET SDK or set its path in Tools > Options.");
            return;
        }

        Message = string.Empty;
        ShowOptionsHint = false;
        var post = MakePost();
        _post = post;
        int port;
        try
        {
            port = _main.Tools.PortFinder.FindFreePort();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Block($"No free port was found: {ex.Message}");
            return;
        }

        var url = $"http://localhost:{port}";
        var generation = ++_siteGeneration;
        var launch = new SiteLaunch(dotnet, ["run", "--no-build", "--urls", url], siteFolder);
        IsSiteRunning = true;
        SiteUrl = url;
        AddInfo($"Run site: dotnet run --no-build --urls {url}");
        ISiteHandle handle;
        try
        {
            handle = _main.Tools.SiteRunner.Start(
                launch,
                (stream, text) => Enqueue(new BuildOutputLine(stream, text, DateTimeOffset.Now)),
                code => post(() => OnSiteExited(generation, code)));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            IsSiteRunning = false;
            SiteUrl = string.Empty;
            Block($"The site could not be started: {ex.Message}");
            return;
        }

        if (IsSiteRunning && generation == _siteGeneration)
        {
            _site = handle;
            _main.Status = $"The site is running at {url}.";
        }
    }

    /// <summary>Stops the site and everything it started.</summary>
    [RelayCommand]
    public void StopSite()
    {
        var handle = _site;
        _site = null;
        if (handle is null && !IsSiteRunning)
        {
            return;
        }

        _siteGeneration++;
        try
        {
            handle?.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process is already gone.
        }

        IsSiteRunning = false;
        SiteUrl = string.Empty;
        AddInfo("The site was stopped.");
        _main.Status = "The site was stopped.";
    }

    private void OnSiteExited(int generation, int exitCode)
    {
        if (generation != _siteGeneration)
        {
            return;
        }

        _site = null;
        IsSiteRunning = false;
        SiteUrl = string.Empty;
        AddInfo($"The site stopped on its own (exit code {exitCode}).");
        _main.Status = $"The site stopped (exit code {exitCode}).";
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var folder = _main.ProjectFolder;
        if (folder is null)
        {
            Block("Save the project first. Output and database paths are resolved against the project folder.");
            return;
        }

        var outputDirectory = ProjectPaths.Resolve(folder, OutputFolder);
        if (!Directory.Exists(outputDirectory))
        {
            Block($"The output folder {outputDirectory} does not exist yet. Use Generate first.");
            return;
        }

        OpenFolder(outputDirectory, appendToStatus: false);
    }

    private void OpenFolder(string path, bool appendToStatus)
    {
        try
        {
            _main.OptionsEnvironment.OpenFolder(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            var text = $"The folder could not be opened: {ex.Message}";
            _main.Status = appendToStatus ? _main.Status + " " + text : text;
        }
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (!IsSiteRunning)
        {
            Block("The site is not running. Use Run site first.");
            return;
        }

        try
        {
            _main.Tools.OpenUrl(SiteUrl);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _main.Status = $"The browser could not be opened: {ex.Message}";
        }
    }
}
