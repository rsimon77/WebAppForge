using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace WebAppForge.Desktop.Ui.Tests;

/// <summary>
/// One launched copy of the desktop app, attached through UI Automation, with its own copy of the Northwind sample in a
/// temp folder. <see cref="Dispose"/> closes the app, kills it if it is still running and deletes the temp folder.
/// </summary>
internal sealed class DesktopSession : IDisposable
{
    public const string MainWindowTitle = "WebAppForge";
    public const string ExpectedStatus = "Connected: 13 table(s), 13 new.";

    /// <summary>Desktop tests share one collection so that only one copy of the app runs at a time.</summary>
    public const string CollectionName = "Desktop UI";

    private readonly UIA3Automation _automation;
    private readonly int _processId;
    private readonly string _folder;

    private DesktopSession(UIA3Automation automation, int processId, Application app, Window window, string folder, string database)
    {
        _automation = automation;
        _processId = processId;
        App = app;
        Window = window;
        _folder = folder;
        Database = database;
    }

    /// <summary>The attached application.</summary>
    public Application App { get; }

    /// <summary>The main window (title "WebAppForge").</summary>
    public Window Window { get; }

    /// <summary>UI Automation instance used for top-level window lookups.</summary>
    public UIA3Automation Automation => _automation;

    /// <summary>The SQLite copy the app is connected to; the Northwind original is never opened.</summary>
    public string Database { get; }

    /// <summary>The desktop executable built from src\WebAppForge.Desktop.</summary>
    public static string ExePath(string sourceFile)
        => Path.Combine(RepositoryRoot(sourceFile), "src", "WebAppForge.Desktop", "bin", "Debug", "net10.0", "WebAppForge.Desktop.exe");

    /// <summary>Copies Northwind into a temp folder, starts the app and attaches to its main window.</summary>
    public static DesktopSession Launch([CallerFilePath] string? sourceFile = null)
    {
        var exe = ExePath(sourceFile!);
        Assert.True(File.Exists(exe), $"The desktop app was not built at {exe}. Build WebAppForge.Desktop first.");

        // The copy lives in a temp folder; the Northwind original is never opened for writing.
        var folder = Path.Combine(Path.GetTempPath(), "WebAppForge.Desktop.Ui.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var database = Path.Combine(folder, "northwind.db");
        File.Copy(SampleData.NorthwindSource, database);

        var automation = new UIA3Automation();
        int? processId = null;
        try
        {
            var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })
                ?? throw new InvalidOperationException($"Could not start {exe}.");
            processId = process.Id;
            var app = Application.Attach(process);
            var window = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(30)).Result
                ?? throw new InvalidOperationException("The main window did not appear within 30 seconds.");
            Assert.True(
                Retry.WhileFalse(() => window.Title == MainWindowTitle, TimeSpan.FromSeconds(10)).Result,
                $"Main window title was '{window.Title}', expected '{MainWindowTitle}'.\n{DumpTree(window)}");

            return new DesktopSession(automation, processId.Value, app, window, folder, database);
        }
        catch
        {
            KillIfRunning(processId);
            automation.Dispose();
            DeleteFolder(folder);
            throw;
        }
    }

    /// <summary>Selects a tab of the main window by its header text.</summary>
    public void SelectTab(string name)
    {
        var tab = Require(Find(ControlType.TabItem, name), $"The '{name}' tab was not found.");
        tab.AsTabItem().Select();
    }

    /// <summary>Finds an element of the main window by control type and exact name, or null.</summary>
    public AutomationElement? Find(ControlType type, string name)
        => Window.FindFirstDescendant(cf => cf.ByControlType(type).And(cf.ByName(name)));

    /// <summary>Waits up to <paramref name="timeout"/> for an element of the main window, or returns null.</summary>
    public AutomationElement? WaitFor(ControlType type, string name, TimeSpan? timeout = null)
        => Retry.WhileNull(() => Find(type, name), timeout ?? TimeSpan.FromSeconds(10)).Result;

    /// <summary>Returns the element, or fails the test with the UIA tree of the main window.</summary>
    public AutomationElement Require(AutomationElement? element, string message)
    {
        if (element is null)
        {
            Fail(message);
        }

        return element!;
    }

    /// <summary>Fails the test; the message is followed by the UIA tree of the main window, which is also written to the test output.</summary>
    public void Fail(string message)
    {
        var tree = DumpTree(Window);
        Console.WriteLine(tree);
        Assert.Fail($"{message}\n{tree}");
    }

    /// <summary>Waits for a window (for example a dialog) with the given title; returns null if none appears.</summary>
    public Window? FindTopLevelWindow(string title, TimeSpan timeout)
        => Retry.WhileNull(() => FindWindowByTitle(title), timeout).Result;

    /// <summary>
    /// True while a window with the given title is on screen. A closed window can linger in the UI Automation tree as
    /// off-screen, so off-screen matches count as closed.
    /// </summary>
    public bool IsWindowOpen(string title)
    {
        var window = FindWindowByTitle(title);
        return window is not null && !window.IsOffscreen;
    }

    /// <summary>Lists the top-level windows and the main window's child windows (title, name, off-screen), for diagnosing dialogs.</summary>
    public string DescribeWindows()
    {
        var sb = new StringBuilder("Top-level windows (title | name | offscreen):");
        foreach (var w in App.GetAllTopLevelWindows(_automation))
        {
            sb.AppendLine();
            sb.Append($"  '{Property(() => w.Title)}' | '{Property(() => w.Name)}' | {Property(() => w.IsOffscreen.ToString())}");
        }

        foreach (var w in Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)))
        {
            sb.AppendLine();
            sb.Append($"  (child) '{Property(() => w.Name)}' | offscreen {Property(() => w.IsOffscreen.ToString())}");
        }

        return sb.ToString();
    }

    private Window? FindWindowByTitle(string title)
    {
        // An owned dialog can be a separate top-level window or sit inside the main window's UIA tree, depending on the platform.
        var top = App.GetAllTopLevelWindows(_automation)
            .FirstOrDefault(w => Property(() => w.Title) == title || Property(() => w.Name) == title);
        if (top is not null)
        {
            return top;
        }

        return Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(title)))?.AsWindow();
    }

    /// <summary>Connects the app to the Northwind copy on the Database tab and waits for the status line.</summary>
    public void ConnectToNorthwind()
    {
        SelectTab("Database");

        // The SQLite path box, by its automation id: the tab has other empty edits since the server engines were added.
        var pathBox = Retry.WhileNull(
            () => Window.FindFirstDescendant(cf => cf.ByAutomationId("SqlitePathBox")),
            TimeSpan.FromSeconds(10)).Result;
        Require(pathBox, "The SQLite path box was not found.").AsTextBox().Enter(Database);

        Require(Find(ControlType.Button, "Connect"), "The Connect button was not found.").AsButton().Invoke();

        var status = Retry.WhileNull(
            () => Window.FindFirstDescendant(cf => cf.ByName(ExpectedStatus)),
            TimeSpan.FromSeconds(30)).Result;
        if (status is null)
        {
            Fail($"The status line did not show '{ExpectedStatus}'.");
        }
    }

    public void Dispose()
    {
        try
        {
            App.Close();
        }
        catch (Exception)
        {
            // The window may already be gone; the process is killed below if it is still running.
        }

        KillIfRunning(_processId);
        _automation.Dispose();
        DeleteFolder(_folder);
    }

    /// <summary>Reads a UIA property; Avalonia does not support every property on every element, so unsupported ones read as null.</summary>
    public static string? Property(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Lists control type, automation id and name of the window's elements, for diagnosing a missing control.</summary>
    public static string DumpTree(AutomationElement window)
    {
        var sb = new StringBuilder("Visible UIA elements (control type | automation id | name):");
        foreach (var element in window.FindAllDescendants().Take(200))
        {
            sb.AppendLine();
            sb.Append($"  {element.ControlType} | '{Property(() => element.AutomationId)}' | '{Property(() => element.Name)}'");
        }

        return sb.ToString();
    }

    /// <summary>The repository root, found from a test source file (tests\WebAppForge.Desktop.Ui.Tests\*.cs).</summary>
    public static string RepositoryRoot([CallerFilePath] string? sourceFile = null)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private static void KillIfRunning(int? processId)
    {
        if (processId is null)
        {
            return;
        }

        try
        {
            using var running = Process.GetProcessById(processId.Value);
            if (!running.WaitForExit(5000))
            {
                running.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; SQLite may still hold the file for a moment.
        }
    }
}

/// <summary>
/// A Fact that runs only on Windows, only when the Northwind sample exists and only when the desktop executable has been
/// built; otherwise it is skipped with the reason.
/// </summary>
public sealed class DesktopUiFactAttribute : FactAttribute
{
    public DesktopUiFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Skip = "The FlaUI tests drive the Windows desktop app and need Windows.";
        }
        else if (!File.Exists(SampleData.NorthwindSource))
        {
            Skip = $"Northwind SQLite not found at {SampleData.NorthwindSource}. Set WEBAPPFORGE_NORTHWIND_DB to a copy of northwind.db.";
        }
        else if (sourceFilePath is not null && !File.Exists(DesktopSession.ExePath(sourceFilePath)))
        {
            Skip = $"The desktop app is not built at {DesktopSession.ExePath(sourceFilePath)}. Build WebAppForge.Desktop first.";
        }
    }
}

/// <summary>Location of the Northwind SQLite sample. Tests copy it before use, so the original is never modified.</summary>
public static class SampleData
{
    public static string NorthwindSource { get; } =
        Environment.GetEnvironmentVariable("WEBAPPFORGE_NORTHWIND_DB") ?? string.Empty;
}
