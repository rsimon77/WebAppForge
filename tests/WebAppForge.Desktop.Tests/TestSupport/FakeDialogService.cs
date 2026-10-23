using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Tests.TestSupport;

/// <summary>Returns preset answers instead of showing pickers or dialogs. Counts the dialogs that were asked.</summary>
public sealed class FakeDialogService : IDialogService
{
    public string? NextOpenProject { get; set; }

    public string? NextSaveProject { get; set; }

    public string? NextSqliteFile { get; set; }

    public string? NextFolder { get; set; }

    public string? NextXamlFile { get; set; }

    /// <summary>Answer to the unsaved-changes prompt. Cancel by default, so a test must choose to replace the project.</summary>
    public UnsavedChangesChoice NextUnsavedChoice { get; set; } = UnsavedChangesChoice.Cancel;

    /// <summary>The last Options view model that was shown.</summary>
    public OptionsViewModel? ShownOptions { get; private set; }

    /// <summary>Runs while the Options dialog is "open": a test edits the view model and clicks OK or Apply here.</summary>
    public Action<OptionsViewModel>? OptionsScript { get; set; }

    /// <summary>Answer to confirmation questions (Reset all). No by default.</summary>
    public bool NextConfirm { get; set; }

    public string? NextExecutable { get; set; }

    public int ConfirmCount { get; private set; }

    /// <summary>How many times the unsaved-changes prompt was shown.</summary>
    public int UnsavedPromptCount { get; private set; }

    public Task<string?> OpenProjectAsync() => Task.FromResult(NextOpenProject);

    public Task<string?> SaveProjectAsync(string? suggestedFileName) => Task.FromResult(NextSaveProject);

    public Task<string?> PickSqliteFileAsync(string? startFolder) => Task.FromResult(NextSqliteFile);

    public Task<string?> PickFolderAsync(string? startFolder) => Task.FromResult(NextFolder);

    public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync()
    {
        UnsavedPromptCount++;
        return Task.FromResult(NextUnsavedChoice);
    }

    public Task<bool> ShowOptionsAsync(OptionsViewModel options)
    {
        ShownOptions = options;
        OptionsScript?.Invoke(options);
        return Task.FromResult(true);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        ConfirmCount++;
        LastConfirmTitle = title;
        LastConfirmMessage = message;
        return Task.FromResult(NextConfirm);
    }

    public Task<string?> PickExecutableAsync(string? startFolder) => Task.FromResult(NextExecutable);

    public Task<string?> PickXamlFileAsync(string? startFolder) => Task.FromResult(NextXamlFile);

    public string? NextImageFile { get; set; }

    /// <summary>Answer to the choose-one dialog. Null cancels.</summary>
    public string? NextChoice { get; set; }

    /// <summary>Answer to the choose-many dialog. Null cancels.</summary>
    public IReadOnlyList<string>? NextChoices { get; set; }

    /// <summary>The items the last choose-one or choose-many dialog offered.</summary>
    public IReadOnlyList<string> LastChoicesOffered { get; private set; } = [];

    public string? LastConfirmTitle { get; private set; }

    /// <summary>The message of the last confirmation question.</summary>
    public string? LastConfirmMessage { get; private set; }

    /// <summary>Path answered by the CSV open picker. Null cancels.</summary>
    public string? NextCsvToOpen { get; set; }

    /// <summary>Path answered by the CSV save picker. Null cancels.</summary>
    public string? NextCsvToSave { get; set; }

    /// <summary>Everything that was put on the clipboard, in order.</summary>
    public List<string> ClipboardTexts { get; } = [];

    public Task<string?> PickCsvToOpenAsync(string? startFolder) => Task.FromResult(NextCsvToOpen);

    public Task<string?> PickCsvToSaveAsync(string? suggestedFileName) => Task.FromResult(NextCsvToSave);

    public Task SetClipboardTextAsync(string text)
    {
        ClipboardTexts.Add(text);
        return Task.CompletedTask;
    }

    public Task<string?> PickImageFileAsync(string? startFolder) => Task.FromResult(NextImageFile);

    public Task<string?> ChooseOneAsync(string title, string message, IReadOnlyList<string> choices)
    {
        LastChoicesOffered = choices;
        return Task.FromResult(NextChoice);
    }

    public Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices)
    {
        LastChoicesOffered = choices;
        LastChoicesDisabled = [];
        return Task.FromResult(NextChoices);
    }

    /// <summary>The items the last choose-many dialog showed as disabled (cannot be ticked).</summary>
    public IReadOnlyCollection<string> LastChoicesDisabled { get; private set; } = [];

    public Task<IReadOnlyList<string>?> ChooseManyAsync(string title, string message, IReadOnlyList<string> choices, IReadOnlyCollection<string> disabledChoices)
    {
        LastChoicesOffered = choices;
        LastChoicesDisabled = disabledChoices;
        return Task.FromResult(NextChoices?.Where(c => !disabledChoices.Contains(c)).ToList() as IReadOnlyList<string>);
    }
}
