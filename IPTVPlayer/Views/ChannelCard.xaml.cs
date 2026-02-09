using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using IPTVPlayer.Models;

namespace IPTVPlayer.Views;

public partial class ChannelCard : UserControl
{
    public static readonly DependencyProperty ChannelProperty =
        DependencyProperty.Register(nameof(Channel), typeof(Channel), typeof(ChannelCard));

    public Channel Channel
    {
        get => (Channel)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    public event EventHandler<Channel>? PlayRequested;
    public event EventHandler<Channel>? AddToListRequested;
    public event EventHandler<Channel>? MoreInfoRequested;

    public ChannelCard()
    {
        InitializeComponent();
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        var storyboard = (Storyboard)Resources["HoverIn"];
        storyboard.Begin();

        // Shadow animation
        var shadowAnim = new DoubleAnimation
        {
            To = 0.6,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);

        // Bring to front
        Panel.SetZIndex(this, 100);
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        var storyboard = (Storyboard)Resources["HoverOut"];
        storyboard.Begin();

        // Shadow animation
        var shadowAnim = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, shadowAnim);

        // Reset Z-index
        Panel.SetZIndex(this, 0);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        PlayRequested?.Invoke(this, Channel);
    }

    private void AddToListButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AddToListRequested?.Invoke(this, Channel);
    }

    private void MoreInfoButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MoreInfoRequested?.Invoke(this, Channel);
    }
}
