using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Noctra.Mobile.Converters;
using Noctra.Models;

namespace Noctra.Mobile.Views.Player;

public partial class MobileSubtitleOverlay : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<SubtitleCueData>?> CuesProperty =
        AvaloniaProperty.Register<MobileSubtitleOverlay, IReadOnlyList<SubtitleCueData>?>(nameof(Cues));

    public static readonly StyledProperty<int> BackgroundOpacityProperty =
        AvaloniaProperty.Register<MobileSubtitleOverlay, int>(nameof(BackgroundOpacity), 0);

    public static readonly StyledProperty<SubtitleVerticalPosition> PositionProperty =
        AvaloniaProperty.Register<MobileSubtitleOverlay, SubtitleVerticalPosition>(
            nameof(Position), SubtitleVerticalPosition.Bottom);

    public static readonly StyledProperty<double> TopOffsetProperty =
        AvaloniaProperty.Register<MobileSubtitleOverlay, double>(nameof(TopOffset), 12.0);

    public static readonly StyledProperty<double> BottomOffsetProperty =
        AvaloniaProperty.Register<MobileSubtitleOverlay, double>(nameof(BottomOffset), 18.0);

    private static readonly SubtitleBackgroundBrushConverter BackgroundConverter = new();

    public MobileSubtitleOverlay()
    {
        InitializeComponent();
        CuesList.FontSize = FontSize;
        LayoutUpdated += OnLayoutUpdated;
        ApplyCues();
        ApplyPosition();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        SubtitleBorder.MaxWidth = Math.Max(0, Bounds.Width * 0.92);
    }

    public IReadOnlyList<SubtitleCueData>? Cues
    {
        get => GetValue(CuesProperty);
        set => SetValue(CuesProperty, value);
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

    public double TopOffset
    {
        get => GetValue(TopOffsetProperty);
        set => SetValue(TopOffsetProperty, value);
    }

    public double BottomOffset
    {
        get => GetValue(BottomOffsetProperty);
        set => SetValue(BottomOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CuesProperty)
        {
            ApplyCues();
        }
        else if (change.Property == PositionProperty ||
                 change.Property == TopOffsetProperty ||
                 change.Property == BottomOffsetProperty)
        {
            ApplyPosition();
        }
        else if (change.Property == BackgroundOpacityProperty)
        {
            ApplyAppearance();
        }
        else if (change.Property == FontSizeProperty)
        {
            CuesList.FontSize = FontSize;
        }
    }

    private void ApplyCues()
    {
        var cues = Cues;
        CuesList.ItemsSource = cues ?? [];
    }

    private void ApplyPosition()
    {
        LayoutGrid.Margin = new Thickness(
            0,
            Math.Max(0, TopOffset),
            0,
            Math.Max(0, BottomOffset));

        Grid.SetRow(SubtitleBorder, Position switch
        {
            SubtitleVerticalPosition.Top => 0,
            SubtitleVerticalPosition.UpperMiddle => 1,
            SubtitleVerticalPosition.LowerMiddle => 2,
            _ => 3
        });

        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        if (BackgroundConverter.Convert(BackgroundOpacity, typeof(IBrush), null!, null!) is IBrush brush)
        {
            SubtitleBorder.Background = brush;
        }
    }
}
