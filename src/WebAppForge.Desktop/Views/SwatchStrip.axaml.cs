using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace WebAppForge.Desktop.Views;

/// <summary>The palette swatches of the colour pickers: clicking one runs <see cref="Command"/> with its CSS colour.</summary>
public partial class SwatchStrip : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SwatchStrip, ICommand?>(nameof(Command));

    public SwatchStrip()
    {
        InitializeComponent();
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
