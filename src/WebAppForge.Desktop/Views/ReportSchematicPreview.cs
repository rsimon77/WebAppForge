using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

/// <summary>
/// Draws a rough picture of a report from a <see cref="ReportSchematic"/>: summary groups and totals, the crosstab matrix, a chart of
/// the chosen kind (Avalonia shapes, sample data), a month grid with sample events or the dashboard strip. Rebuilt on every change.
/// </summary>
public sealed class ReportSchematicPreview : UserControl
{
    public static readonly StyledProperty<ReportSchematic?> SchematicProperty =
        AvaloniaProperty.Register<ReportSchematicPreview, ReportSchematic?>(nameof(Schematic));

    private const double ChartWidth = 460;

    public ReportSchematicPreview()
    {
        Rebuild();
    }

    public ReportSchematic? Schematic
    {
        get => GetValue(SchematicProperty);
        set => SetValue(SchematicProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SchematicProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (Schematic is not { } schematic)
        {
            Content = new TextBlock { Text = "Select a report to see a preview.", Opacity = 0.7 };
            return;
        }

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        body.Children.Add(new TextBlock { Text = schematic.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        switch (schematic)
        {
            case SummarySchematic summary:
                AddFilters(body, summary.Filters);
                body.Children.Add(SummaryTable(summary));
                Note(body, summary.Note);
                break;
            case CrosstabSchematic crosstab:
                AddFilters(body, crosstab.Filters);
                body.Children.Add(CrosstabTable(crosstab));
                Note(body, crosstab.Note);
                break;
            case ChartSchematic chart:
                AddFilters(body, chart.Filters);
                body.Children.Add(ChartView(chart));
                Note(body, chart.Note);
                break;
            case CalendarSchematic calendar:
                AddFilters(body, calendar.Filters);
                body.Children.Add(CalendarView(calendar));
                Note(body, calendar.Note);
                break;
            case DashboardSchematic dashboard:
                body.Children.Add(new DashboardStrip { Boxes = dashboard.Boxes });
                body.Children.Add(new TextBlock
                {
                    Text = dashboard.RefreshSeconds > 0 ? $"Refreshes every {dashboard.RefreshSeconds} seconds." : "Does not refresh by itself.",
                    FontSize = 11,
                    Opacity = 0.6,
                });
                Note(body, dashboard.Note);
                break;
        }

        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = body };
        Themed(frame, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
        Content = frame;
    }

    private static void Note(Panel body, string note)
    {
        if (note.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = note, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }
    }

    private static void AddFilters(Panel body, IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
        {
            return;
        }

        var row = new WrapPanel();
        foreach (var filter in filters)
        {
            var box = new Border
            {
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 2),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock { Text = filter + " v", FontSize = 11 },
            };
            Themed(box, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
            row.Children.Add(box);
        }

        body.Children.Add(row);
    }

    // ---- tables ----

    private static Control Cell(string text, int row, int column, bool bold, bool shaded, bool topLine = false, int span = 1)
    {
        var cell = new Border
        {
            BorderThickness = new Thickness(0, topLine ? 1 : 0, 0, 1),
            Padding = new Thickness(6, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        Themed(cell, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
        if (shaded)
        {
            Themed(cell, Border.BackgroundProperty, "PanelBackground");
        }

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        Grid.SetColumnSpan(cell, span);
        return cell;
    }

    private static Grid TableGrid(int columns, int rows)
    {
        var grid = new Grid();
        for (var c = 0; c < Math.Max(1, columns); c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 60 });
        }

        for (var r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        return grid;
    }

    private static Control SummaryTable(SummarySchematic summary)
    {
        var columns = Math.Max(summary.Headers.Count, summary.Rows.Max(r => r.Cells.Count));
        var grid = TableGrid(columns, summary.Rows.Count + 1);
        for (var c = 0; c < summary.Headers.Count; c++)
        {
            grid.Children.Add(Cell(summary.Headers[c], 0, c, bold: true, shaded: true));
        }

        for (var r = 0; r < summary.Rows.Count; r++)
        {
            var row = summary.Rows[r];
            if (row.Kind == SchematicRowKind.Group)
            {
                grid.Children.Add(Cell(row.Cells[0], r + 1, 0, bold: true, shaded: true, span: columns));
                continue;
            }

            var total = row.Kind is SchematicRowKind.GroupTotal or SchematicRowKind.GrandTotal;
            for (var c = 0; c < row.Cells.Count; c++)
            {
                grid.Children.Add(Cell(row.Cells[c], r + 1, c, bold: total, shaded: row.Kind == SchematicRowKind.GrandTotal, topLine: total));
            }
        }

        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grid };
    }

    private static Control CrosstabTable(CrosstabSchematic crosstab)
    {
        var grid = TableGrid(crosstab.Headers.Count, crosstab.Rows.Count + 1);
        for (var c = 0; c < crosstab.Headers.Count; c++)
        {
            grid.Children.Add(Cell(crosstab.Headers[c], 0, c, bold: true, shaded: true));
        }

        for (var r = 0; r < crosstab.Rows.Count; r++)
        {
            var row = crosstab.Rows[r];
            var total = row.Kind == SchematicRowKind.GrandTotal;
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var lastTotalColumn = crosstab.ShowRowTotals && c == row.Cells.Count - 1;
                grid.Children.Add(Cell(row.Cells[c], r + 1, c, bold: c == 0 || total || lastTotalColumn, shaded: c == 0, topLine: total));
            }
        }

        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grid };
    }

    // ---- calendar ----

    private static Control CalendarView(CalendarSchematic calendar)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{calendar.MonthTitle}   [{string.Join(" | ", calendar.Views)}]  default: {calendar.DefaultView}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
        });

        var columns = calendar.DayHeaders.Count;
        var grid = new Grid { MinWidth = columns * 56 };
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var _ in calendar.Weeks)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(54)));
        }

        for (var c = 0; c < columns; c++)
        {
            grid.Children.Add(Cell(calendar.DayHeaders[c], 0, c, bold: true, shaded: true));
        }

        for (var w = 0; w < calendar.Weeks.Count; w++)
        {
            for (var c = 0; c < calendar.Weeks[w].Count; c++)
            {
                var day = calendar.Weeks[w][c];
                var cell = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(3), Opacity = day.InMonth ? 1 : 0.4 };
                cell.Child = new TextBlock { Text = day.Number.ToString(), FontSize = 10, VerticalAlignment = VerticalAlignment.Top };
                Themed(cell, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
                Grid.SetRow(cell, w + 1);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        foreach (var ev in calendar.Events)
        {
            var bar = new Border
            {
                Height = 16,
                Margin = new Thickness(3, 20, 3, 0),
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 0),
                Child = new TextBlock { Text = ev.Title, FontSize = 10, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            if (ev.Color.Length > 0)
            {
                bar.Background = new SolidColorBrush(ReportPalette.Parse(ev.Color));
            }
            else
            {
                Themed(bar, Border.BackgroundProperty, "AppAccentBrush");
            }

            Grid.SetRow(bar, ev.Row + 1);
            Grid.SetColumn(bar, ev.Column);
            Grid.SetColumnSpan(bar, Math.Max(1, ev.Span));
            grid.Children.Add(bar);
        }

        panel.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = grid });
        var hints = new List<string>();
        hints.Add(calendar.AllowAdd ? "Click an empty day to add" : "Add is off");
        hints.Add(calendar.AllowOpen ? "click an event to open it" : "open is off");
        if (calendar.PopoverFields.Count > 0)
        {
            hints.Add("popover: " + string.Join(", ", calendar.PopoverFields));
        }

        panel.Children.Add(new TextBlock { Text = string.Join("; ", hints) + ".", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    // ---- chart ----

    private static Control ChartView(ChartSchematic chart)
    {
        var height = Math.Clamp(chart.Height * 0.55, 150, 300);
        var canvas = new Canvas { Width = ChartWidth, Height = height };
        var pieLike = chart.Kind is ChartKind.Pie or ChartKind.Doughnut or ChartKind.PolarArea;
        switch (chart.Kind)
        {
            case ChartKind.Pie:
            case ChartKind.Doughnut:
            case ChartKind.PolarArea:
                DrawRound(canvas, chart, height);
                break;
            case ChartKind.Radar:
                DrawRadar(canvas, chart, height);
                break;
            default:
                DrawCartesian(canvas, chart, height);
                break;
        }

        var plot = new Border { Padding = new Thickness(4), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left };
        Themed(plot, Border.BackgroundProperty, "PanelBackground");

        var legend = new WrapPanel();
        var entries = pieLike
            ? chart.Categories.Select((c, i) => (c, ReportPalette.ColorOf(i)))
            : chart.Series.Select(s => (s.Caption, s.Color));
        foreach (var (name, color) in entries)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 10, 2) };
            item.Children.Add(new Border { Width = 10, Height = 10, Background = new SolidColorBrush(ReportPalette.Parse(color)), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = name, FontSize = 11 });
            legend.Children.Add(item);
        }

        var root = new StackPanel { Spacing = 4 };
        root.Children.Add(new TextBlock { Text = $"{chart.Kind} chart (sample data){(chart.TrendLine ? ", trend line" : string.Empty)}", FontSize = 11, Opacity = 0.6 });
        if (chart.Legend == LegendPosition.Top)
        {
            root.Children.Add(legend);
        }

        if (chart.Legend is LegendPosition.Left or LegendPosition.Right)
        {
            var dock = new DockPanel();
            DockPanel.SetDock(legend, chart.Legend == LegendPosition.Left ? Dock.Left : Dock.Right);
            legend.MaxWidth = 140;
            dock.Children.Add(legend);
            dock.Children.Add(plot);
            root.Children.Add(dock);
        }
        else
        {
            root.Children.Add(plot);
        }

        if (chart.Legend == LegendPosition.Bottom)
        {
            root.Children.Add(legend);
        }

        return new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = root };
    }

    private static void Add(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        canvas.Children.Add(control);
    }

    private static TextBlock Label(string text, double size = 9, bool theme = true)
    {
        var block = new TextBlock { Text = text, FontSize = size, Opacity = 0.8 };
        return block;
    }

    private static IBrush Brush(string color) => new SolidColorBrush(ReportPalette.Parse(color));

    private static void Axis(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        var line = new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), StrokeThickness = 1, Opacity = 0.5 };
        line.Bind(Shape.StrokeProperty, line.GetResourceObservable("SystemControlForegroundBaseLowBrush"));
        canvas.Children.Add(line);
    }

    private static void DrawCartesian(Canvas canvas, ChartSchematic chart, double height)
    {
        var horizontal = chart.Kind is ChartKind.Bar or ChartKind.StackedBar;
        var stacked = chart.Kind is ChartKind.StackedColumn or ChartKind.StackedBar or ChartKind.StackedArea or ChartKind.PercentColumn;
        var percent = chart.Kind == ChartKind.PercentColumn;
        var left = horizontal ? 70.0 : 40.0;
        var plot = new Rect(left, 14, ChartWidth - left - 12, height - (horizontal ? 36 : 44));
        var n = chart.Categories.Count;
        if (n == 0 || chart.Series.Count == 0)
        {
            return;
        }

        double Total(int i) => chart.Series.Sum(s => s.Values[i]);
        double maxOf(ChartAxis axis)
        {
            var list = chart.Series.Where(s => s.Axis == axis).ToList();
            if (list.Count == 0)
            {
                return 1;
            }

            return stacked
                ? percent ? 1 : Enumerable.Range(0, n).Max(i => list.Sum(s => s.Values[i]))
                : list.Max(s => s.Values.Max());
        }

        var primaryMax = Math.Max(maxOf(ChartAxis.Primary), 1);
        var secondaryMax = Math.Max(maxOf(ChartAxis.Secondary), 1);

        // Maps (category position 0-1, value 0-1) to a point of the plot, whichever way the chart runs.
        Point P(double category, double value) => horizontal
            ? new Point(plot.X + value * plot.Width, plot.Y + category * plot.Height)
            : new Point(plot.X + category * plot.Width, plot.Bottom - value * plot.Height);

        Axis(canvas, plot.X, plot.Y, plot.X, plot.Bottom);
        Axis(canvas, plot.X, plot.Bottom, plot.Right, plot.Bottom);
        if (chart.Series.Any(s => s.Axis == ChartAxis.Secondary) && !horizontal)
        {
            Axis(canvas, plot.Right, plot.Y, plot.Right, plot.Bottom);
        }

        for (var i = 0; i < n; i++)
        {
            var center = (i + 0.5) / n;
            var text = Label(chart.Categories[i]);
            text.MaxWidth = horizontal ? 64 : plot.Width / n;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            var at = P(center, 0);
            if (horizontal)
            {
                Add(canvas, text, 2, at.Y - 6);
            }
            else
            {
                Add(canvas, text, at.X - text.MaxWidth / 2 + 2, plot.Bottom + 3);
            }
        }

        double Scale(SchematicSeries s, double v, int i)
        {
            if (percent)
            {
                return v / Math.Max(Total(i), 1);
            }

            return v / (s.Axis == ChartAxis.Secondary ? secondaryMax : primaryMax);
        }

        var barSeries = chart.Series.Where(s => IsBar(s.Kind)).ToList();
        var slot = 1.0 / n;
        var width = slot * 0.7;
        var stackBase = new double[n];
        for (var index = 0; index < chart.Series.Count; index++)
        {
            var s = chart.Series[index];
            var brush = Brush(s.Color);
            var points = new List<Point>();
            for (var i = 0; i < n; i++)
            {
                var v = Scale(s, s.Values[i], i);
                var center = (i + 0.5) * slot;
                if (IsBar(s.Kind))
                {
                    double from, to, a, b;
                    if (stacked)
                    {
                        from = stackBase[i];
                        to = from + v;
                        stackBase[i] = to;
                        a = center - width / 2;
                        b = center + width / 2;
                    }
                    else
                    {
                        var k = barSeries.IndexOf(s);
                        var part = width / Math.Max(barSeries.Count, 1);
                        a = center - width / 2 + k * part;
                        b = a + part;
                        from = 0;
                        to = v;
                    }

                    var p1 = P(a, from);
                    var p2 = P(b, to);
                    var rect = new Rectangle { Width = Math.Max(1, Math.Abs(p2.X - p1.X)), Height = Math.Max(1, Math.Abs(p2.Y - p1.Y)), Fill = brush };
                    Add(canvas, rect, Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y));
                    if (chart.ShowDataLabels)
                    {
                        Add(canvas, Label(s.Values[i].ToString("0")), Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y) - 11);
                    }
                }
                else
                {
                    var level = stacked ? (stackBase[i] += v) : v;
                    points.Add(P(center, Math.Min(level, 1.05)));
                    if (chart.ShowDataLabels)
                    {
                        var at = points[^1];
                        Add(canvas, Label(s.Values[i].ToString("0")), at.X - 6, at.Y - 12);
                    }
                }
            }

            if (points.Count > 0 && s.Kind is ChartKind.Area or ChartKind.StackedArea || (points.Count > 0 && stacked && !IsBar(s.Kind)))
            {
                var polygon = new Polygon
                {
                    Points = [.. points, P((n - 0.5) / n, 0), P(0.5 / n, 0)],
                    Fill = brush,
                    Opacity = 0.35,
                };
                canvas.Children.Add(polygon);
            }

            if (points.Count > 0)
            {
                canvas.Children.Add(new Polyline { Points = points, Stroke = brush, StrokeThickness = 2 });
                foreach (var point in points)
                {
                    Add(canvas, new Ellipse { Width = 6, Height = 6, Fill = brush }, point.X - 3, point.Y - 3);
                }
            }
        }

        if (chart.TrendLine && !stacked)
        {
            var first = chart.Series[0];
            var xs = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
            var ys = first.Values.ToArray();
            var mx = xs.Average();
            var my = ys.Average();
            var slope = xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / Math.Max(xs.Sum(x => (x - mx) * (x - mx)), 1e-9);
            double At(int i) => Math.Clamp((my + slope * (i - mx)) / (first.Axis == ChartAxis.Secondary ? secondaryMax : primaryMax), 0, 1);
            var trend = new Line { StartPoint = P(0.5 / n, At(0)), EndPoint = P((n - 0.5) / n, At(n - 1)), StrokeThickness = 1.5, StrokeDashArray = [4, 3] };
            trend.Bind(Shape.StrokeProperty, trend.GetResourceObservable("SystemControlForegroundBaseHighBrush"));
            canvas.Children.Add(trend);
        }

        if (chart.XAxisTitle.Length > 0)
        {
            Add(canvas, Label(chart.XAxisTitle, 10), plot.X + plot.Width / 2 - 30, height - 16);
        }

        if (chart.YAxisTitle.Length > 0)
        {
            var title = Label(chart.YAxisTitle, 10);
            title.RenderTransform = new RotateTransform(-90);
            Add(canvas, title, 0, plot.Y + plot.Height / 2 + 20);
        }
    }

    private static bool IsBar(ChartKind kind) => kind is ChartKind.Column or ChartKind.Bar or ChartKind.StackedColumn or ChartKind.StackedBar or ChartKind.PercentColumn;

    private static Path Wedge(Point center, double inner, double outer, double startDeg, double endDeg, IBrush fill)
    {
        Point At(double radius, double deg) => new(center.X + radius * Math.Cos((deg - 90) * Math.PI / 180), center.Y + radius * Math.Sin((deg - 90) * Math.PI / 180));
        var sweep = endDeg - startDeg;
        var figure = new PathFigure { StartPoint = At(outer, startDeg), IsClosed = true };
        figure.Segments = new PathSegments
        {
            new ArcSegment { Point = At(outer, endDeg - (sweep >= 360 ? 0.01 : 0)), Size = new Size(outer, outer), SweepDirection = SweepDirection.Clockwise, IsLargeArc = sweep > 180 },
            new LineSegment { Point = At(inner, endDeg - (sweep >= 360 ? 0.01 : 0)) },
        };
        if (inner > 0)
        {
            figure.Segments.Add(new ArcSegment { Point = At(inner, startDeg), Size = new Size(inner, inner), SweepDirection = SweepDirection.CounterClockwise, IsLargeArc = sweep > 180 });
        }

        return new Path { Data = new PathGeometry { Figures = [figure] }, Fill = fill, StrokeThickness = 1 };
    }

    private static void DrawRound(Canvas canvas, ChartSchematic chart, double height)
    {
        if (chart.Series.Count == 0 || chart.Categories.Count == 0)
        {
            return;
        }

        var values = chart.Series[0].Values;
        var radius = Math.Min(height - 20, 200) / 2;
        var center = new Point(ChartWidth / 2, height / 2);
        var n = chart.Categories.Count;
        if (chart.Kind == ChartKind.PolarArea)
        {
            var max = values.Max();
            for (var i = 0; i < n; i++)
            {
                var r = radius * Math.Sqrt(values[i] / max);
                canvas.Children.Add(Wedge(center, 0, r, 360.0 / n * i, 360.0 / n * (i + 1), Brush(ReportPalette.ColorOf(i))));
            }

            return;
        }

        var total = values.Sum();
        var angle = 0.0;
        for (var i = 0; i < n; i++)
        {
            var sweep = values[i] / total * 360;
            var wedge = Wedge(center, chart.Kind == ChartKind.Doughnut ? radius * 0.55 : 0, radius, angle, angle + sweep, Brush(ReportPalette.ColorOf(i)));
            canvas.Children.Add(wedge);
            if (chart.ShowDataLabels)
            {
                var mid = (angle + sweep / 2 - 90) * Math.PI / 180;
                var at = new Point(center.X + radius * 0.78 * Math.Cos(mid), center.Y + radius * 0.78 * Math.Sin(mid));
                Add(canvas, Label($"{values[i] / total:P0}", 9), at.X - 10, at.Y - 6);
            }

            angle += sweep;
        }
    }

    private static void DrawRadar(Canvas canvas, ChartSchematic chart, double height)
    {
        var n = chart.Categories.Count;
        if (n < 3 || chart.Series.Count == 0)
        {
            return;
        }

        var radius = Math.Min(height - 40, 200) / 2;
        var center = new Point(ChartWidth / 2, height / 2);
        Point At(int i, double fraction) => new(
            center.X + radius * fraction * Math.Cos((360.0 / n * i - 90) * Math.PI / 180),
            center.Y + radius * fraction * Math.Sin((360.0 / n * i - 90) * Math.PI / 180));

        foreach (var ring in new[] { 0.33, 0.66, 1.0 })
        {
            var polygon = new Polygon { Points = Enumerable.Range(0, n).Select(i => At(i, ring)).ToList(), StrokeThickness = 1, Opacity = 0.4 };
            polygon.Bind(Shape.StrokeProperty, polygon.GetResourceObservable("SystemControlForegroundBaseLowBrush"));
            canvas.Children.Add(polygon);
        }

        for (var i = 0; i < n; i++)
        {
            var outer = At(i, 1.12);
            Add(canvas, Label(chart.Categories[i]), outer.X - 14, outer.Y - 6);
        }

        var max = chart.Series.Max(s => s.Values.Max());
        foreach (var s in chart.Series)
        {
            var brush = Brush(s.Color);
            canvas.Children.Add(new Polygon
            {
                Points = Enumerable.Range(0, n).Select(i => At(i, s.Values[i] / max)).ToList(),
                Stroke = brush,
                StrokeThickness = 2,
                Fill = brush,
                Opacity = 0.45,
            });
        }
    }

    private static void Themed(Control control, AvaloniaProperty property, string resourceKey)
        => control.Bind(property, control.GetResourceObservable(resourceKey));
}
