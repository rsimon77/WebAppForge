using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Services;

/// <summary>Implements the pickers with Avalonia's StorageProvider (Windows file and folder dialogs).</summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType ProjectFiles = new("WebAppForge project")
    {
        Patterns = ["*.xml"],
    };

    private static readonly FilePickerFileType SqliteFiles = new("SQLite database")
    {
        Patterns = ["*.db", "*.sqlite", "*.sqlite3", "*.db3"],
    };

    private static readonly FilePickerFileType XamlFiles = new("Avalonia resource dictionary")
    {
        Patterns = ["*.axaml", "*.xaml"],
    };

    private static readonly FilePickerFileType ImageFiles = new("Image")
    {
        Patterns = ["*.ico", "*.png", "*.svg", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
    };

    private static readonly FilePickerFileType CsvFiles = new("CSV file")
    {
        Patterns = ["*.csv"],
    };

    public async Task<string?> PickCsvToOpenAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import translations",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder),
            FileTypeFilter = [CsvFiles, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickCsvToSaveAsync(string? suggestedFileName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export translations",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = [CsvFiles],
        });

        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (owner.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync()
        => new UnsavedChangesWindow().ShowDialog<UnsavedChangesChoice>(owner);

    public Task<bool> ShowOptionsAsync(OptionsViewModel options)
        => new OptionsWindow { DataContext = options }.ShowDialog<bool>(owner);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
        => new ConfirmWindow(title, message, confirmText).ShowDialog<bool>(owner);

    public Task<string?> ChooseOneAsync(string title, string message, IReadOnlyList<string> choices)
        => new ChoiceWindow(title, message, choices, multiple: false).ShowDialog<string?>(owner);

    public async Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices)
        => await new ChoiceWindow(title, message, choices, multiple: true).ShowDialog<IReadOnlyList<string>?>(owner);

    public async Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices, IReadOnlyCollection<string> disabledChoices)
        => await new ChoiceWindow(title, message, choices, multiple: true, disabledChoices).ShowDialog<IReadOnlyList<string>?>(owner);

    public async Task<string?> PickImageFileAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose image",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder),
            FileTypeFilter = [ImageFiles, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickExecutableAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose executable",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder is null ? null : Path.GetDirectoryName(startFolder)),
            FileTypeFilter = [FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickXamlFileAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose custom styles",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder is null ? null : Path.GetDirectoryName(startFolder)),
            FileTypeFilter = [XamlFiles, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> OpenProjectAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = [ProjectFiles, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveProjectAsync(string? suggestedFileName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save project as",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xml",
            FileTypeChoices = [ProjectFiles],
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickSqliteFileAsync(string? startFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose SQLite database",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder),
            FileTypeFilter = [SqliteFiles, FilePickerFileTypes.All],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string? startFolder)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose output folder",
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(startFolder),
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        return await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(path)));
    }
}
