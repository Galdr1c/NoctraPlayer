using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Noctra.Avalonia.Views;

public partial class UpsellWindow : Window
{
    public UpsellWindow()
    {
        InitializeComponent();

        // Window dragging
        PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void Buy_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void ContinueFree_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
