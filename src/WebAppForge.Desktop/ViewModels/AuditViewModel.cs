using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.Localization;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>
/// The Audit trail tab: the log table and options of the generated site (<see cref="Project.Audit"/>) and which tables
/// are audited (<see cref="TableInfo.Audit"/>). Edits write straight into the open project, so Save persists them.
/// </summary>
public partial class AuditViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    private bool _enabled;
    private string _logTable = AuditSettings.DefaultLogTable;
    private bool _createTable = true;
    private AuditValues _values = AuditValues.Changed;
    private bool _recordIpAddress;
    private bool _showLogPage = true;
    private string _logPageMinLevelText = "0";
    private string? _inputMessage;
    private string? _validationMessage;

    public AuditViewModel(MainViewModel main)
    {
        _main = main;
    }

    /// <summary>The values an entry can keep, for the picker.</summary>
    public IReadOnlyList<AuditValues> ValueChoices { get; } = Enum.GetValues<AuditValues>();

    /// <summary>One row per generated table.</summary>
    public ObservableCollection<TableAuditRow> Rows { get; } = [];

    private AuditSettings Model => _main.Current.Audit;

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

    public string LogTable
    {
        get => _logTable;
        set
        {
            if (SetProperty(ref _logTable, value ?? string.Empty))
            {
                Model.LogTable = _logTable.Trim();
                Refresh();
            }
        }
    }

    public bool CreateTable
    {
        get => _createTable;
        set
        {
            if (SetProperty(ref _createTable, value))
            {
                Model.CreateTable = value;
            }
        }
    }

    public AuditValues Values
    {
        get => _values;
        set
        {
            if (SetProperty(ref _values, value))
            {
                Model.Values = value;
            }
        }
    }

    public bool RecordIpAddress
    {
        get => _recordIpAddress;
        set
        {
            if (SetProperty(ref _recordIpAddress, value))
            {
                Model.RecordIpAddress = value;
            }
        }
    }

    public bool ShowLogPage
    {
        get => _showLogPage;
        set
        {
            if (SetProperty(ref _showLogPage, value))
            {
                Model.ShowLogPage = value;
            }
        }
    }

    public string LogPageMinLevelText
    {
        get => _logPageMinLevelText;
        set
        {
            if (!SetProperty(ref _logPageMinLevelText, value ?? string.Empty))
            {
                return;
            }

            if (int.TryParse(_logPageMinLevelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level >= 0)
            {
                Model.LogPageMinLevel = level;
                InputMessage = null;
                return;
            }

            InputMessage = Localizer.Instance["Audit_TheLowestLevelForTheLog"];
            _main.Status = InputMessage;
        }
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

    /// <summary>The first problem the generator would reject, or null. Only set while the audit trail is enabled.</summary>
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

    /// <summary>
    /// The first problem with the audit settings: no log table name, or a table that excludes a field it does not have.
    /// Null when the trail is disabled or the settings are fine. A project table named like the log table is left to the
    /// generator, which accepts a table it created earlier.
    /// </summary>
    public static string? Validate(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!project.Audit.Enabled)
        {
            return null;
        }

        if (project.Audit.LogTable.Trim().Length == 0)
        {
            return Localizer.Instance["Audit_TheAuditTrailNeedsTheName"];
        }

        foreach (var table in project.Tables.Where(t => t.Generate && t.Audit.Enabled))
        {
            foreach (var field in table.Audit.ExcludedFields)
            {
                if (!table.Fields.Any(f => string.Equals(f.Name, field.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    return Localizer.Instance.Format("Audit_TableExcludesTheFieldFromThe", table.Name, field);
                }
            }
        }

        return null;
    }

    /// <summary>Copies the audit settings of the open project into the tab. Called whenever a project is loaded.</summary>
    public void LoadFrom(Project project)
    {
        var audit = project.Audit;
        Enabled = audit.Enabled;
        LogTable = audit.LogTable;
        CreateTable = audit.CreateTable;
        Values = audit.Values;
        RecordIpAddress = audit.RecordIpAddress;
        ShowLogPage = audit.ShowLogPage;
        LogPageMinLevelText = audit.LogPageMinLevel.ToString(CultureInfo.InvariantCulture);
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
                Rows.Add(new TableAuditRow(table, this));
            }
        }

        ValidationMessage = Validate(_main.Current);
    }
}

/// <summary>One table of the audit grid. Edits write into <see cref="TableInfo.Audit"/>.</summary>
public sealed partial class TableAuditRow : ObservableObject
{
    private readonly AuditViewModel _owner;

    public TableAuditRow(TableInfo table, AuditViewModel owner)
    {
        Table = table;
        _owner = owner;
    }

    public TableInfo Table { get; }

    public string Name => Table.Name;

    public bool Enabled
    {
        get => Table.Audit.Enabled;
        set => Set(Table.Audit.Enabled, value, v => Table.Audit.Enabled = v);
    }

    public bool Inserts
    {
        get => Table.Audit.Inserts;
        set => Set(Table.Audit.Inserts, value, v => Table.Audit.Inserts = v);
    }

    public bool Updates
    {
        get => Table.Audit.Updates;
        set => Set(Table.Audit.Updates, value, v => Table.Audit.Updates = v);
    }

    public bool Deletes
    {
        get => Table.Audit.Deletes;
        set => Set(Table.Audit.Deletes, value, v => Table.Audit.Deletes = v);
    }

    /// <summary>Fields kept out of the trail, comma separated.</summary>
    public string ExcludedText
    {
        get => string.Join(", ", Table.Audit.ExcludedFields);
        set
        {
            var names = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.SequenceEqual(Table.Audit.ExcludedFields))
            {
                return;
            }

            Table.Audit.ExcludedFields.Clear();
            Table.Audit.ExcludedFields.AddRange(names);
            OnPropertyChanged();
            _owner.Refresh();
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
