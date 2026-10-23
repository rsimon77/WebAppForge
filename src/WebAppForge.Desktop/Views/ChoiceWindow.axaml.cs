using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WebAppForge.Desktop.Views;

/// <summary>Modal list of strings. In single mode it closes with the chosen item; in multiple mode with the ticked items. Cancel closes with null.</summary>
public partial class ChoiceWindow : Window
{
    private readonly bool _multiple;
    private readonly HashSet<string> _disabled;

    public ChoiceWindow()
        : this("Choose", string.Empty, [], multiple: false)
    {
    }

    public ChoiceWindow(string title, string message, IReadOnlyList<string> choices, bool multiple, IReadOnlyCollection<string>? disabled = null)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _disabled = new HashSet<string>(disabled ?? []);
        ChoiceList.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item && e.Index >= 0 && e.Index < choices.Count)
            {
                item.IsEnabled = !_disabled.Contains(choices[e.Index]);
            }
        };
        ChoiceList.ItemsSource = choices;
        ChoiceList.SelectionMode = multiple ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single;
        _multiple = multiple;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selected = ChoiceList.SelectedItems?.Cast<string>().Where(s => !_disabled.Contains(s)).ToList() ?? [];
        if (_multiple)
        {
            Close(selected);
        }
        else
        {
            Close(selected.FirstOrDefault());
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
