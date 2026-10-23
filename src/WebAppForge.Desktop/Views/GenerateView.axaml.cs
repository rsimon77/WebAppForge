using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class GenerateView : UserControl
{
    private GenerateViewModel? _subscribed;

    public GenerateView()
    {
        InitializeComponent();

        // Refresh the login check when this tab shows, since Generate flags may have changed on the Tables tab.
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel main)
            {
                main.Security.Refresh();
                main.Generate.RefreshSummary();
                main.Generate.RefreshToolStatus();
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.OutputLines.CollectionChanged -= OnOutputChanged;
            }

            _subscribed = (DataContext as MainViewModel)?.Generate;
            if (_subscribed is not null)
            {
                _subscribed.OutputLines.CollectionChanged += OnOutputChanged;
            }
        };

        DiagnosticsGrid.DoubleTapped += OnDiagnosticDoubleTapped;
    }

    private void OnOutputChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscribed is not { AutoScroll: true } || e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // After layout, so the new row exists.
        Dispatcher.UIThread.Post(() =>
        {
            var count = _subscribed?.OutputLines.Count ?? 0;
            if (count > 0)
            {
                OutputList.ScrollIntoView(count - 1);
            }
        }, DispatcherPriority.Background);
    }

    private void OnDiagnosticDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DiagnosticsGrid.SelectedItem is DiagnosticRow row && _subscribed is not null)
        {
            _subscribed.CopyDiagnosticCommand.Execute(row);
        }
    }
}
