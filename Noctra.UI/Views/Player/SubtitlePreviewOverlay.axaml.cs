using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Noctra.Models;

namespace Noctra.UI.Views.Player;

public partial class SubtitlePreviewOverlay : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SubtitlePreviewOverlay, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<int> BackgroundOpacityProperty =
        AvaloniaProperty.Register<SubtitlePreviewOverlay, int>(nameof(BackgroundOpacity), 0);

    public static readonly StyledProperty<SubtitleVerticalPosition> PositionProperty =
        AvaloniaProperty.Register<SubtitlePreviewOverlay, SubtitleVerticalPosition>(
            nameof(Position), SubtitleVerticalPosition.Bottom);

    public SubtitlePreviewOverlay()
    {
        InitializeComponent();
        Sync();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int BackgroundOpacity
    {
        get => GetValue(BackgroundOpacityProperty);
        set => SetValue(BackgroundOpacityProperty, value);
    }

    public SubtitleVerticalPosition Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
            SubtitleText.Text = Text;
        else if (change.Property == FontSizeProperty)
            SubtitleText.FontSize = FontSize;
        else if (change.Property == PositionProperty || change.Property == BackgroundOpacityProperty)
            Sync();
    }

    private void Sync()
    {
        Grid.SetRow(SubtitleBorder, Position switch
        {
            SubtitleVerticalPosition.Top => 0,
            SubtitleVerticalPosition.UpperMiddle => 1,
            SubtitleVerticalPosition.LowerMiddle => 2,
            _ => 3
        });

        var opacity = Math.Clamp(BackgroundOpacity, 0, 100) / 100d;
        SubtitleBorder.Background = new SolidColorBrush(
            Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
    }
}
