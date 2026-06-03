using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Noctra.Avalonia.Views;

public partial class ReviewPromptWindow : Window
{
    public ReviewPromptWindow()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Rate_Click(object? sender, RoutedEventArgs e)
    {
        Close(ReviewPromptResult.RateNow);
    }

    private void Later_Click(object? sender, RoutedEventArgs e)
    {
        Close(ReviewPromptResult.Later);
    }

    private void Never_Click(object? sender, RoutedEventArgs e)
    {
        Close(ReviewPromptResult.Never);
    }
}

public enum ReviewPromptResult
{
    Later,
    Never,
    RateNow
}
