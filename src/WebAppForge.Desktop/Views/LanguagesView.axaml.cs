using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

/// <summary>The Languages tab. The language columns of the translations grid depend on the project, so they are built here.</summary>
public partial class LanguagesView : UserControl
{
    /// <summary>The key and source text columns declared in the XAML; every column after them is a language.</summary>
    private const int FixedColumns = 2;

    private LanguagesViewModel? _subscribed;

    public LanguagesView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.ColumnsChanged -= OnColumnsChanged;
            }

            _subscribed = (DataContext as MainViewModel)?.Languages;
            if (_subscribed is not null)
            {
                _subscribed.ColumnsChanged += OnColumnsChanged;
                BuildColumns();
            }
        };

        TranslationsGrid.SelectionChanged += (_, _) => _subscribed?.SetSelectedRows(TranslationsGrid.SelectedItems.OfType<TranslationRow>());
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => BuildColumns();

    private void BuildColumns()
    {
        if (_subscribed is null)
        {
            return;
        }

        var columns = TranslationsGrid.Columns;
        while (columns.Count > FixedColumns)
        {
            columns.RemoveAt(columns.Count - 1);
        }

        var template = (IDataTemplate)Resources["TranslationCellTemplate"]!;
        foreach (var language in _subscribed.VisibleLanguages)
        {
            var code = language;
            columns.Add(new DataGridTemplateColumn
            {
                Header = code,
                Width = new DataGridLength(300),
                MinWidth = 180,
                CellTemplate = new FuncDataTemplate<TranslationRow>(
                    (row, _) => row is null ? null : new ContentControl { Content = row.CellFor(code), ContentTemplate = template }),
            });
        }
    }
}
