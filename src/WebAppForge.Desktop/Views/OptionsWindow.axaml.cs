using Avalonia.Controls;
using Avalonia.Interactivity;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

/// <summary>
/// Modal Application Options dialog. Its DataContext is an <see cref="OptionsViewModel"/>. OK applies and closes with
/// true; Apply applies and stays open; Cancel (or closing the window) closes with false. Changes already applied stay.
/// </summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    private OptionsViewModel Options => (OptionsViewModel)DataContext!;

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Options.Ok())
        {
            Close(true);
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e) => Options.Apply();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
