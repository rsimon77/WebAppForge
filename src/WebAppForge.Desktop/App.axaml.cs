using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Services;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var main = new MainViewModel(
                new AvaloniaDialogService(window),
                new SettingsStore(SettingsStore.DefaultFilePath),
                new AvaloniaAppearance(this));
            main.ReportPreview = new CoreReportPreviewService(() => main.ProjectFolder);
            main.ReportValidation = new CoreReportValidationService();
            window.DataContext = main;
            desktop.MainWindow = window;
            main.ReopenLastProject();
        }

        base.OnFrameworkInitializationCompleted();
    }
}