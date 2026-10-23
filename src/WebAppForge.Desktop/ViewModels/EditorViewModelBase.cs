using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// Shared plumbing of the customization view models: a message for the last bad input, text boxes that keep what was
/// typed while it is invalid (the model keeps the last valid value), and whole-number parsing with a range.
/// </summary>
public abstract class EditorViewModelBase : ViewModelBase
{
    private readonly Dictionary<string, string> _rawText = [];
    private string _message = string.Empty;

    /// <summary>Explains why the last input was refused. Empty after a valid input.</summary>
    public string Message
    {
        get => _message;
        protected set
        {
            if (SetProperty(ref _message, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => _message.Length > 0;

    /// <summary>Called after any value changed. The page designer redraws its preview here.</summary>
    protected virtual void Changed()
    {
    }

    /// <summary>The text to show: what the user typed while it is invalid, otherwise the model value.</summary>
    protected string Shown(string name, string modelText)
        => _rawText.TryGetValue(name, out var raw) ? raw : modelText;

    protected void ForgetRawText() => _rawText.Clear();

    /// <summary>Remembers the typed text; the model changes only when <paramref name="error"/> is null.</summary>
    protected bool Accept(string name, string text, string? error, Action store)
    {
        _rawText[name] = text;
        Message = error ?? string.Empty;
        if (error is not null)
        {
            OnPropertyChanged(name);
            return false;
        }

        store();
        _rawText.Remove(name);
        OnPropertyChanged(name);
        Changed();
        return true;
    }

    protected static bool TryWholeNumber(string? text, int min, int max, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    protected static string RangeMessage(string label, int min, int max)
        => $"{label} must be a whole number from {min} to {max}. The last valid value was kept.";

    /// <summary>Writes <paramref name="value"/> through <paramref name="store"/> when it differs from <paramref name="current"/>.</summary>
    protected bool Put<T>(T current, T value, Action<T> store, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        store(value);
        OnPropertyChanged(name);
        Changed();
        return true;
    }
}
