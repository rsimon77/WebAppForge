using System.Collections.Generic;
using System.Threading.Tasks;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Services;

/// <summary>The answer to "the open project has unsaved changes". Cancel is the default, so closing the prompt keeps the project.</summary>
public enum UnsavedChangesChoice
{
    Cancel = 0,
    Save,
    Discard,
}

/// <summary>File and folder pickers and modal dialogs. The view layer implements this; view models only call it.</summary>
public interface IDialogService
{
    /// <summary>Returns the local path of a project file to open, or null when cancelled.</summary>
    Task<string?> OpenProjectAsync();

    /// <summary>Returns the local path to save a project to, or null when cancelled.</summary>
    Task<string?> SaveProjectAsync(string? suggestedFileName);

    /// <summary>Returns the local path of a SQLite database file, or null when cancelled.</summary>
    Task<string?> PickSqliteFileAsync(string? startFolder);

    /// <summary>Returns the local path of a folder, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string? startFolder);

    /// <summary>Modal Save, Discard, Cancel prompt shown before a new or different project replaces unsaved changes.</summary>
    Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync();

    /// <summary>Modal Application Options dialog. Returns true when the user closed it with OK.</summary>
    Task<bool> ShowOptionsAsync(OptionsViewModel options);

    /// <summary>Modal yes/no question. Returns true when the user confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>Returns the local path of an executable file, or null when cancelled.</summary>
    Task<string?> PickExecutableAsync(string? startFolder);

    /// <summary>Returns the local path of an AXAML resource dictionary, or null when cancelled.</summary>
    Task<string?> PickXamlFileAsync(string? startFolder);

    /// <summary>Returns the local path of an image file (favicon or logo), or null when cancelled.</summary>
    Task<string?> PickImageFileAsync(string? startFolder);

    /// <summary>Modal list where exactly one item is chosen. Returns null when cancelled.</summary>
    Task<string?> ChooseOneAsync(string title, string message, IReadOnlyList<string> choices);

    /// <summary>Modal list where any number of items are ticked. Returns null when cancelled, otherwise the ticked items.</summary>
    Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices);

    /// <summary>Like <see cref="ChooseManyAsync(string, string, IReadOnlyList{string})"/>, but the items in <paramref name="disabledChoices"/> are shown and cannot be ticked.</summary>
    Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices, IReadOnlyCollection<string> disabledChoices);

    /// <summary>Returns the local path of a CSV file to read, or null when cancelled.</summary>
    Task<string?> PickCsvToOpenAsync(string? startFolder);

    /// <summary>Returns the local path of a CSV file to write, or null when cancelled.</summary>
    Task<string?> PickCsvToSaveAsync(string? suggestedFileName);

    /// <summary>Puts text on the clipboard.</summary>
    Task SetClipboardTextAsync(string text);
}
