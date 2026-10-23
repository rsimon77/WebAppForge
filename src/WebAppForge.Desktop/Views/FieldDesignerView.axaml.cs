using Avalonia.Controls;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class FieldDesignerView : UserControl
{
    public FieldDesignerView()
    {
        InitializeComponent();
    }

    private void OnOptionCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (DataContext is MainViewModel main)
        {
            main.FieldDesigner.RefreshOptionsMessage();
        }
    }
}
