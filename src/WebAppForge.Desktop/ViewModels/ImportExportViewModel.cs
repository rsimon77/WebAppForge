using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WebAppForge.Core.Generation;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The Import/Export tab: the file formats and options of the generated site (<see cref="Project.ImportExport"/>) and which
/// tables export and import (<see cref="TableInfo.ImportExport"/>). Edits write straight into the open project, so Save persists them.
/// </summary>
public partial class ImportExportViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private bool _enabled;
    private string _maxUploadText = ImportExportSettings.DefaultMaxUploadMb.ToString(CultureInfo.InvariantCulture);
    private string _csvDelimiter = ",";
    private HeaderStyle _headerStyle = HeaderStyle.Caption;
    private bool _includeHeader = true;
    private string _dateFormat = ImportExportSettings.DefaultDateFormat;
    private string _fileNamePattern = ImportExportSettings.DefaultFileNamePattern;
    private string _exportMinLevelText = "0";
    private string _importMinLevelText = "0";
    private string? _inputMessage;
    private string? _validationMessage;

    public ImportExportViewModel(MainViewModel main)
    {
        _main = main;
        Formats = [.. new[] { DataFormats.Csv, DataFormats.Xlsx, DataFormats.Json, DataFormats.Xml, DataFormats.Word }.Select(f => new FormatChoice(f, this))];
    }

    /// <summary>One row per file format, with an export and an import check box.</summary>
    public IReadOnlyList<FormatChoice> Formats { get; }

    public IReadOnlyList<HeaderStyle> HeaderStyleChoices { get; } = Enum.GetValues<HeaderStyle>();

    public IReadOnlyList<ImportMode> ImportModeChoices { get; } = Enum.GetValues<ImportMode>();

    /// <summary>One row per generated table.</summary>
    public ObservableCollection<TableImportExportRow> Rows { get; } = [];

    internal ImportExportSettings Model => _main.Current.ImportExport;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                Model.Enabled = value;
                Refresh();
            }
        }
    }

    public string MaxUploadText
    {
        get => _maxUploadText;
        set => ApplyNumber(ref _maxUploadText, value, 1, 100, v => Model.MaxUploadMb = v, "The upload limit must be a whole number of megabytes from 1 to 100.");
    }

    /// <summary>One character, or "tab".</summary>
    public string CsvDelimiter
    {
        get => _csvDelimiter;
        set
        {
            if (SetProperty(ref _csvDelimiter, value ?? string.Empty))
            {
                Model.CsvDelimiter = _csvDelimiter;
                Refresh();
            }
        }
    }

    public HeaderStyle HeaderStyle
    {
        get => _headerStyle;
        set
        {
            if (SetProperty(ref _headerStyle, value))
            {
                Model.HeaderStyle = value;
            }
        }
    }

    public bool IncludeHeader
    {
        get => _includeHeader;
        set
        {
            if (SetProperty(ref _includeHeader, value))
            {
                Model.IncludeHeader = value;
            }
        }
    }

    public string DateFormat
    {
        get => _dateFormat;
        set
        {
            if (SetProperty(ref _dateFormat, value ?? string.Empty))
            {
                Model.DateFormat = _dateFormat.Trim();
                Refresh();
            }
        }
    }

    /// <summary>The export file name without extension; {table}, {date} and {datetime} are replaced.</summary>
    public string FileNamePattern
    {
        get => _fileNamePattern;
        set
        {
            if (SetProperty(ref _fileNamePattern, value ?? string.Empty))
            {
                Model.ExportFileNamePattern = _fileNamePattern.Trim();
                Refresh();
            }
        }
    }

    public string ExportMinLevelText
    {
        get => _exportMinLevelText;
        set => ApplyNumber(ref _exportMinLevelText, value, 0, int.MaxValue, v => Model.ExportMinLevel = v, "The lowest level for export must be zero or a positive whole number.");
    }

    public string ImportMinLevelText
    {
        get => _importMinLevelText;
        set => ApplyNumber(ref _importMinLevelText, value, 0, int.MaxValue, v => Model.ImportMinLevel = v, "The lowest level for import must be zero or a positive whole number.");
    }

    /// <summary>Message about the last rejected number input, or null.</summary>
    public string? InputMessage
    {
        get => _inputMessage;
        private set
        {
            if (SetProperty(ref _inputMessage, value))
            {
                OnPropertyChanged(nameof(HasInputMessage));
            }
        }
    }

    public bool HasInputMessage => InputMessage is not null;

    /// <summary>The first problem the generator would reject, or null. Only set while import and export are enabled.</summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
                _main.Security.Refresh();
            }
        }
    }

    public bool HasValidationMessage => ValidationMessage is not null;

    /// <summary>The first problem with the import and export settings, or null when the feature is off or the settings are fine.</summary>
    public static string? Validate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return ImportExportRules.Problems(project).FirstOrDefault();
    }

    /// <summary>Copies the settings of the open project into the tab. Called whenever a project is loaded.</summary>
    public void LoadFrom(Project project)
    {
        var settings = project.ImportExport;
        Enabled = settings.Enabled;
        MaxUploadText = settings.MaxUploadMb.ToString(CultureInfo.InvariantCulture);
        CsvDelimiter = settings.CsvDelimiter;
        HeaderStyle = settings.HeaderStyle;
        IncludeHeader = settings.IncludeHeader;
        DateFormat = settings.DateFormat;
        FileNamePattern = settings.ExportFileNamePattern;
        ExportMinLevelText = settings.ExportMinLevel.ToString(CultureInfo.InvariantCulture);
        ImportMinLevelText = settings.ImportMinLevel.ToString(CultureInfo.InvariantCulture);
        foreach (var format in Formats)
        {
            format.Reload();
        }

        InputMessage = null;
        Rows.Clear();
        Refresh();
    }

    /// <summary>Rebuilds the table rows from the open project and recomputes the validation message.</summary>
    public void Refresh()
    {
        var tables = _main.Current.Tables.Where(t => t.Generate).ToList();
        if (!Rows.Select(r => r.Table).SequenceEqual(tables))
        {
            Rows.Clear();
            foreach (var table in tables)
            {
                Rows.Add(new TableImportExportRow(table, this));
            }
        }

        ValidationMessage = Validate(_main.Current);
    }

    internal void Reject(string message)
    {
        InputMessage = message;
        _main.Status = message;
    }

    private void ApplyNumber(ref string field, string? value, int min, int max, Action<int> store, string message, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value ?? string.Empty, name))
        {
            return;
        }

        if (int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max)
        {
            store(number);
            InputMessage = null;
            Refresh();
            return;
        }

        Reject(message + " The last valid value was kept.");
    }
}

/// <summary>One file format of the tab: whether the site exports it and whether it imports it.</summary>
public sealed partial class FormatChoice : ObservableObject
{
    private readonly ImportExportViewModel _owner;

    public FormatChoice(DataFormats format, ImportExportViewModel owner)
    {
        Format = format;
        _owner = owner;
    }

    public DataFormats Format { get; }

    public string Name => Format switch
    {
        DataFormats.Xlsx => "Excel (.xlsx)",
        DataFormats.Word => "Word (.docx)",
        _ => Format.ToString().ToUpperInvariant(),
    };

    public bool Export
    {
        get => _owner.Model.ExportFormats.HasFlag(Format);
        set => Set(Export, value, v => _owner.Model.ExportFormats = v ? _owner.Model.ExportFormats | Format : _owner.Model.ExportFormats & ~Format);
    }

    public bool Import
    {
        get => _owner.Model.ImportFormats.HasFlag(Format);
        set => Set(Import, value, v => _owner.Model.ImportFormats = v ? _owner.Model.ImportFormats | Format : _owner.Model.ImportFormats & ~Format);
    }

    /// <summary>Raises the change of both boxes after a project load.</summary>
    public void Reload()
    {
        OnPropertyChanged(nameof(Export));
        OnPropertyChanged(nameof(Import));
    }

    private void Set(bool current, bool value, Action<bool> store, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value)
        {
            return;
        }

        store(value);
        OnPropertyChanged(name);
        _owner.Refresh();
    }
}

/// <summary>One table of the import/export grid. Edits write into <see cref="TableInfo.ImportExport"/>.</summary>
public sealed partial class TableImportExportRow : ObservableObject
{
    private readonly ImportExportViewModel _owner;
    private string _exportMaxRowsText;

    public TableImportExportRow(TableInfo table, ImportExportViewModel owner)
    {
        Table = table;
        _owner = owner;
        _exportMaxRowsText = table.ImportExport.ExportMaxRows.ToString(CultureInfo.InvariantCulture);
    }

    public TableInfo Table { get; }

    public string Name => Table.Name;

    /// <summary>The import modes, for the picker of the row.</summary>
    public IReadOnlyList<ImportMode> ModeChoices { get; } = Enum.GetValues<ImportMode>();

    public bool Export
    {
        get => Table.ImportExport.Export;
        set => Set(Table.ImportExport.Export, value, v => Table.ImportExport.Export = v);
    }

    public bool Import
    {
        get => Table.ImportExport.Import;
        set => Set(Table.ImportExport.Import, value, v => Table.ImportExport.Import = v);
    }

    public ImportMode ImportMode
    {
        get => Table.ImportExport.ImportMode;
        set
        {
            if (Table.ImportExport.ImportMode == value)
            {
                return;
            }

            Table.ImportExport.ImportMode = value;
            OnPropertyChanged();
            _owner.Refresh();
        }
    }

    /// <summary>Fields kept out of export and import, comma separated.</summary>
    public string ExcludedText
    {
        get => string.Join(", ", Table.ImportExport.ExcludedFields);
        set
        {
            var names = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.SequenceEqual(Table.ImportExport.ExcludedFields))
            {
                return;
            }

            Table.ImportExport.ExcludedFields.Clear();
            Table.ImportExport.ExcludedFields.AddRange(names);
            OnPropertyChanged();
            _owner.Refresh();
        }
    }

    /// <summary>The most rows an export writes; 0 means all.</summary>
    public string ExportMaxRowsText
    {
        get => _exportMaxRowsText;
        set
        {
            if (!SetProperty(ref _exportMaxRowsText, value ?? string.Empty))
            {
                return;
            }

            if (int.TryParse(_exportMaxRowsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows) && rows >= 0)
            {
                Table.ImportExport.ExportMaxRows = rows;
                _owner.Refresh();
                return;
            }

            _owner.Reject($"The most export rows of '{Name}' must be zero (all) or a positive whole number. The last valid value was kept.");
        }
    }

    private void Set(bool current, bool value, Action<bool> store, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value)
        {
            return;
        }

        store(value);
        OnPropertyChanged(name);
        _owner.Refresh();
    }
}
