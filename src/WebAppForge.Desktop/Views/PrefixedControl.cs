using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace WebAppForge.Desktop.Views;

/// <summary>
/// A reusable editor that appears several times on the Reports tab. Setting <see cref="IdPrefix"/> gives every named control
/// the automation id prefix + name, so each use stays addressable (for tests and UI automation).
/// </summary>
public class PrefixedControl : UserControl
{
    public static readonly StyledProperty<string> IdPrefixProperty =
        AvaloniaProperty.Register<PrefixedControl, string>(nameof(IdPrefix), string.Empty);

    public string IdPrefix
    {
        get => GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IdPrefixProperty)
        {
            foreach (var control in this.GetLogicalDescendants().OfType<Control>().Where(c => !string.IsNullOrEmpty(c.Name)))
            {
                AutomationProperties.SetAutomationId(control, IdPrefix + control.Name);
            }
        }
    }
}
