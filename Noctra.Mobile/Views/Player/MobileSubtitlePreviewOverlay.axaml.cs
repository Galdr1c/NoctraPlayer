using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Noctra.Mobile.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Views.Player;

public partial class MobileSubtitlePreviewOverlay : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MobileSubtitlePreviewOverlay, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<int> BackgroundOpacityProperty =
        AvaloniaProperty.Register<MobileSubtitlePreviewOverlay, int>(nameof(BackgroundOpacity), 0);

    public static readonly StyledProperty<SubtitleVerticalPosition> PositionProperty =
        AvaloniaProperty.Register<MobileSubtitlePreviewOverlay, SubtitleVerticalPosition>(
            nameof(Position), SubtitleVerticalPosition.Bottom);

    private static readonly SubtitleBackgroundBrushConverter BackgroundConverter = new();

    public MobileSubtitlePreviewOverlay()
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
        {
            SubtitleText.Text = Text;
        }
        else if (change.Property == FontSizeProperty)
        {
            SubtitleText.FontSize = FontSize;
        }
        else if (change.Property == PositionProperty || change.Property == BackgroundOpacityProperty)
        {
            Sync();
        }
    }

    private void Sync()
    {
        var row = Position switch
        {
            SubtitleVerticalPosition.Top => 0,
            SubtitleVerticalPosition.UpperMiddle => 1,
            SubtitleVerticalPosition.LowerMiddle => 2,
            _ => 3
        };

        Grid.SetRow(SubtitleBorder, row);

        if (BackgroundConverter.Convert(BackgroundOpacity, typeof(IBrush), null!, null!) is IBrush brush)
        {
            SubtitleBorder.Background = brush;
        }
    }
}
