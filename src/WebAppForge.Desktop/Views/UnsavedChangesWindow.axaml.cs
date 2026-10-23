using Avalonia.Controls;
using Avalonia.Interactivity;
using WebAppForge.Desktop.Services;

namespace WebAppForge.Desktop.Views;

/// <summary>Modal Save, Discard, Cancel prompt. The result is the button clicked; closing the window counts as Cancel.</summary>
public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Save);

    private void OnDiscardClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Discard);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);
}
