using System;
using System.Collections.Generic;
using System.Linq;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One language of the site in the languages list.</summary>
public sealed class LanguageRow
{
    public LanguageRow(string code, bool isDefault)
    {
        Code = code;
        IsDefault = isDefault;
        var known = LanguageCatalog.Find(code);
        EnglishName = known?.EnglishName ?? string.Empty;
        NativeName = known?.NativeName ?? string.Empty;
        IsRightToLeft = LanguageCatalog.IsRightToLeft(code);
    }

    public string Code { get; }

    public bool IsDefault { get; }

    public string EnglishName { get; }

    public string NativeName { get; }

    public bool IsRightToLeft { get; }

    /// <summary>"RTL" for right-to-left languages, otherwise empty.</summary>
    public string RightToLeftMarker => IsRightToLeft ? "RTL" : string.Empty;

    public string DefaultMarker => IsDefault ? "default" : string.Empty;

    public bool CanRemove => !IsDefault;

    /// <summary>"hu - Hungarian (magyar)", or just the code for a language that is not in the catalog.</summary>
    public string Display => EnglishName.Length == 0 ? Code : $"{Code} - {EnglishName} ({NativeName})";

    public override string ToString() => Display;
}

/// <summary>
/// One row of the translations grid: a table caption, field caption or fixed UI string with its source text, and one
/// <see cref="TranslationCell"/> per language.
/// </summary>
public sealed class TranslationRow : ObservableObject
{
    private readonly Dictionary<string, TranslationCell> _cells = new(StringComparer.OrdinalIgnoreCase);

    public TranslationRow(string key, string sourceText, string sourceLanguage, IEnumerable<string> languages, LocalizationSettings settings)
    {
        Key = key;
        SourceText = sourceText;
        SourceLanguage = sourceLanguage;
        foreach (var language in languages)
        {
            _cells[language] = new TranslationCell(this, language, settings);
        }
    }

    public string Key { get; }

    public string SourceText { get; }

    /// <summary>The language the source text is written in (English for the fixed UI strings, the default language for captions).</summary>
    public string SourceLanguage { get; }

    /// <summary>"Table", "Field" or "UI".</summary>
    public string Kind => Key.StartsWith("table.", StringComparison.Ordinal) ? "Table"
        : Key.StartsWith("field.", StringComparison.Ordinal) ? "Field" : "UI";

    public IReadOnlyCollection<TranslationCell> Cells => _cells.Values;

    /// <summary>The cell of a language (compared without case), or null.</summary>
    public TranslationCell? CellFor(string language) => _cells.TryGetValue(language, out var cell) ? cell : null;

    public void Refresh()
    {
        foreach (var cell in _cells.Values)
        {
            cell.Refresh();
        }
    }
}

/// <summary>
/// The translation of one key into one language. Editing the text stores a manual, reviewed entry (and remembers which
/// source text it was made from); clearing the text removes the entry. The cell of the source language shows the source
/// text and cannot be edited.
/// </summary>
public sealed class TranslationCell : ObservableObject
{
    private readonly LocalizationSettings _settings;

    public TranslationCell(TranslationRow row, string language, LocalizationSettings settings)
    {
        Row = row;
        Language = language;
        _settings = settings;
    }

    public TranslationRow Row { get; }

    public string Language { get; }

    /// <summary>True when this column is the language the source text is written in.</summary>
    public bool IsSource => string.Equals(Language, Row.SourceLanguage, StringComparison.OrdinalIgnoreCase);

    public bool CanEdit => !IsSource;

    public TranslationEntry? Entry => IsSource ? null : _settings.Find(Row.Key, Language);

    public string Text
    {
        get => IsSource ? Row.SourceText : Entry?.Text ?? string.Empty;
        set
        {
            if (IsSource)
            {
                return;
            }

            var text = value ?? string.Empty;
            if (text == (Entry?.Text ?? string.Empty))
            {
                return;
            }

            if (text.Length == 0)
            {
                _settings.Entries.RemoveAll(e => e.Key == Row.Key && string.Equals(e.Language, Language, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var entry = _settings.SetManual(Row.Key, Language, text);
                entry.SourceHash = TranslationEntry.HashOf(Row.SourceText);
            }

            Refresh();
        }
    }

    public bool Reviewed
    {
        get => Entry?.Reviewed ?? false;
        set
        {
            if (Entry is { } entry && entry.Reviewed != value)
            {
                entry.Reviewed = value;
                Refresh();
            }
        }
    }

    public bool HasEntry => Entry is not null;

    /// <summary>No translation yet (not for the source language).</summary>
    public bool IsMissing => !IsSource && Entry is null;

    /// <summary>A translation exists but nobody approved it.</summary>
    public bool IsUnreviewed => Entry is { Reviewed: false };

    /// <summary>The source text changed after this translation was made.</summary>
    public bool IsStale => Entry is { SourceHash.Length: > 0 } entry && entry.SourceHash != TranslationEntry.HashOf(Row.SourceText);

    /// <summary>"Manual", "Google" or "Claude" for an existing translation, otherwise empty.</summary>
    public string SourceLabel => Entry?.Source.ToString() ?? string.Empty;

    public void Refresh() => OnPropertyChanged(string.Empty);
}
