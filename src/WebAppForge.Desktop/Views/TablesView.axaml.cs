using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class TablesView : UserControl
{
    public TablesView()
    {
        InitializeComponent();
    }

    /// <summary>Sets the muted or warning row class when the grid loads a row (rows are reused as the grid scrolls).</summary>
    private void OnRowPrepared(object? sender, DataGridRowEventArgs e) => ApplyRowCategory(e.Row);

    /// <summary>Generate is a checkbox: after an edit the row categories are recalculated.</summary>
    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
            {
                ApplyRowCategory(row);
            }
        }
    }

    /// <summary>
    /// The tables are plain model objects, so after an edit in the inspector the grid row of the table is bound again to show
    /// the new caption, pages and category.
    /// </summary>
    private void OnInspectorEdited(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: TableInfo table })
        {
            return;
        }

        foreach (var row in TablesGrid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.DataContext == table))
        {
            row.DataContext = null;
            row.DataContext = table;
            ApplyRowCategory(row);
        }
    }

    private static void ApplyRowCategory(DataGridRow row)
    {
        if (row.DataContext is not TableInfo table)
        {
            return;
        }

        var category = TableRowRules.Categorize(table);
        row.Classes.Set("row-muted", category == TableRowCategory.NotGenerated);
        row.Classes.Set("row-warning", category == TableRowCategory.NoPrimaryKey);
    }

    /// <summary>Writes the picked owner field of the row's table. Ignores the empty selection that the grid sets while it builds rows.</summary>
    private void OnOwnerFieldSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: TableInfo table } combo
            && combo.SelectedItem is string choice
            && DataContext is MainViewModel main)
        {
            main.Tables.SetOwnerField(table, choice);
        }
    }

    /// <summary>
    /// Writes the view level typed in the row's text box. Only edits made while the box has focus are read, so the
    /// text the grid sets while it builds rows does not trigger a message.
    /// </summary>
    private void OnViewLevelTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsFocused: true, DataContext: TableInfo table } box
            && DataContext is MainViewModel main)
        {
            main.Tables.SetViewLevel(table, box.Text);
        }
    }

    /// <summary>Same as <see cref="OnViewLevelTextChanged"/> for the edit level.</summary>
    private void OnEditLevelTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsFocused: true, DataContext: TableInfo table } box
            && DataContext is MainViewModel main)
        {
            main.Tables.SetEditLevel(table, box.Text);
        }
    }
}
