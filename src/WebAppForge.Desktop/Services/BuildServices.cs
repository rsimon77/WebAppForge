using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Core.Localization;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.Services;

/// <summary>Runs generation and the dotnet and docker steps. The default uses the Core library; tests inject a fake.</summary>
public interface IBuildRunner
{
    Task<GenerateAndBuildResult> RunAsync(
        Project project, string outputDirectory, string projectFolder,
        BuildTools tools, IProgress<StageProgress> stages, IProgress<BuildOutputLine> output, CancellationToken cancellationToken);

    Task<BuildResult> BuildAsync(string siteFolder, string configuration, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken);

    Task<BuildResult> PublishAsync(string siteFolder, BuildSettings settings, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken);
}

/// <summary>Creates the Core <see cref="BuildTools"/> from the Options dialog values.</summary>
public static class BuildToolsFactory
{
    public static BuildTools From(AppSettings settings)
        => new(settings.DotnetPath, settings.DockerPath, settings.NodePath)
        {
            HttpProxy = settings.HttpProxyUrl,
            Timeout = settings.RequestTimeoutSeconds > 0 ? TimeSpan.FromSeconds(settings.RequestTimeoutSeconds) : null,
        };
}

/// <summary>The Core <see cref="GenerateAndBuild"/> and <see cref="SiteBuilder"/>.</summary>
public sealed class CoreBuildRunner : IBuildRunner
{
    public Task<GenerateAndBuildResult> RunAsync(
        Project project, string outputDirectory, string projectFolder,
        BuildTools tools, IProgress<StageProgress> stages, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
        => new GenerateAndBuild(tools).RunAsync(project, outputDirectory, projectFolder, stages, output, cancellationToken);

    public Task<BuildResult> BuildAsync(string siteFolder, string configuration, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
        => new SiteBuilder(tools).BuildAsync(siteFolder, configuration, output, cancellationToken);

    public Task<BuildResult> PublishAsync(string siteFolder, BuildSettings settings, BuildTools tools, IProgress<BuildOutputLine> output, CancellationToken cancellationToken)
        => new SiteBuilder(tools).PublishAsync(siteFolder, settings, output, cancellationToken);
}

/// <summary>A site process to start: the program, its argument list (no shell) and the folder to run in.</summary>
public sealed record SiteLaunch(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

/// <summary>A running site process.</summary>
public interface ISiteHandle
{
    int ProcessId { get; }

    /// <summary>Kills the process and everything it started. Safe to call more than once.</summary>
    void Stop();
}

/// <summary>Starts the generated site. Tests inject a fake that starts no process.</summary>
public interface ISiteRunner
{
    /// <param name="onLine">Called from any thread for each output line.</param>
    /// <param name="onExited">Called once from any thread with the exit code when the process ends, also after <see cref="ISiteHandle.Stop"/>.</param>
    ISiteHandle Start(SiteLaunch launch, Action<OutputStream, string> onLine, Action<int> onExited);
}

/// <summary>Finds a free TCP port.</summary>
public interface IPortFinder
{
    int FindFreePort();
}

public sealed class SystemPortFinder : IPortFinder
{
    public int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}

/// <summary>Starts a real process, redirects its output and kills its whole tree on Stop and when this application exits.</summary>
public sealed class SystemSiteRunner : ISiteRunner
{
    public ISiteHandle Start(SiteLaunch launch, Action<OutputStream, string> onLine, Action<int> onExited)
    {
        var info = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = launch.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine(OutputStream.StdOut, e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine(OutputStream.StdErr, e.Data);
            }
        };

        var handle = new Handle(process);
        process.Exited += (_, _) =>
        {
            int code;
            try
            {
                process.WaitForExit();
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }

            handle.MarkExited();
            onExited(code);
        };

        process.Start();
        process.StandardInput.Close();
        handle.Started(process.Id);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return handle;
    }

    private sealed class Handle : ISiteHandle
    {
        private readonly Process _process;
        private readonly EventHandler _onProcessExit;
        private int _exited;

        public Handle(Process process)
        {
            _process = process;
            _onProcessExit = (_, _) => Kill();
            AppDomain.CurrentDomain.ProcessExit += _onProcessExit;
        }

        public int ProcessId { get; private set; }

        public void Started(int id) => ProcessId = id;

        public void MarkExited()
        {
            Interlocked.Exchange(ref _exited, 1);
            AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
        }

        public void Stop() => Kill();

        private void Kill()
        {
            if (Volatile.Read(ref _exited) != 0)
            {
                return;
            }

            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already gone.
            }
        }
    }
}

/// <summary>Creates the translation provider for a kind. Tests inject a fake provider, so no test touches the network.</summary>
public interface ITranslationProviderFactory
{
    /// <summary>The provider, or null for <see cref="TranslationProviderKind.None"/>.</summary>
    ITranslationProvider? Create(TranslationProviderKind kind);
}

/// <summary>Google or Claude through the Core providers, using the timeout, proxy and Claude model of the application settings.</summary>
public sealed class DefaultTranslationProviderFactory(Func<AppSettings> settings, IApiKeyProvider keys) : ITranslationProviderFactory
{
    public ITranslationProvider? Create(TranslationProviderKind kind)
    {
        if (kind == TranslationProviderKind.None)
        {
            return null;
        }

        var current = settings();
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(current.HttpProxyUrl) && Uri.TryCreate(current.HttpProxyUrl, UriKind.Absolute, out var proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = current.RequestTimeoutSeconds > 0 ? TimeSpan.FromSeconds(current.RequestTimeoutSeconds) : Timeout.InfiniteTimeSpan };
        return TranslationProviders.Create(kind, client, keys, current.ClaudeModelId);
    }
}

/// <summary>
/// The machine and network services of the Languages and Generate tabs, replaced by fakes in tests. Defaults do the real
/// thing. <see cref="PostToUi"/> null means "back to the thread that started the operation".
/// </summary>
public sealed class ToolServices
{
    public IBuildRunner BuildRunner { get; init; } = new CoreBuildRunner();

    public ISiteRunner SiteRunner { get; init; } = new SystemSiteRunner();

    public IPortFinder PortFinder { get; init; } = new SystemPortFinder();

    /// <summary>Reads the translation API keys from the environment. Only used to tell whether a key is set.</summary>
    public IApiKeyProvider ApiKeys { get; init; } = new EnvironmentApiKeyProvider();

    /// <summary>Null uses <see cref="DefaultTranslationProviderFactory"/> with the current application settings.</summary>
    public ITranslationProviderFactory? TranslationProviders { get; init; }

    /// <summary>Opens a URL in the default browser.</summary>
    public Action<string> OpenUrl { get; init; } = OpenUrlInShell;

    /// <summary>Null posts to the synchronization context that was current when the operation started (the UI thread).</summary>
    public Action<Action>? PostToUi { get; init; }

    private static void OpenUrlInShell(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(Localizer.Instance["BuildServices_OnlyHttpAndHttpsAddressesCan"]);
        }

        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
