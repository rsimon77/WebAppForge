using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WebAppForge.Desktop.ViewModels;

namespace WebAppForge.Desktop.Views;

/// <summary>
/// The live layout of a dashboard: a 12-column strip with each item drawn as a box labelled with its caption, in its span,
/// wrapping onto new rows like Bootstrap. Rebuilt whenever <see cref="Boxes"/> changes.
/// </summary>
public sealed class DashboardStrip : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<DashboardBox>?> BoxesProperty =
        AvaloniaProperty.Register<DashboardStrip, IReadOnlyList<DashboardBox>?>(nameof(Boxes));

    public DashboardStrip()
    {
        Rebuild();
    }

    public IReadOnlyList<DashboardBox>? Boxes
    {
        get => GetValue(BoxesProperty);
        set => SetValue(BoxesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoxesProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var boxes = Boxes ?? [];
        var rows = new StackPanel { Spacing = 4 };
        if (boxes.Count == 0)
        {
            rows.Children.Add(new TextBlock { Text = "No items yet.", Opacity = 0.7 });
        }

        foreach (var group in boxes.GroupBy(b => b.Row).OrderBy(g => g.Key))
        {
            var grid = new Grid { MinWidth = 12 * 30 };
            for (var i = 0; i < DashboardLayout.Columns; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            foreach (var box in group)
            {
                var label = new TextBlock
                {
                    Text = box.Caption,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 12,
                };
                var border = new Border
                {
                    Margin = new Thickness(2),
                    Padding = new Thickness(6, 8),
                    MinHeight = box.Height > 0 ? System.Math.Clamp(box.Height / 4.0, 30, 120) : 40,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Child = label,
                };
                border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("AppAccentBrush"));
                border.Bind(Border.BackgroundProperty, border.GetResourceObservable("PanelBackground"));
                ToolTip.SetTip(border, $"{box.Caption} - span {box.Span}");
                AutomationProperties.SetAutomationId(border, $"DashboardBox{box.Index}");
                Grid.SetColumn(border, box.Column);
                Grid.SetColumnSpan(border, box.Span);
                grid.Children.Add(border);
            }

            rows.Children.Add(grid);
        }

        Content = rows;
    }
}
