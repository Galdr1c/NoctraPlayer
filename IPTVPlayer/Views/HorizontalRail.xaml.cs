using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IPTVPlayer.Views;

public partial class HorizontalRail : UserControl
{
    // Dependency Properties
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(HorizontalRail));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(HorizontalRail));

    public static readonly DependencyProperty ShowSeeAllProperty =
        DependencyProperty.Register(nameof(ShowSeeAll), typeof(bool), typeof(HorizontalRail), new PropertyMetadata(false));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool ShowSeeAll
    {
        get => (bool)GetValue(ShowSeeAllProperty);
        set => SetValue(ShowSeeAllProperty, value);
    }

    private const double ScrollAmount = 900; // 3 cards (300px each)

    public HorizontalRail()
    {
        InitializeComponent();
        Loaded += HorizontalRail_Loaded;
    }

    private void HorizontalRail_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateArrowVisibility();
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        double newOffset = Math.Max(0, ScrollViewer.HorizontalOffset - ScrollAmount);
        ScrollViewer.ScrollToHorizontalOffset(newOffset);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        double maxScroll = ScrollViewer.ScrollableWidth;
        double newOffset = Math.Min(maxScroll, ScrollViewer.HorizontalOffset + ScrollAmount);
        ScrollViewer.ScrollToHorizontalOffset(newOffset);
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateArrowVisibility();
    }

    private void UpdateArrowVisibility()
    {
        // Left arrow: Visible if not at start
        LeftArrow.Visibility = ScrollViewer.HorizontalOffset > 0 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        // Right arrow: Visible if not at end
        RightArrow.Visibility = ScrollViewer.HorizontalOffset < ScrollViewer.ScrollableWidth 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    // Mouse wheel horizontal scroll
    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ScrollViewer.ScrollToHorizontalOffset(ScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }
}
