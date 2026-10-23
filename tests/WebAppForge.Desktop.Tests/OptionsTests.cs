using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WebAppForge.Core.Build;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests;

public class OptionsTests
{
    // ---- AppSettings file ----

    [Fact]
    public void NewPropertiesRoundTripThroughTheSettingsFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var saved = new AppSettings
        {
            ReopenLastProject = true,
            LastProjectPath = @"C:\p\shop.xml",
            RecentFilesLength = 12,
            RecentFiles = [@"C:\p\shop.xml", @"C:\p\blog.xml"],
            ConfirmOnExit = false,
            AutosaveMinutes = 7,
            SessionTimeoutMinutes = 45,
            UiFontSize = 16,
            CompactTables = true,
            DefaultOutputFolderPattern = "out/{Project}",
            DefaultPageSize = 50,
            OpenOutputAfterGenerate = true,
            ClearOutputBeforeGenerate = true,
            BuildAfterGenerate = true,
            DefaultBuildConfiguration = BuildConfiguration.Release,
            DotnetPath = @"C:\dotnet\dotnet.exe",
            NodePath = @"C:\node\node.exe",
            DockerPath = @"C:\docker\docker.exe",
            TranslationProvider = TranslationProvider.Claude,
            ClaudeModelId = "claude-sonnet-5-5",
            HttpProxyUrl = "http://proxy.example.com:8080",
            RequestTimeoutSeconds = 90,
            LogLevel = AppLogLevel.Debug,
        };

        Assert.True(new SettingsStore(path).Save(saved));
        var loaded = new SettingsStore(path).Load();

        Assert.True(loaded.ReopenLastProject);
        Assert.Equal(@"C:\p\shop.xml", loaded.LastProjectPath);
        Assert.Equal(12, loaded.RecentFilesLength);
        Assert.Equal(new[] { @"C:\p\shop.xml", @"C:\p\blog.xml" }, loaded.RecentFiles);
        Assert.False(loaded.ConfirmOnExit);
        Assert.Equal(7, loaded.AutosaveMinutes);
        Assert.Equal(45, loaded.SessionTimeoutMinutes);
        Assert.Equal(16, loaded.UiFontSize);
        Assert.True(loaded.CompactTables);
        Assert.Equal("out/{Project}", loaded.DefaultOutputFolderPattern);
        Assert.Equal(50, loaded.DefaultPageSize);
        Assert.True(loaded.OpenOutputAfterGenerate);
        Assert.True(loaded.ClearOutputBeforeGenerate);
        Assert.True(loaded.BuildAfterGenerate);
        Assert.Equal(BuildConfiguration.Release, loaded.DefaultBuildConfiguration);
        Assert.Equal(@"C:\dotnet\dotnet.exe", loaded.DotnetPath);
        Assert.Equal(@"C:\node\node.exe", loaded.NodePath);
        Assert.Equal(@"C:\docker\docker.exe", loaded.DockerPath);
        Assert.Equal(TranslationProvider.Claude, loaded.TranslationProvider);
        Assert.Equal("claude-sonnet-5-5", loaded.ClaudeModelId);
        Assert.Equal("http://proxy.example.com:8080", loaded.HttpProxyUrl);
        Assert.Equal(90, loaded.RequestTimeoutSeconds);
        Assert.Equal(AppLogLevel.Debug, loaded.LogLevel);
    }

    [Fact]
    public void OldSettingsFileLoadsWithDefaultsForTheNewProperties()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """
            { "SessionTimeoutMinutes": 12, "ThemeName": "Green (success)", "Appearance": "Dark", "CustomStylesPath": "C:\\s.axaml" }
            """);

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(12, loaded.SessionTimeoutMinutes);
        Assert.Equal("Green (success)", loaded.ThemeName);
        Assert.Equal(AppearanceMode.Dark, loaded.Appearance);
        Assert.Equal(@"C:\s.axaml", loaded.CustomStylesPath);
        Assert.False(loaded.ReopenLastProject);
        Assert.Equal(10, loaded.RecentFilesLength);
        Assert.True(loaded.ConfirmOnExit);
        Assert.Equal(0, loaded.AutosaveMinutes);
        Assert.Equal(14, loaded.UiFontSize);
        Assert.False(loaded.CompactTables);
        Assert.Equal("generated", loaded.DefaultOutputFolderPattern);
        Assert.Equal(20, loaded.DefaultPageSize);
        Assert.False(loaded.ClearOutputBeforeGenerate);
        Assert.Equal(BuildConfiguration.Debug, loaded.DefaultBuildConfiguration);
        Assert.Null(loaded.DotnetPath);
        Assert.Equal(TranslationProvider.None, loaded.TranslationProvider);
        Assert.Equal("claude-haiku-5-5", loaded.ClaudeModelId);
        Assert.Null(loaded.HttpProxyUrl);
        Assert.Equal(30, loaded.RequestTimeoutSeconds);
        Assert.Equal(AppLogLevel.Info, loaded.LogLevel);
        Assert.Empty(loaded.RecentFiles);
    }

    [Fact]
    public void LoadedNewValuesOutOfRangeAreCorrected()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        File.WriteAllText(path, """{ "UiFontSize": 99, "RecentFilesLength": 1, "AutosaveMinutes": -4, "RequestTimeoutSeconds": 0, "DefaultPageSize": 25 }""");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.MaxUiFontSize, loaded.UiFontSize);
        Assert.Equal(AppSettings.MinRecentFilesLength, loaded.RecentFilesLength);
        Assert.Equal(0, loaded.AutosaveMinutes);
        Assert.Equal(AppSettings.MinRequestTimeoutSeconds, loaded.RequestTimeoutSeconds);
        Assert.Equal(20, loaded.DefaultPageSize);
    }

    [Fact]
    public void SettingsFileNeverContainsKeys()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        new SettingsStore(path).Save(new AppSettings());

        var text = File.ReadAllText(path);

        Assert.DoesNotContain("ApiKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---- validation ----

    [Theory]
    [InlineData("9", false)]
    [InlineData("10", true)]
    [InlineData("20", true)]
    [InlineData("21", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void FontSizeMustBeTenToTwenty(string text, bool valid)
    {
        var options = NewOptions();
        options.UiFontSizeText = text;

        Assert.Equal(valid, options.UiFontSizeError is null);
        Assert.Equal(!valid, options.HasErrors);
        Assert.Equal(valid, options.CanApply);
    }

    [Theory]
    [InlineData("4", false)]
    [InlineData("5", true)]
    [InlineData("20", true)]
    [InlineData("21", false)]
    public void RecentFilesLengthMustBeFiveToTwenty(string text, bool valid)
    {
        var options = NewOptions();
        options.RecentFilesLengthText = text;

        Assert.Equal(valid, options.RecentFilesLengthError is null);
    }

    [Theory]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("30", true)]
    [InlineData("120", true)]
    [InlineData("121", false)]
    [InlineData("x", false)]
    public void AutosaveMustBeZeroToOneTwenty(string text, bool valid)
    {
        var options = NewOptions();
        options.AutosaveMinutesText = text;

        Assert.Equal(valid, options.AutosaveMinutesError is null);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("http://proxy.example.com:8080", true)]
    [InlineData("https://proxy.example.com", true)]
    [InlineData("socks5://10.0.0.1:1080", true)]
    [InlineData("proxy.example.com:8080", false)]
    [InlineData("ftp://proxy.example.com", false)]
    [InlineData("http://", false)]
    [InlineData("not a url", false)]
    public void ProxyUrlMustBeEmptyOrAnAbsoluteProxyUrl(string text, bool valid)
    {
        var options = NewOptions();
        options.HttpProxyUrl = text;

        Assert.Equal(valid, options.HttpProxyUrlError is null);
    }

    [Fact]
    public void InvalidEntriesBlockApplyAndOk()
    {
        var applied = new List<AppSettings>();
        var options = NewOptions(apply: applied.Add);
        options.UiFontSizeText = "99";

        Assert.False(options.Apply());
        Assert.False(options.Ok());

        Assert.Empty(applied);
        Assert.Contains("UI font size", options.UiFontSizeError);

        options.UiFontSizeText = "16";
        Assert.True(options.Ok());
        Assert.Equal(16, Assert.Single(applied).UiFontSize);
    }

    [Fact]
    public void ModelIdAndOutputPatternMustNotBeEmpty()
    {
        var options = NewOptions();
        options.ClaudeModelId = " ";
        options.OutputFolderPattern = "";

        Assert.NotNull(options.ClaudeModelIdError);
        Assert.NotNull(options.OutputFolderPatternError);
        Assert.True(options.HasErrors);
    }

    [Fact]
    public void ApplyHandsOverTheEditedValuesAndKeepsHiddenOnes()
    {
        var applied = new List<AppSettings>();
        var start = new AppSettings { LastProjectPath = @"C:\p\a.xml", RecentFiles = [@"C:\p\a.xml"] };
        var options = NewOptions(start, applied.Add);
        options.ThemeName = "Teal";
        options.Appearance = AppearanceMode.Dark;
        options.CompactTables = true;
        options.DefaultPageSize = 100;
        options.TranslationProvider = TranslationProvider.Google;
        options.RequestTimeoutText = "45";

        Assert.True(options.Apply());

        var next = Assert.Single(applied);
        Assert.Equal("Teal", next.ThemeName);
        Assert.Equal(AppearanceMode.Dark, next.Appearance);
        Assert.True(next.CompactTables);
        Assert.Equal(100, next.DefaultPageSize);
        Assert.Equal(TranslationProvider.Google, next.TranslationProvider);
        Assert.Equal(45, next.RequestTimeoutSeconds);
        Assert.Equal(@"C:\p\a.xml", next.LastProjectPath);
        Assert.Equal(new[] { @"C:\p\a.xml" }, next.RecentFiles);
    }

    // ---- reset ----

    [Fact]
    public void ResetCategoryRestoresOnlyThatPage()
    {
        var options = NewOptions(NonDefaultSettings());

        options.ResetCategory(OptionsCategory.Appearance);

        Assert.Equal(AppSettings.DefaultUiFontSize.ToString(), options.UiFontSizeText);
        Assert.False(options.CompactTables);
        Assert.Equal(AppearanceMode.System, options.Appearance);
        Assert.Equal(ThemePalettes.Default.Name, options.ThemeName);
        Assert.Equal(string.Empty, options.CustomStylesPath);

        // Other pages keep their edited values.
        Assert.True(options.ReopenLastProject);
        Assert.Equal("5", options.AutosaveMinutesText);
        Assert.Equal(BuildConfiguration.Release, options.BuildConfiguration);
        Assert.Equal("http://proxy.example.com:8080", options.HttpProxyUrl);
    }

    [Theory]
    [InlineData(OptionsCategory.General)]
    [InlineData(OptionsCategory.Appearance)]
    [InlineData(OptionsCategory.Generate)]
    [InlineData(OptionsCategory.Tools)]
    [InlineData(OptionsCategory.Translation)]
    [InlineData(OptionsCategory.Network)]
    [InlineData(OptionsCategory.Advanced)]
    public void ResetPageCommandResetsTheSelectedPage(string categoryId)
    {
        var options = NewOptions(NonDefaultSettings());
        options.SelectedCategory = options.Categories.Single(c => c.Id == categoryId);

        options.ResetPageCommand.Execute(null);
        var after = options.ToSettings();
        var defaults = new AppSettings();

        switch (categoryId)
        {
            case OptionsCategory.General:
                Assert.Equal(defaults.RecentFilesLength, after.RecentFilesLength);
                Assert.Equal(defaults.AutosaveMinutes, after.AutosaveMinutes);
                Assert.Equal(defaults.ConfirmOnExit, after.ConfirmOnExit);
                Assert.Equal(defaults.SessionTimeoutMinutes, after.SessionTimeoutMinutes);
                Assert.False(after.ReopenLastProject);
                break;
            case OptionsCategory.Appearance:
                Assert.Equal(defaults.UiFontSize, after.UiFontSize);
                break;
            case OptionsCategory.Generate:
                Assert.Equal(defaults.DefaultOutputFolderPattern, after.DefaultOutputFolderPattern);
                Assert.Equal(defaults.DefaultPageSize, after.DefaultPageSize);
                Assert.False(after.ClearOutputBeforeGenerate);
                Assert.Equal(BuildConfiguration.Debug, after.DefaultBuildConfiguration);
                break;
            case OptionsCategory.Tools:
                Assert.Null(after.DotnetPath);
                Assert.Null(after.NodePath);
                Assert.Null(after.DockerPath);
                break;
            case OptionsCategory.Translation:
                Assert.Equal(TranslationProvider.None, after.TranslationProvider);
                Assert.Equal("claude-haiku-5-5", after.ClaudeModelId);
                break;
            case OptionsCategory.Network:
                Assert.Null(after.HttpProxyUrl);
                Assert.Equal(30, after.RequestTimeoutSeconds);
                break;
            case OptionsCategory.Advanced:
                Assert.Equal(AppLogLevel.Info, after.LogLevel);
                break;
        }
    }

    [Fact]
    public async Task ResetAllAsksFirstAndThenResetsEverythingAndApplies()
    {
        var applied = new List<AppSettings>();
        var dialogs = new FakeDialogService { NextConfirm = false };
        var options = NewOptions(NonDefaultSettings(), applied.Add, dialogs);

        await options.ResetAllCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.ConfirmCount);
        Assert.Empty(applied); // Declined: nothing changed.
        Assert.Equal("5", options.AutosaveMinutesText);

        dialogs.NextConfirm = true;
        await options.ResetAllCommand.ExecuteAsync(null);

        var next = Assert.Single(applied);
        var defaults = new AppSettings();
        Assert.Equal(defaults.UiFontSize, next.UiFontSize);
        Assert.Equal(defaults.AutosaveMinutes, next.AutosaveMinutes);
        Assert.Equal(defaults.ThemeName, next.ThemeName);
        Assert.Equal(defaults.HttpProxyUrl, next.HttpProxyUrl);
        Assert.Equal(defaults.DefaultBuildConfiguration, next.DefaultBuildConfiguration);
        Assert.Empty(next.RecentFiles);
        Assert.Null(next.LastProjectPath);
        Assert.Equal("0", options.AutosaveMinutesText);
        Assert.False(options.HasErrors);
    }

    // ---- key status and tools ----

    [Fact]
    public void KeyStatusShowsSetOrNotSetAndNeverTheValue()
    {
        const string secret = "sk-test-secret-value";
        var environment = new OptionsEnvironment
        {
            GetVariable = name => name == OptionsEnvironment.AnthropicKeyVariable ? secret : null,
        };

        var options = NewOptions(environment: environment);

        Assert.Equal("GOOGLE_TRANSLATE_API_KEY is not set.", options.GoogleKeyStatus);
        Assert.Equal("ANTHROPIC_API_KEY is set.", options.AnthropicKeyStatus);
        Assert.DoesNotContain(secret, options.GoogleKeyStatus + options.AnthropicKeyStatus);
    }

    [Fact]
    public async Task DetectButtonShowsTheVersionFoundForEachTool()
    {
        var environment = new OptionsEnvironment
        {
            Detector = new ToolDetector(
                tool => @"C:\bin\" + tool + ".exe",
                (file, _) => Task.FromResult(new ToolVersionResult(true, "1.2.3", string.Empty)),
                _ => true),
        };
        var options = NewOptions(environment: environment);

        await options.DetectToolsCommand.ExecuteAsync(null);

        Assert.Contains("1.2.3", options.DotnetStatus);
        Assert.Contains("1.2.3", options.NodeStatus);
        Assert.Contains("1.2.3", options.DockerStatus);
        Assert.False(options.IsDetecting);
    }

    [Fact]
    public async Task BrowseFillsThePathFromThePicker()
    {
        var dialogs = new FakeDialogService { NextExecutable = @"C:\tools\node.exe" };
        var options = NewOptions(dialogs: dialogs);

        await options.BrowseNodeCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\tools\node.exe", options.NodePath);
        Assert.Equal(@"C:\tools\node.exe", options.ToSettings().NodePath);
    }

    [Fact]
    public void OpenSettingsFolderUsesTheEnvironment()
    {
        using var folder = new TempFolder();
        string? opened = null;
        var environment = new OptionsEnvironment { SettingsFolder = folder.Path, OpenFolder = p => opened = p };
        var options = NewOptions(environment: environment);

        options.OpenSettingsFolderCommand.Execute(null);

        Assert.Equal(folder.Path, opened);
    }

    // ---- main view model integration ----

    [Fact]
    public async Task OptionsCommandShowsTheDialogAndApplyingStoresTheSettings()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json");
        var dialogs = new FakeDialogService
        {
            OptionsScript = o =>
            {
                o.AutosaveMinutesText = "3";
                o.UiFontSizeText = "18";
                Assert.True(o.Ok());
            },
        };
        var main = new MainViewModel(dialogs, new SettingsStore(path), appearance: null);

        await main.ShowOptionsCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.ShownOptions);
        Assert.Equal(3, main.Settings.AutosaveMinutes);
        var reloaded = new SettingsStore(path).Load();
        Assert.Equal(3, reloaded.AutosaveMinutes);
        Assert.Equal(18, reloaded.UiFontSize);
    }

    [Fact]
    public async Task OpeningAndSavingRecordRecentFilesUpToTheConfiguredLength()
    {
        using var folder = new TempFolder();
        var settingsPath = folder.File("settings.json");
        var seed = new AppSettings { RecentFilesLength = 5 };
        new SettingsStore(settingsPath).Save(seed);
        var dialogs = new FakeDialogService();
        var main = new MainViewModel(dialogs, new SettingsStore(settingsPath), appearance: null);

        for (var i = 0; i < 7; i++)
        {
            dialogs.NextSaveProject = folder.File($"p{i}.xml");
            await main.SaveAsCommand.ExecuteAsync(null);
        }

        Assert.Equal(5, main.Settings.RecentFiles.Count);
        Assert.Equal(folder.File("p6.xml"), main.Settings.RecentFiles[0]);
        Assert.Equal(folder.File("p6.xml"), main.Settings.LastProjectPath);
    }

    [Fact]
    public void ReopenLastProjectOpensItOnlyWhenTheOptionIsOn()
    {
        using var folder = new TempFolder();
        var project = folder.File("shop.xml");
        ProjectFile.Save(new Project { Name = "Shop" }, project);
        var settingsPath = folder.File("settings.json");
        new SettingsStore(settingsPath).Save(new AppSettings { LastProjectPath = project, ReopenLastProject = false });

        var off = new MainViewModel(new FakeDialogService(), new SettingsStore(settingsPath), appearance: null);
        Assert.False(off.ReopenLastProject());
        Assert.Equal("Project1", off.ProjectName);

        new SettingsStore(settingsPath).Save(new AppSettings { LastProjectPath = project, ReopenLastProject = true });
        var on = new MainViewModel(new FakeDialogService(), new SettingsStore(settingsPath), appearance: null);
        Assert.True(on.ReopenLastProject());
        Assert.Equal("Shop", on.ProjectName);
        Assert.False(on.IsDirty);
    }

    [Fact]
    public async Task AutosaveWritesABackupNextToTheProjectAndNeverOverwritesIt()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder, "shop.xml");
        main.ApplySettings(new AppSettings { AutosaveMinutes = 2 }, "ok");
        var original = File.ReadAllText(folder.File("shop.xml"));
        main.ProjectName = "Edited";
        var start = DateTime.UtcNow;

        Assert.Null(main.AutosaveIfDue(start.AddMinutes(1))); // Not due yet.
        var backup = main.AutosaveIfDue(start.AddMinutes(3));

        Assert.Equal(folder.File("shop.autosave.xml"), backup);
        Assert.Equal("Edited", ProjectFile.Load(backup!).Name);
        Assert.Equal(original, File.ReadAllText(folder.File("shop.xml")));
        Assert.True(main.IsDirty);

        // Nothing changed since the last backup: no new write.
        Assert.Null(main.AutosaveIfDue(start.AddMinutes(10)));
    }

    [Fact]
    public async Task AutosaveIsOffByDefaultAndSkippedForUnsavedOrCleanProjects()
    {
        using var folder = new TempFolder();
        var main = await Harness.SavedProjectAsync(folder, "shop.xml");
        main.ProjectName = "Edited";
        Assert.Null(main.AutosaveIfDue(DateTime.UtcNow.AddHours(5))); // Off.

        main.ApplySettings(new AppSettings { AutosaveMinutes = 1 }, "ok");
        var unsaved = new MainViewModel(new FakeDialogService());
        unsaved.ApplySettings(new AppSettings { AutosaveMinutes = 1 }, "ok");
        Assert.Null(unsaved.AutosaveIfDue(DateTime.UtcNow.AddHours(5))); // No project file yet.
    }

    // ---- helpers ----

    private static AppSettings NonDefaultSettings() => new()
    {
        ReopenLastProject = true,
        RecentFilesLength = 15,
        ConfirmOnExit = false,
        AutosaveMinutes = 5,
        SessionTimeoutMinutes = 90,
        ThemeName = "Teal",
        Appearance = AppearanceMode.Dark,
        UiFontSize = 18,
        CompactTables = true,
        CustomStylesPath = @"C:\s.axaml",
        DefaultOutputFolderPattern = "build/out",
        DefaultPageSize = 100,
        OpenOutputAfterGenerate = true,
        ClearOutputBeforeGenerate = true,
        BuildAfterGenerate = true,
        DefaultBuildConfiguration = BuildConfiguration.Release,
        DotnetPath = @"C:\d\dotnet.exe",
        NodePath = @"C:\n\node.exe",
        DockerPath = @"C:\k\docker.exe",
        TranslationProvider = TranslationProvider.Claude,
        ClaudeModelId = "claude-sonnet-5-5",
        HttpProxyUrl = "http://proxy.example.com:8080",
        RequestTimeoutSeconds = 120,
        LogLevel = AppLogLevel.Debug,
    };

    private static OptionsViewModel NewOptions(
        AppSettings? settings = null,
        Action<AppSettings>? apply = null,
        FakeDialogService? dialogs = null,
        OptionsEnvironment? environment = null)
        => new(settings ?? new AppSettings(), apply ?? (_ => { }), dialogs ?? new FakeDialogService(), environment ?? new OptionsEnvironment
        {
            GetVariable = _ => null,
            OpenFolder = _ => { },
        });
}
