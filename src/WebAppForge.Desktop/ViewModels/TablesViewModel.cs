using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using WebAppForge.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>The Tables tab (table grid) and the Fields tab (fields of the selected table).</summary>
public partial class TablesViewModel : ViewModelBase
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private TableInfo? _selectedTable;

    [ObservableProperty]
    private FieldInfo? _selectedField;

    public TablesViewModel(MainViewModel main)
    {
        _main = main;
    }

    /// <summary>The page sizes offered for a table's list pages.</summary>
    public static IReadOnlyList<int> PageSizeChoices { get; } = [10, 20, 50, 100];

    /// <summary>Tables of the open project. The items are the same objects as Project.Tables, so edits go straight into the model.</summary>
    public ObservableCollection<TableInfo> Items { get; } = [];

    public ObservableCollection<FieldInfo> Fields { get; } = [];

    public bool HasSelectedTable => SelectedTable is not null;

    /// <summary>
    /// Page size of the selected table, for the page-size box on the Tables tab. Setting it writes
    /// <see cref="TableInfo.Pages"/>.<see cref="PageSettings.PageSize"/> of the selected table.
    /// </summary>
    public int? SelectedPageSize
    {
        get => SelectedTable?.Pages.PageSize;
        set
        {
            if (SelectedTable is not null && value is { } size)
            {
                SetPageSize(SelectedTable, size);
            }
        }
    }

    public string FieldsHeader => SelectedTable is null
        ? "Select a table on the Tables tab to edit its fields."
        : $"Fields of {SelectedTable.Name}";

    /// <summary>Rebuilds the grid from the open project, keeping the selection when the table still exists.</summary>
    public void Reload()
    {
        var previous = SelectedTable?.Name;

        Items.Clear();
        foreach (var table in _main.Current.Tables)
        {
            Items.Add(table);
        }

        SelectedTable = Items.FirstOrDefault(t => t.Name == previous) ?? Items.FirstOrDefault();
        RefreshFields();
    }

    /// <summary>
    /// Writes the owner field picked on the Tables tab. "(none)" (null or the label) clears it. The security
    /// validation is recomputed so the message and the Generate gate follow the change.
    /// </summary>
    public void SetOwnerField(TableInfo table, string? choice)
    {
        ArgumentNullException.ThrowIfNull(table);

        var value = OwnerFieldPicker.Value(choice);
        if (table.OwnerField == value)
        {
            return;
        }

        table.OwnerField = value;
        _main.Security.Refresh();
    }

    /// <summary>
    /// Writes the page size chosen for a table. Only the offered sizes are accepted; anything else keeps the last value
    /// and shows a message. Returns true when the value was stored.
    /// </summary>
    public bool SetPageSize(TableInfo table, int size)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!PageSizeChoices.Contains(size))
        {
            _main.Status = "Page size must be 10, 20, 50 or 100. The last valid value was kept.";
            return false;
        }

        table.Pages.PageSize = size;
        OnPropertyChanged(nameof(SelectedPageSize));
        return true;
    }

    /// <summary>
    /// Writes the view level typed on the Tables tab. Only whole numbers 0 or more reach the model. Anything else
    /// shows a message in the status line and keeps the last valid value. Returns true when the value was stored.
    /// </summary>
    public bool SetViewLevel(TableInfo table, string? text)
    {
        ArgumentNullException.ThrowIfNull(table);
        return ApplyLevel(text, "View level", n => table.ViewMinLevel = n);
    }

    /// <summary>Same as <see cref="SetViewLevel"/> for the edit level.</summary>
    public bool SetEditLevel(TableInfo table, string? text)
    {
        ArgumentNullException.ThrowIfNull(table);
        return ApplyLevel(text, "Edit level", n => table.EditMinLevel = n);
    }

    /// <summary>True for a whole number 0 or more. Level 0 means any signed-in user.</summary>
    public static bool TryParseLevel(string? text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private bool ApplyLevel(string? text, string label, Action<int> store)
    {
        if (TryParseLevel(text, out var value))
        {
            store(value);
            return true;
        }

        _main.Status = $"{label} must be a whole number, 0 or more. The last valid value was kept.";
        return false;
    }

    partial void OnSelectedTableChanged(TableInfo? value)
    {
        RefreshFields();
        OnPropertyChanged(nameof(HasSelectedTable));
        OnPropertyChanged(nameof(SelectedPageSize));
        _main.RefreshBreadcrumb();
    }

    partial void OnSelectedFieldChanged(FieldInfo? value) => _main.RefreshBreadcrumb();

    private void RefreshFields()
    {
        SelectedField = null;
        Fields.Clear();
        if (SelectedTable is not null)
        {
            foreach (var field in SelectedTable.Fields)
            {
                Fields.Add(field);
            }
        }

        OnPropertyChanged(nameof(FieldsHeader));
    }
}
