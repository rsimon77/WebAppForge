using Avalonia.Controls;
using Avalonia.Input;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class RelationsView : UserControl
{
    public RelationsView()
    {
        InitializeComponent();
    }

    /// <summary>Double-clicking a line of the overview selects that link (and its master table).</summary>
    private void OnOverviewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel main && (sender as ListBox)?.SelectedItem is OverviewRow row)
        {
            main.Relations.OpenOverviewItemCommand.Execute(row);
        }
    }
}
