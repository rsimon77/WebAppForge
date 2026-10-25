using System;
using WebAppForge.Core.Build;
using CommunityToolkit.Mvvm.ComponentModel;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>State of one stage in the stage strip.</summary>
public enum StageState
{
    Pending,
    Running,
    Ok,
    Failed,
    Skipped,
    Cancelled,
}

/// <summary>One stage of the strip: Generate, Docker files, Build, Publish or Docker image.</summary>
public sealed class StageItem : ObservableObject
{
    private StageState _state = StageState.Pending;
    private string _message = string.Empty;

    public StageItem(BuildStage stage, string title)
    {
        Stage = stage;
        Title = title;
    }

    public BuildStage Stage { get; }

    public string Title { get; }

    public StageState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsOk));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsSkipped));
                OnPropertyChanged(nameof(IsCancelled));
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    /// <summary>Detail of the stage, such as the number of files or the first error.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    public bool IsPending => _state == StageState.Pending;

    public bool IsRunning => _state == StageState.Running;

    public bool IsOk => _state == StageState.Ok;

    public bool IsFailed => _state == StageState.Failed;

    public bool IsSkipped => _state == StageState.Skipped;

    public bool IsCancelled => _state == StageState.Cancelled;

    public string Tooltip => _message.Length == 0 ? $"{Title}: {_state}" : $"{Title}: {_state}. {_message}";
}

/// <summary>How a console line is coloured.</summary>
public enum OutputKind
{
    Normal,
    Warning,
    Error,
}

/// <summary>One line of the build console.</summary>
public sealed record OutputLineItem(string Text, OutputKind Kind, DateTimeOffset Timestamp)
{
    public bool IsError => Kind == OutputKind.Error;

    public bool IsWarning => Kind == OutputKind.Warning;
}

/// <summary>One row of the diagnostics grid. <see cref="Severity"/> is "Error" or "Warning"; the grid shows <see cref="SeverityText"/>.</summary>
public sealed record DiagnosticRow(string Severity, string Code, string Message, string File, int Line)
{
    public const string Error = "Error";
    public const string Warning = "Warning";

    public bool IsError => Severity == Error;

    /// <summary>The severity in the UI language.</summary>
    public string SeverityText => Localizer.Instance[IsError ? "Generate_SeverityError" : "Generate_SeverityWarning"];

    /// <summary>"path:line", the text double-click copies. Without a line it is the path alone.</summary>
    public string Location => Line > 0 ? $"{File}:{Line}" : File;
}
