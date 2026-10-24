using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    /// <summary>Keeps the "Saved / Unsaved changes" note next to the project name current.</summary>
    private readonly DispatcherTimer _saveStateTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        // Any input anywhere in the window is activity: it resets the session idle timer (keep-alive).
        // The handlers run even when a control has already handled the event.
        AddHandler(InputElement.PointerMovedEvent, OnUserInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerPressedEvent, OnUserInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyDownEvent, OnUserInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.TextInputEvent, OnUserInput, RoutingStrategies.Tunnel, handledEventsToo: true);

        _sessionTimer.Tick += (_, _) =>
        {
            if (DataContext is MainViewModel main)
            {
                main.CheckSession(DateTime.UtcNow);
                main.AutosaveIfDue(DateTime.UtcNow);
            }
        };
        _sessionTimer.Start();
        _saveStateTimer.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshSaveState();
        _saveStateTimer.Start();

        // Ctrl+K jumps to the search box; picking a result opens that section, table, field or report.
        AddHandler(InputElement.KeyDownEvent, OnSearchShortcut, RoutingStrategies.Tunnel);
        SearchBox.GotFocus += (_, _) =>
        {
            if (DataContext is MainViewModel main)
            {
                SearchBox.ItemsSource = main.SearchEntries();
            }
        };
        SearchBox.SelectionChanged += (_, _) =>
        {
            if (SearchBox.SelectedItem is SearchEntry entry && DataContext is MainViewModel main)
            {
                main.GoTo(entry);
                SearchBox.SelectedItem = null;
                SearchBox.Text = string.Empty;
            }
        };

        Closed += (_, _) =>
        {
            _sessionTimer.Stop();
            _saveStateTimer.Stop();

            // A site started from the Generate tab must not outlive the editor.
            (DataContext as MainViewModel)?.Generate.Shutdown();
        };

        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.ExitRequested -= OnExitRequested;
            }

            _subscribed = DataContext as MainViewModel;
            if (_subscribed is not null)
            {
                _subscribed.ExitRequested += OnExitRequested;
            }
        };
    }

    private void OnSearchShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private MainViewModel? _subscribed;
    private bool _exitConfirmed;

    private void OnExitRequested(object? sender, EventArgs e)
    {
        _exitConfirmed = true;
        Close();
    }

    /// <summary>Closing the window (the X button, Alt+F4) goes through the same unsaved-changes prompt as File > Exit.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_exitConfirmed || DataContext is not MainViewModel main || !main.IsDirty || !main.Settings.ConfirmOnExit)
        {
            return;
        }

        e.Cancel = true;
        _ = ConfirmThenCloseAsync(main);
    }

    private async System.Threading.Tasks.Task ConfirmThenCloseAsync(MainViewModel main)
    {
        if (await main.ConfirmExitAsync())
        {
            _exitConfirmed = true;
            Close();
        }
    }

    private void OnUserInput(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel main)
        {
            main.NoteActivity(DateTime.UtcNow);
        }
    }
}
