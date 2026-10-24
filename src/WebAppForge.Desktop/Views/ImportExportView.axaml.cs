using Avalonia.Controls;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class ImportExportView : UserControl
{
    public ImportExportView()
    {
        InitializeComponent();

        // Generate flags on the Tables tab can change without notice, so the rows are rebuilt when this tab shows.
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel main)
            {
                main.ImportExport.Refresh();
            }
        };
    }
}
