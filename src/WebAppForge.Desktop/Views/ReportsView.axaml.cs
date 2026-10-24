using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class ReportsView : UserControl
{
    private ReportsViewModel? _model;

    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
            }

            _model = (DataContext as MainViewModel)?.Reports;
            if (_model is not null)
            {
                _model.PropertyChanged += OnModelChanged;
                RebuildPreviewColumns();
            }
        };
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReportsViewModel.PreviewColumns) or "")
        {
            RebuildPreviewColumns();
        }
    }

    /// <summary>The preview result has the columns the report returned, so the grid's columns are rebuilt for each run.</summary>
    private void RebuildPreviewColumns()
    {
        PreviewGrid.Columns.Clear();
        var columns = _model?.PreviewColumns ?? [];
        for (var i = 0; i < columns.Count; i++)
        {
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Binding($"Cells[{i}]"),
                IsReadOnly = true,
                MinWidth = 100,
                Width = DataGridLength.Auto,
            });
        }
    }
}
