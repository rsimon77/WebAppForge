using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using WebAppForge.Core.Model;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

/// <summary>
/// Draws a rough picture of a generated page from a <see cref="PageSchematic"/>: the heading, the buttons, the list header
/// with sample rows or the form fields in the chosen layout. It is rebuilt whenever the schematic changes.
/// </summary>
public sealed class SchematicPreview : UserControl
{
    public static readonly StyledProperty<PageSchematic?> SchematicProperty =
        AvaloniaProperty.Register<SchematicPreview, PageSchematic?>(nameof(Schematic));

    public SchematicPreview()
    {
        Rebuild();
    }

    public PageSchematic? Schematic
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
        if (Schematic is not { } page)
        {
            Content = new TextBlock { Text = "Pick a table to see a preview.", Opacity = 0.7 };
            return;
        }

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        if (page.HasHeaderHtml)
        {
            body.Children.Add(Placeholder("Header HTML"));
        }

        body.Children.Add(new TextBlock { Text = page.Heading, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (page.Subtitle.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = page.Subtitle, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        }

        if (page.TopButtons.Count > 0)
        {
            body.Children.Add(Buttons(page.TopButtons));
        }

        if (page.Note.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = page.Note, TextWrapping = TextWrapping.Wrap });
        }

        if (page.Kind == PageKind.List)
        {
            body.Children.Add(page.ListLayout == ListLayout.Cards ? CardList(page) : TableList(page));
            if (page.ShowPager)
            {
                body.Children.Add(new TextBlock { Text = "<  1  2  3  >", HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.8 });
            }

            body.Children.Add(new TextBlock { Text = $"Empty list: {page.EmptyText}", Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            var isDetails = page.Kind is PageKind.View or PageKind.Delete;
            Control Fields(IReadOnlyList<SchematicField> fields) => isDetails ? Details(fields) : Form(page, fields);
            body.Children.Add(page.MultiPage is { } multi ? MultiPageBody(page, multi, Fields) : Fields(page.Fields));
            foreach (var grid in page.DetailGrids)
            {
                body.Children.Add(DetailGrid(grid));
            }
        }

        if (page.BottomButtons.Count > 0)
        {
            body.Children.Add(Buttons(page.BottomButtons));
        }

        if (page.HasFooterHtml)
        {
            body.Children.Add(Placeholder("Footer HTML"));
        }

        var frame = new Border
        {
            BorderThickness = new Thickness(page.Container == PageContainer.Offcanvas ? 4 : 1, 1, 1, 1),
            CornerRadius = new CornerRadius(page.Container == PageContainer.Modal ? 12 : 4),
            Child = body,
        };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SystemControlForegroundBaseLowBrush"));

        var outer = new StackPanel { Spacing = 6 };
        outer.Children.Add(new TextBlock
        {
            Text = page.Container switch
            {
                PageContainer.Modal => "Opens in a modal dialog over the list",
                PageContainer.Offcanvas => "Opens in an offcanvas panel beside the list",
                _ => "Opens as a full page",
            },
            FontSize = 11,
            Opacity = 0.6,
        });
        outer.Children.Add(frame);
        Content = outer;
    }

    private Control Placeholder(string text)
    {
        var box = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2),
            Child = new TextBlock { Text = text, FontSize = 11, Opacity = 0.7 },
        };
        Themed(box, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
        return box;
    }

    private Control Buttons(IReadOnlyList<string> names)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (var i = 0; i < names.Count; i++)
        {
            var button = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 3),
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = names[i], FontSize = 12, Foreground = i == 0 ? Brushes.White : null },
            };
            Themed(button, Border.BorderBrushProperty, i == 0 ? "AppAccentBrush" : "SystemControlForegroundBaseLowBrush");
            if (i == 0)
            {
                Themed(button, Border.BackgroundProperty, "AppAccentBrush");
            }

            row.Children.Add(button);
        }

        return row;
    }

    private Control TableList(PageSchematic page)
    {
        var grid = new Grid();
        var count = Math.Max(1, page.Headers.Count);
        for (var column = 0; column < count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var row = 0; row < page.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var cellBorder = page.Bordered ? new Thickness(1) : new Thickness(0, 0, 0, 1);
        for (var column = 0; column < page.Headers.Count; column++)
        {
            var header = new Border
            {
                BorderThickness = cellBorder,
                Padding = new Thickness(6, 4),
                Child = new TextBlock { Text = page.Headers[column], FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            Themed(header, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
            Themed(header, Border.BackgroundProperty, "PanelBackground");
            Grid.SetColumn(header, column);
            grid.Children.Add(header);
        }

        for (var row = 0; row < page.Rows.Count; row++)
        {
            for (var column = 0; column < page.Rows[row].Count; column++)
            {
                var cell = new Border
                {
                    BorderThickness = cellBorder,
                    Padding = new Thickness(6, 3),
                    Child = new TextBlock { Text = page.Rows[row][column], FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                };
                Themed(cell, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
                if (page.Striped && row % 2 == 1)
                {
                    Themed(cell, Border.BackgroundProperty, "PanelBackground");
                }

                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }

        var panel = new StackPanel { Spacing = 2 };
        var flags = new List<string>();
        if (page.StickyHeader)
        {
            flags.Add("sticky header");
        }

        if (page.Hover)
        {
            flags.Add("hover");
        }

        if (page.ListLayout == ListLayout.Compact)
        {
            flags.Add("compact");
        }

        if (flags.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = string.Join(", ", flags), FontSize = 11, Opacity = 0.6 });
        }

        panel.Children.Add(grid);
        return panel;
    }

    private Control CardList(PageSchematic page)
    {
        var cards = new WrapPanel();
        foreach (var row in page.Rows)
        {
            var lines = new StackPanel { Spacing = 2 };
            for (var i = 0; i < row.Count && i < page.Headers.Count; i++)
            {
                lines.Children.Add(new TextBlock { Text = $"{page.Headers[i]}: {row[i]}", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            }

            var card = new Border
            {
                Width = 170,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = lines,
            };
            Themed(card, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
            cards.Children.Add(card);
        }

        return cards;
    }

    /// <summary>The pages of a multi-page form: a tab strip, pills, accordion headers or wizard steps, each page with its field boxes and grids.</summary>
    private Control MultiPageBody(PageSchematic page, MultiPageSchematic multi, Func<IReadOnlyList<SchematicField>, Control> fields)
    {
        var root = new StackPanel { Spacing = 8 };
        var mode = multi.Mode;
        if (mode is MultiPageMode.Tabs or MultiPageMode.Pills)
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = mode == MultiPageMode.Pills ? 6 : 2 };
            for (var i = 0; i < multi.Pages.Count; i++)
            {
                strip.Children.Add(StripItem(PageTitle(multi.Pages[i]), i == 0, mode == MultiPageMode.Pills));
            }

            root.Children.Add(strip);
        }
        else if (mode == MultiPageMode.Wizard)
        {
            var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            for (var i = 0; i < multi.Pages.Count; i++)
            {
                if (i > 0)
                {
                    steps.Children.Add(new TextBlock { Text = "-", Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
                }

                steps.Children.Add(StripItem($"{i + 1}  {PageTitle(multi.Pages[i])}", i == 0, pill: true));
            }

            root.Children.Add(steps);
        }

        for (var i = 0; i < multi.Pages.Count; i++)
        {
            var current = multi.Pages[i];
            var content = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
            if (current.Description.Length > 0)
            {
                content.Children.Add(new TextBlock { Text = current.Description, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            }

            content.Children.Add(current.Fields.Count == 0
                ? new TextBlock { Text = "No fields on this page.", Opacity = 0.6, FontSize = 11 }
                : fields(current.Fields));
            foreach (var grid in current.Grids)
            {
                content.Children.Add(DetailGrid(grid));
            }

            if (mode == MultiPageMode.Wizard)
            {
                var last = i == multi.Pages.Count - 1;
                var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                navigation.Children.Add(SmallButton("Previous", enabled: i > 0));
                navigation.Children.Add(SmallButton(last ? Submit(page) : "Next", enabled: true, primary: true));
                content.Children.Add(navigation);
            }

            var heading = mode switch
            {
                MultiPageMode.Accordion => $"v  {PageTitle(current)}",
                MultiPageMode.Wizard => $"Step {i + 1} of {multi.Pages.Count}: {PageTitle(current)}",
                _ => PageTitle(current),
            };
            var block = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
            Themed(block, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
            var inner = new StackPanel();
            var header = new Border { Padding = new Thickness(8, 4), Child = new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, FontSize = 12 } };
            Themed(header, Border.BackgroundProperty, "PanelBackground");
            inner.Children.Add(header);
            inner.Children.Add(content);
            block.Child = inner;
            root.Children.Add(block);
        }

        return root;
    }

    private static string PageTitle(SchematicPage page) => page.Icon.Length > 0 ? $"[{page.Icon}] {page.Caption}" : page.Caption;

    private static string Submit(PageSchematic page) => page.TopButtons.Count > 0 ? page.TopButtons[0] : page.BottomButtons.Count > 0 ? page.BottomButtons[0] : "Save";

    private Control StripItem(string text, bool selected, bool pill)
    {
        var item = new Border
        {
            Padding = new Thickness(10, 3),
            BorderThickness = pill ? new Thickness(0) : new Thickness(1, 1, 1, selected ? 0 : 1),
            CornerRadius = pill ? new CornerRadius(12) : new CornerRadius(4, 4, 0, 0),
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = selected && pill ? Brushes.White : null },
        };
        Themed(item, Border.BorderBrushProperty, selected ? "AppAccentBrush" : "SystemControlForegroundBaseLowBrush");
        if (selected && pill)
        {
            Themed(item, Border.BackgroundProperty, "AppAccentBrush");
        }

        return item;
    }

    private Control SmallButton(string text, bool enabled, bool primary = false)
    {
        var button = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 3),
            BorderThickness = new Thickness(1),
            Opacity = enabled ? 1 : 0.4,
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = primary ? Brushes.White : null },
        };
        Themed(button, Border.BorderBrushProperty, primary ? "AppAccentBrush" : "SystemControlForegroundBaseLowBrush");
        if (primary)
        {
            Themed(button, Border.BackgroundProperty, "AppAccentBrush");
        }

        return button;
    }

    /// <summary>A placeholder for a detail grid: its heading, an Add button and the column headers.</summary>
    private Control DetailGrid(SchematicGrid grid)
    {
        var stack = new StackPanel { Spacing = 4 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = grid.Caption, FontWeight = FontWeight.SemiBold, FontSize = 12 });
        if (grid.CanAdd)
        {
            title.Children.Add(new TextBlock { Text = "[+ Add]", FontSize = 11, Opacity = 0.7 });
        }

        stack.Children.Add(title);
        var headers = new UniformGrid { Columns = Math.Max(1, grid.Headers.Count) };
        foreach (var header in grid.Headers)
        {
            headers.Children.Add(new TextBlock { Text = header, FontSize = 11, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        stack.Children.Add(headers);
        stack.Children.Add(new TextBlock { Text = "detail rows ...", FontSize = 11, Opacity = 0.5 });
        var box = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 4), Child = stack };
        Themed(box, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
        return box;
    }

    private Control Details(IReadOnlyList<SchematicField> fieldList)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        for (var i = 0; i < fieldList.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = fieldList[i].Caption, FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 2) };
            var value = new TextBlock { Text = "sample value", FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 2) };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return grid;
    }

    private Control Form(PageSchematic page, IReadOnlyList<SchematicField> pageFields)
    {
        var sections = new StackPanel { Spacing = 8 };
        string? currentGroup = null;
        var fields = new List<SchematicField>();

        void Flush()
        {
            if (fields.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(currentGroup))
            {
                sections.Children.Add(new TextBlock { Text = currentGroup, FontWeight = FontWeight.SemiBold, Opacity = 0.8 });
            }

            var cells = new UniformGrid { Columns = Math.Max(1, page.FormColumns) };
            foreach (var field in fields)
            {
                cells.Children.Add(FieldCell(field, page.FormLayout == FormLayout.Horizontal));
            }

            sections.Children.Add(cells);
            fields.Clear();
        }

        foreach (var field in pageFields)
        {
            if (fields.Count > 0 && field.Group != currentGroup)
            {
                Flush();
            }

            currentGroup = field.Group;
            fields.Add(field);
        }

        Flush();
        if (pageFields.Count == 0)
        {
            sections.Children.Add(new TextBlock { Text = "No fields are shown on this form.", Opacity = 0.6 });
        }

        return sections;
    }

    private Control FieldCell(SchematicField field, bool horizontal)
    {
        var label = new TextBlock { Text = field.Caption, FontSize = 12, Width = horizontal ? 110 : double.NaN, VerticalAlignment = VerticalAlignment.Center };
        var input = SampleInput(field.Control);
        var cell = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 10, 6), Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
        if (horizontal)
        {
            cell.Spacing = 8;
        }

        if (field.Control == "Checkbox")
        {
            cell.Children.Add(input);
            cell.Children.Add(label);
            cell.Orientation = Orientation.Horizontal;
            cell.Spacing = 6;
        }
        else
        {
            cell.Children.Add(label);
            cell.Children.Add(input);
        }

        return cell;
    }

    private Control SampleInput(string control)
    {
        switch (control)
        {
            case "Checkbox":
            {
                var box = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
                Themed(box, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
                return box;
            }

            case "Radio":
                return new TextBlock { Text = "( ) one   ( ) two", FontSize = 12, Opacity = 0.8 };
            case "Hidden":
                return new TextBlock { Text = "(hidden)", FontSize = 11, Opacity = 0.5 };
            case "ReadOnly":
                return new TextBlock { Text = "read-only text", FontSize = 12, Opacity = 0.8 };
        }

        var text = control switch
        {
            "Select" => "Choose...   v",
            "Password" => "********",
            "Date" => "yyyy-mm-dd",
            "DateTime" => "yyyy-mm-dd hh:mm",
            "Time" => "hh:mm",
            "Number" => "0",
            "Email" => "name@example.com",
            "Url" => "https://",
            "Phone" => "+1 555 0100",
            "Color" => "#000000",
            _ => "text",
        };
        var box2 = new Border
        {
            MinHeight = control == "TextArea" ? 52 : 26,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3),
            Child = new TextBlock { Text = text, FontSize = 12, Opacity = 0.5 },
        };
        Themed(box2, Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");
        return box2;
    }

    private static void Themed(Control control, AvaloniaProperty property, string resourceKey)
        => control.Bind(property, control.GetResourceObservable(resourceKey));
}
