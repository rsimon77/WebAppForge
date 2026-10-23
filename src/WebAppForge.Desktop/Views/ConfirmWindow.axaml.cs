using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WebAppForge.Desktop.Views;

/// <summary>Modal question with a confirm button and Cancel. The result is true only for the confirm button.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
        : this("Confirm", string.Empty, "OK")
    {
    }

    public ConfirmWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
