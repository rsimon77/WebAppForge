using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Fonts.Inter;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebAppForge.Desktop.Localization;
using WebAppForge.Desktop.Tests.TestSupport;
using WebAppForge.Desktop.ViewModels;
using WebAppForge.Desktop.Views;

namespace WebAppForge.Desktop.Tests.Headless;

/// <summary>The real main window in Hungarian: live switching, no untranslated keys, and texts that fit.</summary>
[Collection(UiLanguageCollection.Name)]
public class UiLanguageTests
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu");
    private static readonly Regex RawKey = new("^[A-Z][A-Za-z]+_[A-Za-z0-9]+$");

    [AvaloniaFact]
    public void SwitchingTheLanguageUpdatesTheOpenWindowAtOnce()
    {
        var (main, _) = CustomizationTests.Project();
        var window = Open(main);
        Settle(window);
        try
        {
            Assert.Contains("File", MenuHeaders(window));
            Assert.Contains("Tables", TabHeaders(window));

            Localizer.Instance.SetCulture(Hungarian);
            Settle(window);
            Assert.Contains("Fájl", MenuHeaders(window));
            Assert.Contains("Táblák", TabHeaders(window));
            Assert.StartsWith("Project1 > Adatbázis", main.Breadcrumb, StringComparison.Ordinal);
        }
        finally
        {
            Localizer.Instance.SetCulture(Localizer.NeutralCulture);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EveryTabInHungarianShowsTranslatedTextThatFits()
    {
        var (main, _) = CustomizationTests.Project();
        var window = Open(main);
        Localizer.Instance.SetCulture(Hungarian);
        var problems = new List<string>();
        try
        {
            main.Tables.SelectedTable = main.Current.Tables[0];
            for (var i = 0; i < MainViewModel.TabNames.Count; i++)
            {
                main.ActiveTabIndex = i;
                Settle(window);
                foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                {
                    if (RawKey.IsMatch(text.Text!))
                    {
                        problems.Add($"{MainViewModel.TabNames[i]}: untranslated key {text.Text}");
                    }
                    else if (IsClipped(text))
                    {
                        problems.Add($"{MainViewModel.TabNames[i]}: '{text.Text}' needs {NaturalWidth(text):0}px, has {text.Bounds.Width:0}px");
                    }
                }
            }
        }
        finally
        {
            Localizer.Instance.SetCulture(Localizer.NeutralCulture);
            window.Close();
        }

        Assert.True(problems.Count == 0, "Hungarian texts that do not fit or are missing:\n" + string.Join("\n", problems.Distinct()));
    }

    /// <summary>
    /// A one-line text without trimming that is wider than the space it got is cut off. A grid column header draws its text
    /// across the whole header cell (the sort arrow space is empty until the column is sorted), so the cell is the limit there.
    /// </summary>
    private static bool IsClipped(TextBlock text)
    {
        if (text.TextWrapping != TextWrapping.NoWrap || text.TextTrimming != TextTrimming.None || text.Bounds.Width <= 0)
        {
            return false;
        }

        var header = text.FindAncestorOfType<DataGridColumnHeader>();
        var room = header is null ? text.Bounds.Width + 1 : header.Bounds.Width - HeaderInset;
        return NaturalWidth(text) > room;
    }

    /// <summary>Left padding of a column header plus a little space before the next column.</summary>
    private const double HeaderInset = 16;

    private static double NaturalWidth(TextBlock text)
    {
        var probe = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            FontFamily = text.FontFamily,
            LetterSpacing = text.LetterSpacing,
            Padding = text.Padding,
        };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }

    /// <summary>The main window at 1100 x 720, drawn with Inter (the app's font, WithInterFont in Program.cs) rather than a machine font.</summary>
    private static MainWindow Open(MainViewModel main)
    {
        FontManager.Current.AddFontCollection(new InterFontCollection());
        var window = new MainWindow { DataContext = main, FontFamily = new FontFamily("fonts:Inter#Inter"), Width = 1100, Height = 720 };
        window.Show();
        return window;
    }

    private static List<string?> MenuHeaders(Window window)
        => window.GetVisualDescendants().OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();

    private static List<string?> TabHeaders(Window window)
        => window.GetVisualDescendants().OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();
        }
    }
}
