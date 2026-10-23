using Avalonia.Controls;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class SecurityView : UserControl
{
    public SecurityView()
    {
        InitializeComponent();

        // Generate flags on the Tables tab can change without notice, so the lists are rebuilt when this tab shows.
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel main)
            {
                main.Security.Refresh();
            }
        };
    }
}
